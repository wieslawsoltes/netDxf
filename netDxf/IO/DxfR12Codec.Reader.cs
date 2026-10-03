// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        /// <summary>Reads supported R12 model-space primitives into the existing typed entity classes.</summary>
        /// <param name="document">An immutable AC1009 raw document.</param>
        /// <returns>Detached entities in ENTITIES order, sharing decoded layer objects by name.</returns>
        /// <remarks>
        /// This explicitly selects ENTITIES and their basic layer settings; it is not whole-document
        /// conversion. It does not expand blocks or interpret unrelated sections/HEADER settings.
        /// Unsupported entities, paper-space records, application data, unknown entity fields and
        /// ambiguous fields reject the entire selection. The raw source remains unchanged and retains
        /// every unselected record. Optional entity handles are retained on the detached objects but
        /// are newly assigned when Create authors a new drawing. Normal directions are normalized;
        /// OCS circle/arc centers are converted to world coordinates. Ordinary unsmoothed 2D and 3D
        /// POLYLINE sequences are decoded as typed polylines. Default widths are materialized into
        /// vertex overrides; child handles are checked for uniqueness but remain only in the raw
        /// document. Reauthoring allocates new child identities. Unsupported child metadata and
        /// fitted/mesh sequences reject. No external resources are read.
        /// </remarks>
        public static IReadOnlyList<EntityObject> ReadEntities(DxfRawDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.Version != DxfVersion.AutoCad12)
                throw new NotSupportedException("This typed primitive codec requires the AC1009 R11/R12 format family.");
            var layers = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);
            DxfRawSection entities = null;
            bool sawTables = false;
            foreach (DxfRawSection section in document.Sections)
            {
                if (string.Equals(section.Name, "ENTITIES", StringComparison.OrdinalIgnoreCase))
                {
                    if (entities != null) throw new FormatException("Duplicate ENTITIES sections are ambiguous.");
                    entities = section;
                }
                if (!string.Equals(section.Name, "TABLES", StringComparison.OrdinalIgnoreCase)) continue;
                if (sawTables) throw new FormatException("Duplicate TABLES sections are ambiguous.");
                sawTables = true;
                string table = null;
                foreach (DxfRawRecord record in section.Records)
                {
                    if (string.Equals(record.Name, "TABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (table != null) throw new FormatException("Nested table declarations are invalid.");
                        table = new Fields(record).Text(2, null, true);
                    }
                    else if (string.Equals(record.Name, "ENDTAB", StringComparison.OrdinalIgnoreCase))
                    {
                        if (table == null) throw new FormatException("Unmatched ENDTAB.");
                        table = null;
                    }
                    else if (string.Equals(record.Name, "LAYER", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.Equals(table, "LAYER", StringComparison.OrdinalIgnoreCase))
                            throw new FormatException("A LAYER record is outside its table.");
                        Layer layer = ReadLayer(record);
                        if (layers.ContainsKey(layer.Name)) throw new FormatException("Duplicate layer names are ambiguous.");
                        layers.Add(layer.Name, layer);
                    }
                    else if (string.Equals(record.Name, "LTYPE", StringComparison.OrdinalIgnoreCase))
                    {
                        var fields = new Fields(record);
                        string name = fields.Text(2, null, true);
                        if (!string.Equals(name, "CONTINUOUS", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(name, "BYLAYER", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(name, "BYBLOCK", StringComparison.OrdinalIgnoreCase)) continue;
                        fields.Identity(); fields.Text(3, "");
                        if (fields.Integer(70, 0) != 0 || fields.Integer(72, 65) != 65
                            || fields.Integer(73, 0) != 0 || fields.Number(40, 0) != 0)
                            throw new NotSupportedException("A built-in linetype has unsupported pattern data.");
                        fields.Finish();
                    }
                }
                if (table != null) throw new FormatException("Missing ENDTAB.");
            }
            if (entities == null) throw new FormatException("R12 primitive reading requires an ENTITIES section.");
            var result = new List<EntityObject>();
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < entities.Records.Count; index++)
            {
                DxfRawRecord record = entities.Records[index];
                var fields = new Fields(record);
                string handle = fields.Identity();
                if (handle != null && !handles.Add(handle)) throw new FormatException("Duplicate entity identities are ambiguous.");
                string layerName = ResourceName(fields.Text(8, "0"));
                string linetype = BuiltinLinetype(fields.Text(6, "BYLAYER"));
                short color = fields.Integer(62, 256);
                if (color < 0 || color > 256) throw new NotSupportedException("Unsupported entity color index.");
                if (fields.Integer(67, 0) != 0) throw new NotSupportedException("Paper-space selection requires layout-aware import.");
                Vector3 normal = UnitNormal(fields.Vector(210, Vector3.UnitZ, false));
                double thickness = fields.Number(39, 0);
                EntityObject entity;
                switch (record.Name.ToUpperInvariant())
                {
                    case "POLYLINE":
                        entity = ReadPolyline(fields, entities.Records, ref index, layerName, normal, thickness, handles);
                        break;
                    case "LINE":
                        entity = new Line(fields.Vector(10, Vector3.Zero, true), fields.Vector(11, Vector3.Zero, true))
                            { Thickness = thickness, Normal = normal };
                        break;
                    case "POINT":
                        entity = new netDxf.Entities.Point(fields.Vector(10, Vector3.Zero, true))
                            { Thickness = thickness, Normal = normal, Rotation = 360.0 - fields.Number(50, 0) };
                        break;
                    case "CIRCLE":
                        entity = new Circle(ToWorld(fields.Vector(10, Vector3.Zero, true), normal), fields.Number(40, 0, true))
                            { Thickness = thickness, Normal = normal };
                        break;
                    case "ARC":
                        entity = new Arc(ToWorld(fields.Vector(10, Vector3.Zero, true), normal), fields.Number(40, 0, true),
                            fields.Number(50, 0, true), fields.Number(51, 0, true)) { Thickness = thickness, Normal = normal };
                        break;
                    case "3DFACE":
                        if (normal.X != 0 || normal.Y != 0 || normal.Z != 1 || thickness != 0)
                            throw new NotSupportedException("3DFACE has no supported extrusion or thickness override.");
                        Vector3 a = fields.Vector(10, Vector3.Zero, true), b = fields.Vector(11, Vector3.Zero, true);
                        Vector3 c = fields.Vector(12, Vector3.Zero, true), d = fields.Vector(13, c, false);
                        short flags = fields.Integer(70, 0);
                        if ((flags & ~15) != 0) throw new NotSupportedException("Unknown 3DFACE edge flags.");
                        entity = new Face3D(a, b, c, d) { EdgeFlags = (Face3DEdgeFlags)flags };
                        break;
                    case "SOLID": case "TRACE":
                        Vector3 q0 = fields.Vector(10, Vector3.Zero, true), q1 = fields.Vector(11, Vector3.Zero, true);
                        Vector3 q2 = fields.Vector(12, Vector3.Zero, true), q3 = fields.Vector(13, q2, false);
                        if (q0.Z != q1.Z || q0.Z != q2.Z || q0.Z != q3.Z)
                            throw new NotSupportedException("Planar R12 primitives require a single OCS elevation.");
                        if (string.Equals(record.Name, "SOLID", StringComparison.OrdinalIgnoreCase))
                            entity = new Solid(new Vector2(q0.X, q0.Y), new Vector2(q1.X, q1.Y), new Vector2(q2.X, q2.Y), new Vector2(q3.X, q3.Y))
                                { Elevation = q0.Z, Thickness = thickness, Normal = normal };
                        else
                            entity = new Trace(new Vector2(q0.X, q0.Y), new Vector2(q1.X, q1.Y), new Vector2(q2.X, q2.Y), new Vector2(q3.X, q3.Y))
                                { Elevation = q0.Z, Thickness = thickness, Normal = normal };
                        break;
                    default: throw new NotSupportedException("Unsupported R12 entity: " + record.Name);
                }
                fields.Finish();
                if (!layers.TryGetValue(layerName, out Layer layer))
                {
                    layer = new Layer(layerName); layers.Add(layerName, layer);
                }
                entity.Layer = layer;
                entity.Linetype = linetype == "BYLAYER" ? Linetype.ByLayer : linetype == "BYBLOCK" ? Linetype.ByBlock : Linetype.Continuous;
                entity.Color = AciColor.FromCadIndex(color); entity.Handle = handle;
                result.Add(entity);
            }
            return new ReadOnlyCollection<EntityObject>(result);
        }

        private static Layer ReadLayer(DxfRawRecord record)
        {
            var fields = new Fields(record);
            string name = ResourceName(fields.Text(2, null, true)); fields.Identity();
            short flags = fields.Integer(70, 0), color = fields.Integer(62, 7);
            if ((flags & ~5) != 0 || color == 0 || color < -255 || color > 255
                || BuiltinLinetype(fields.Text(6, "CONTINUOUS")) != "CONTINUOUS")
                throw new NotSupportedException("Unsupported R12 layer flags, color or linetype.");
            fields.Finish();
            return new Layer(name) { Color = AciColor.FromCadIndex((short)Math.Abs(color)), IsVisible = color > 0,
                IsFrozen = (flags & 1) != 0, IsLocked = (flags & 4) != 0 };
        }

        private sealed class Fields
        {
            private readonly Dictionary<short, DxfTag> remaining = new Dictionary<short, DxfTag>();
            private readonly string name;
            internal Fields(DxfRawRecord record)
            {
                this.name = record.Name;
                if (record.MarkerCode != 0) throw new FormatException("An entity or table record must use group 0.");
                for (int i = 1; i < record.Tags.Count; i++)
                {
                    DxfTag tag = record.Tags[i];
                    if (tag.Code == 999) continue;
                    if (this.remaining.ContainsKey(tag.Code)) throw new FormatException("Repeated field in " + this.name + ": " + tag.Code);
                    this.remaining.Add(tag.Code, tag);
                }
            }
            private object Take(short code, object fallback, bool required)
            {
                if (!this.remaining.TryGetValue(code, out DxfTag tag))
                {
                    if (required) throw new FormatException("Missing required group " + code + " in " + this.name);
                    return fallback;
                }
                this.remaining.Remove(code); return tag.Value;
            }
            internal string Text(short code, string fallback, bool required = false) { return (string)this.Take(code, fallback, required); }
            internal double? OptionalNumber(short code)
            { return this.remaining.ContainsKey(code) ? (double?)this.Number(code, 0) : null; }
            internal double Number(short code, double fallback, bool required = false) { return Finite((double)this.Take(code, fallback, required)); }
            internal short Integer(short code, short fallback) { return (short)this.Take(code, fallback, false); }
            internal Vector3 Vector(short code, Vector3 fallback, bool required)
            {
                return new Vector3(this.Number(code, fallback.X, required), this.Number((short)(code + 10), fallback.Y, required),
                    this.Number((short)(code + 20), fallback.Z));
            }
            internal string Identity()
            {
                string value = this.Text(5, null);
                if (value == null) return null;
                if (value.Length == 0 || value.Length > 16 || !ulong.TryParse(value, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out ulong number) || number == 0)
                    throw new FormatException("Invalid R12 object identity.");
                return number.ToString("X", CultureInfo.InvariantCulture);
            }
            internal void Finish()
            {
                foreach (short code in this.remaining.Keys)
                    throw new NotSupportedException("Uninterpreted R12 " + this.name + " group " + code + "; use raw preservation for this record.");
            }
        }
    }
}
