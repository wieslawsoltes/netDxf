// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace netDxf.IO
{
    /// <summary>Explicit typed R12 interchange for LINE, POINT, CIRCLE, ARC, 3DFACE, SOLID, TRACE, ordinary 2D/3D POLYLINE, polygon/polyface meshes and TEXT/STYLE.</summary>
    /// <remarks>
    /// Creates a new model-space primitive drawing, not an implicit downgrade of an entire DxfDocument.
    /// Geometry, indexed entity colors, basic layers and simple dash/dot/gap linetypes are supported. Unsupported
    /// entity types, modern attributes and dependency-bearing metadata reject instead of being dropped.
    /// Source objects, handles and owners are not modified. Output identities are newly allocated.
    /// The existing raw codec supplies AC1009 text/binary framing, encoding and bounded serialization.
    /// TEXT supports all fifteen alignments, OCS placement, rotation, mirroring, width and oblique
    /// factors, named styles and C0/caret escaping. Font names are stored references, not resolved
    /// resources. No glyph metrics or rendered appearance are computed. Character encoding follows
    /// the raw codec (new drawings use Windows-1252). Unsupported data rejects instead of disappearing.
    /// Ordinary polygon grids and indexed polyfaces use classic VERTEX/SEQEND chains. Grid order,
    /// U/V closure, stored density hints, signed face indices and effective face colors/layers are
    /// preserved. Polyface count hints are advisory; forward/interleaved faces are supported. New
    /// output groups coordinates before faces. Face styling is materialized and ignored face-point
    /// coordinates are discarded. Retained child metadata and fitted-surface sequences reject.
    /// Simple LTYPE definitions preserve signed element values and descriptions, share decoded
    /// references by name, and reject inconsistent definitions. Embedded text/shape patterns and
    /// external dependencies require another format codec. Redundant total pattern length is
    /// recomputed on output; decoding tolerates producer rounding within 1e-12 relative plus one
    /// binary64 subnormal quantum per term. The informational referenced flag is not retained.
    /// DxfDocument's admitted versions and the lossless raw-preservation API remain unchanged.
    /// </remarks>
    public static partial class DxfR12Codec
    {
        /// <summary>Creates an immutable R12 drawing from supported typed primitives.</summary>
        /// <param name="entities">Primitives to copy, in output order; enumeration completes before publication.</param>
        /// <param name="binary">Preferred output transport.</param>
        /// <param name="options">Existing raw tag, byte and string budgets.</param>
        /// <returns>A new AC1009 raw drawing with typed primitive semantics.</returns>
        public static DxfRawDocument Create(IEnumerable<EntityObject> entities, bool binary = false, DxfRawOptions options = null)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            var writer = new PrimitiveWriter(options ?? new DxfRawOptions());
            foreach (EntityObject entity in entities) writer.Entity(entity);
            return writer.Complete(binary);
        }

        /// <summary>Stages and saves a new R12 primitive drawing without changing source entities.</summary>
        /// <param name="stream">Writable destination left open.</param>
        /// <param name="entities">Supported primitives.</param>
        /// <param name="binary">Requested output transport.</param>
        /// <param name="options">Raw processing budgets.</param>
        /// <param name="cancellationToken">Checked while enumerating and by raw serialization/copying.</param>
        /// <remarks>
        /// Validation, iterator failure and cancellation before copying leave the destination untouched.
        /// IO failure or cancellation during copying may leave partial output; existing suffixes are not
        /// truncated. Use the raw document's atomic file save for filesystem replacement semantics.
        /// </remarks>
        public static void Save(Stream stream, IEnumerable<EntityObject> entities, bool binary = false,
            DxfRawOptions options = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("A writable stream is required.", nameof(stream));
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            cancellationToken.ThrowIfCancellationRequested();
            Create(CheckedEntities(entities, cancellationToken), binary, options).Save(stream, binary, cancellationToken);
        }

        private static IEnumerable<EntityObject> CheckedEntities(IEnumerable<EntityObject> entities, CancellationToken token)
        {
            foreach (EntityObject entity in entities) { token.ThrowIfCancellationRequested(); yield return entity; }
            token.ThrowIfCancellationRequested();
        }

        private static void Metadata(DxfObject item)
        {
            if (item.ExtensionDictionary != null || item.PersistentReactors.Count != 0 || item.XData.Count != 0)
                throw new NotSupportedException("R12 primitive interchange cannot discard attached application data or dependencies.");
        }

        private static string ResourceName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 31)
                throw new NotSupportedException("R12 primitive resource names require 1 through 31 ASCII characters.");
            foreach (char c in name)
                if (c < 33 || c > 126 || "<>/\\\":;?*|=,`".IndexOf(c) >= 0)
                    throw new NotSupportedException("The resource name is outside this R12 symbol-name profile.");
            return name;
        }

        private static string BuiltinLinetype(string name)
        {
            if (string.Equals(name, "ByLayer", StringComparison.OrdinalIgnoreCase)) return "BYLAYER";
            if (string.Equals(name, "ByBlock", StringComparison.OrdinalIgnoreCase)) return "BYBLOCK";
            if (string.Equals(name, "Continuous", StringComparison.OrdinalIgnoreCase)) return "CONTINUOUS";
            throw new NotSupportedException("A child record requires the supported built-in linetype selection.");
        }

        private static double Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "R12 primitive geometry must be finite.");
            return value;
        }

        private static Vector3 FinitePoint(Vector3 point)
        { Finite(point.X); Finite(point.Y); Finite(point.Z); return point; }

        private static Vector3 UnitNormal(Vector3 value)
        {
            FinitePoint(value);
            double scale = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
            if (scale == 0) throw new ArgumentException("Extrusion direction must be nonzero.");
            double x = value.X / scale, y = value.Y / scale, z = value.Z / scale;
            double length = Math.Sqrt(x * x + y * y + z * z);
            return new Vector3(x / length, y / length, z / length);
        }

        private static Vector3 ToObject(Vector3 value, Vector3 normal)
        {
            FinitePoint(value);
            if (normal.X == 0 && normal.Y == 0 && normal.Z == 1) return value;
            Matrix3 axes = MathHelper.ArbitraryAxis(normal);
            return FinitePoint(new Vector3(axes.M11 * value.X + axes.M21 * value.Y + axes.M31 * value.Z,
                axes.M12 * value.X + axes.M22 * value.Y + axes.M32 * value.Z,
                axes.M13 * value.X + axes.M23 * value.Y + axes.M33 * value.Z));
        }

        private static Vector3 ToWorld(Vector3 value, Vector3 normal)
        {
            if (normal.X == 0 && normal.Y == 0 && normal.Z == 1) return value;
            return FinitePoint(MathHelper.ArbitraryAxis(normal) * value);
        }

        private sealed class LayerPacket
        {
            internal string Name, PatternName;
            internal short Color, Flags;
            internal static LayerPacket Capture(Layer layer)
            {
                if (layer == null || layer.GetType() != typeof(Layer)) throw new NotSupportedException("A plain Layer is required.");
                Metadata(layer); Metadata(layer.Linetype);
                if (layer.Color.UseTrueColor || !layer.Plot || layer.Lineweight != Lineweight.Default
                    || layer.Transparency.Value != 0 || layer.Transparency.StoredAlphaValue.HasValue
                    || layer.Description.Length != 0)
                    throw new NotSupportedException("The layer contains settings outside the supported R12 layer profile.");
                return new LayerPacket { Name = ResourceName(layer.Name), PatternName = LinetypeName(layer.Linetype.Name),
                    Color = (short)(layer.IsVisible ? layer.Color.Index : -layer.Color.Index),
                    Flags = (short)((layer.IsFrozen ? 1 : 0) | (layer.IsLocked ? 4 : 0)) };
            }
        }

        private sealed partial class PrimitiveWriter
        {
            private readonly DxfRawOptions options;
            private readonly List<DxfTag> body = new List<DxfTag>();
            private readonly Dictionary<string, LayerPacket> layers = new Dictionary<string, LayerPacket>(StringComparer.OrdinalIgnoreCase);
            private readonly List<LayerPacket> orderedLayers = new List<LayerPacket>();
            private long nextHandle = 256;
            private int tags;
            internal PrimitiveWriter(DxfRawOptions options) { this.options = options; }
            private void Add(List<DxfTag> destination, short code, object value)
            {
                if (++this.tags > this.options.MaximumTags) throw new InvalidDataException("R12 primitive output exceeds the raw tag budget.");
                if (value is string text && text.Length > this.options.MaximumStringLength)
                    throw new InvalidDataException("R12 primitive output exceeds the raw string budget.");
                if (value is double number) Finite(number);
                destination.Add(new DxfTag(code, value));
            }
            private void Tag(short code, object value) { this.Add(this.body, code, value); }
            private void Point(short code, Vector3 point)
            {
                FinitePoint(point); this.Tag(code, point.X); this.Tag((short)(code + 10), point.Y); this.Tag((short)(code + 20), point.Z);
            }
            private void Plane(Vector3 normal, double thickness)
            { this.Tag(39, Finite(thickness)); this.Point(210, normal); }
            internal void Entity(EntityObject entity)
            {
                if (entity == null) throw new ArgumentException("A primitive cannot be null.");
                Type type = entity.GetType();
                if (type != typeof(Line) && type != typeof(netDxf.Entities.Point) && type != typeof(Circle)
                    && type != typeof(Arc) && type != typeof(Face3D) && type != typeof(Solid) && type != typeof(Trace)
                    && type != typeof(Polyline2D) && type != typeof(Polyline3D) && type != typeof(Text)
                    && type != typeof(PolygonMesh) && type != typeof(PolyfaceMesh))
                    throw new NotSupportedException("Unsupported R12 primitive type: " + type.FullName);
                Metadata(entity); Metadata(entity.Linetype);
                if (entity.Color.UseTrueColor || entity.ColorName != null || entity.ShadowMode.HasValue
                    || entity.CommonData.ProxyGraphics != null || entity.Reactors.Count != 0
                    || !entity.IsVisible || entity.Lineweight != Lineweight.ByLayer || entity.LinetypeScale != 1
                    || !entity.Transparency.IsByLayer || entity.Transparency.StoredAlphaValue.HasValue)
                    throw new NotSupportedException("The primitive has attributes that cannot be represented by this R12 codec.");
                LayerPacket layer = this.RegisterLayer(entity.Layer);
                string linetype = this.RegisterLinetype(entity.Linetype);
                Vector3 normal = UnitNormal(entity.Normal);
                this.Tag(0, entity is Polyline2D || entity is Polyline3D ? "POLYLINE" : entity.CodeName); this.Tag(5, (this.nextHandle++).ToString("X", CultureInfo.InvariantCulture));
                this.Tag(8, layer.Name); this.Tag(6, linetype); this.Tag(62, entity.Color.Index);
                if (entity is Text text) this.TextEntity(text, normal);
                else if (entity is Polyline2D polyline2D) this.Polyline(polyline2D, layer.Name, normal);
                else if (entity is Polyline3D polyline3D) this.Polyline(polyline3D, layer.Name, normal);
                else if (entity is PolygonMesh polygon) this.Polygon(polygon, layer.Name, normal);
                else if (entity is PolyfaceMesh polyface) this.Polyface(polyface, layer.Name, normal);
                else if (entity is Line line)
                { this.Point(10, line.StartPoint); this.Point(11, line.EndPoint); this.Plane(normal, line.Thickness); }
                else if (entity is netDxf.Entities.Point point)
                {
                    this.Point(10, point.Position); this.Plane(normal, point.Thickness);
                    this.Tag(50, Finite(360.0 - point.Rotation));
                }
                else if (entity is Circle circle)
                {
                    this.Point(10, ToObject(circle.Center, normal)); this.Tag(40, Finite(circle.Radius));
                    this.Plane(normal, circle.Thickness);
                }
                else if (entity is Arc arc)
                {
                    this.Point(10, ToObject(arc.Center, normal)); this.Tag(40, Finite(arc.Radius));
                    this.Plane(normal, arc.Thickness); this.Tag(50, Finite(arc.StartAngle)); this.Tag(51, Finite(arc.EndAngle));
                }
                else if (entity is Face3D face)
                {
                    if (normal.X != 0 || normal.Y != 0 || normal.Z != 1 || ((int)face.EdgeFlags & ~15) != 0)
                        throw new NotSupportedException("3DFACE requires its default normal and known edge flags.");
                    this.Point(10, face.FirstVertex); this.Point(11, face.SecondVertex);
                    this.Point(12, face.ThirdVertex); this.Point(13, face.FourthVertex); this.Tag(70, (short)face.EdgeFlags);
                }
                else if (entity is Solid solid)
                {
                    this.Quad(solid.FirstVertex, solid.SecondVertex, solid.ThirdVertex, solid.FourthVertex, solid.Elevation);
                    this.Plane(normal, solid.Thickness);
                }
                else if (entity is Trace trace)
                {
                    this.Quad(trace.FirstVertex, trace.SecondVertex, trace.ThirdVertex, trace.FourthVertex, trace.Elevation);
                    this.Plane(normal, trace.Thickness);
                }
            }
            private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, double elevation)
            {
                this.Point(10, new Vector3(a.X, a.Y, elevation)); this.Point(11, new Vector3(b.X, b.Y, elevation));
                this.Point(12, new Vector3(c.X, c.Y, elevation)); this.Point(13, new Vector3(d.X, d.Y, elevation));
            }
            internal DxfRawDocument Complete(bool binary)
            {
                if (!this.layers.ContainsKey("0"))
                {
                    if (this.layers.Count == short.MaxValue) throw new NotSupportedException("R12 layer-table count limit exceeded.");
                    this.orderedLayers.Insert(0, LayerPacket.Capture(Layer.Default));
                }
                var prefix = new List<DxfTag>();
                this.Add(prefix, 0, "SECTION"); this.Add(prefix, 2, "HEADER");
                this.Add(prefix, 9, "$ACADVER"); this.Add(prefix, 1, "AC1009");
                this.Add(prefix, 9, "$DWGCODEPAGE"); this.Add(prefix, 3, "ANSI_1252");
                this.Add(prefix, 9, "$HANDSEED"); this.Add(prefix, 5, this.nextHandle.ToString("X", CultureInfo.InvariantCulture));
                this.Add(prefix, 0, "ENDSEC"); this.Add(prefix, 0, "SECTION"); this.Add(prefix, 2, "TABLES");
                this.WriteLinetypes(prefix);
                this.Add(prefix, 0, "TABLE"); this.Add(prefix, 2, "LAYER");
                this.Add(prefix, 70, (short)this.orderedLayers.Count);
                foreach (LayerPacket layer in this.orderedLayers)
                {
                    this.Add(prefix, 0, "LAYER"); this.Add(prefix, 2, layer.Name); this.Add(prefix, 70, layer.Flags);
                    this.Add(prefix, 62, layer.Color); this.Add(prefix, 6, layer.PatternName);
                }
                this.Add(prefix, 0, "ENDTAB"); this.WriteTextStyles(prefix); this.Add(prefix, 0, "ENDSEC");
                this.Add(prefix, 0, "SECTION"); this.Add(prefix, 2, "BLOCKS"); this.Add(prefix, 0, "ENDSEC");
                this.Add(prefix, 0, "SECTION"); this.Add(prefix, 2, "ENTITIES");
                this.Tag(0, "ENDSEC"); this.Tag(0, "EOF");
                return DxfRawDocument.Create(Join(prefix, this.body), binary, this.options);
            }
            private static IEnumerable<DxfTag> Join(List<DxfTag> prefix, List<DxfTag> body)
            { foreach (DxfTag tag in prefix) yield return tag; foreach (DxfTag tag in body) yield return tag; }
        }
    }
}
