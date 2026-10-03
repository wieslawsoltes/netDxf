// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using netDxf.Entities;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        private static double Width(double value)
        {
            Finite(value);
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Polyline width must be nonnegative.");
            return value;
        }

        private sealed partial class PrimitiveWriter
        {
            private void Child(string kind, string layer)
            {
                this.Tag(0, kind);
                this.Tag(5, (this.nextHandle++).ToString("X", CultureInfo.InvariantCulture));
                this.Tag(8, layer);
            }

            private void Polyline(Polyline2D polyline, string layer, Vector3 normal)
            {
                if (polyline.HasStoredRecords || polyline.SmoothType != PolylineSmoothType.NoSmooth
                    || ((int)polyline.Flags & ~129) != 0)
                    throw new NotSupportedException("R12 selection cannot discard retained child metadata or fitted polyline semantics.");
                // A nonzero lightweight constant width masks vertex overrides. Materialize
                // effective widths into the legacy vertices, without editing the source.
                bool constant = polyline.ConstantWidth.GetValueOrDefault() > 0;
                double? start = polyline.LegacyDefaultStartWidth, end = polyline.LegacyDefaultEndWidth;
                if (start.HasValue) Width(start.Value);
                if (end.HasValue) Width(end.Value);
                this.Tag(66, (short)1);
                this.Point(10, new Vector3(0, 0, polyline.Elevation));
                this.Tag(70, (short)((polyline.IsClosed ? 1 : 0) | (polyline.LinetypeGeneration ? 128 : 0)));
                if (!constant && start.HasValue) this.Tag(40, start.Value);
                if (!constant && end.HasValue) this.Tag(41, end.Value);
                this.Plane(normal, polyline.Thickness);
                for (int i = 0; i < polyline.Vertexes.Count; i++)
                {
                    Polyline2DVertex vertex = polyline.Vertexes[i];
                    if (vertex == null || vertex.GetType() != typeof(Polyline2DVertex) || vertex.VertexIdentifier.HasValue)
                        throw new NotSupportedException("R12 vertices require plain coordinates without modern/private identifiers.");
                    // Validate masked values too: corruption must not be hidden by ConstantWidth.
                    Width(vertex.StartWidth); Width(vertex.EndWidth); Finite(vertex.Bulge);
                    this.Child("VERTEX", layer);
                    this.Point(10, new Vector3(vertex.Position.X, vertex.Position.Y, 0));
                    this.Tag(70, (short)0);
                    if (constant || vertex.StartWidthOverride.HasValue)
                        this.Tag(40, Width(constant ? polyline.ConstantWidth.Value : vertex.StartWidth));
                    if (constant || vertex.EndWidthOverride.HasValue)
                        this.Tag(41, Width(constant ? polyline.ConstantWidth.Value : vertex.EndWidth));
                    this.Tag(42, vertex.Bulge);
                }
                this.Child("SEQEND", layer);
            }

            private void Polyline(Polyline3D polyline, string layer, Vector3 normal)
            {
                if (polyline.HasStoredRecords || polyline.SmoothType != PolylineSmoothType.NoSmooth
                    || ((int)polyline.Flags & ~137) != 0)
                    throw new NotSupportedException("R12 selection cannot discard retained child metadata or fitted polyline semantics.");
                this.Tag(66, (short)1); this.Point(10, Vector3.Zero);
                this.Tag(70, (short)(8 | (polyline.IsClosed ? 1 : 0) | (polyline.LinetypeGeneration ? 128 : 0)));
                this.Point(210, normal);
                foreach (Vector3 vertex in polyline.Vertexes)
                {
                    this.Child("VERTEX", layer); this.Point(10, vertex); this.Tag(70, (short)32);
                }
                this.Child("SEQEND", layer);
            }
        }

        private static EntityObject ReadPolyline(Fields header, IReadOnlyList<DxfRawRecord> records,
            ref int index, string layer, Vector3 normal, double thickness, HashSet<string> handles)
        {
            short follows = header.Integer(66, 1), flags = header.Integer(70, 0);
            if ((follows != 0 && follows != 1) || (flags & ~137) != 0)
                throw new NotSupportedException("Only ordinary unsmoothed R12 2D/3D polyline sequences are supported.");
            bool spatial = (flags & 8) != 0;
            Vector3 origin = header.Vector(10, Vector3.Zero, false);
            double? start = header.OptionalNumber(40), end = header.OptionalNumber(41);
            if (start.HasValue) Width(start.Value);
            if (end.HasValue) Width(end.Value);
            if (origin.X != 0 || origin.Y != 0 || (spatial && (origin.Z != 0 || thickness != 0
                || start.GetValueOrDefault() != 0 || end.GetValueOrDefault() != 0)))
                throw new NotSupportedException("Polyline header geometry cannot be represented by the selected typed model.");
            foreach (short code in new short[] { 71, 72, 73, 74, 75 })
                if (header.Integer(code, 0) != 0)
                    throw new NotSupportedException("Mesh counts, fitting and surface parameters require a different codec.");
            header.Finish();
            var planar = spatial ? null : new List<Polyline2DVertex>();
            var points = spatial ? new List<Vector3>() : null;
            bool terminated = false;
            while (++index < records.Count)
            {
                DxfRawRecord record = records[index];
                bool terminator = string.Equals(record.Name, "SEQEND", StringComparison.OrdinalIgnoreCase);
                if (!terminator && !string.Equals(record.Name, "VERTEX", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("A POLYLINE sequence must end with SEQEND before another entity.");
                var child = new Fields(record);
                string identity = child.Identity();
                if (identity != null && !handles.Add(identity))
                    throw new FormatException("Duplicate identity in an R12 polyline sequence.");
                if (!string.Equals(child.Text(8, layer), layer, StringComparison.OrdinalIgnoreCase)
                    || child.Integer(67, 0) != 0 || child.Integer(62, 256) != 256
                    || BuiltinLinetype(child.Text(6, "BYLAYER")) != "BYLAYER")
                    throw new NotSupportedException("Child layer/space overrides cannot be discarded during typed selection.");
                if (terminator) { child.Finish(); terminated = true; break; }
                short vertexFlags = child.Integer(70, 0);
                if (vertexFlags != (spatial ? 32 : 0))
                    throw new NotSupportedException("Unexpected fitted, mesh or dimensional vertex flags.");
                Vector3 position = child.Vector(10, Vector3.Zero, true);
                double? localStart = child.OptionalNumber(40), localEnd = child.OptionalNumber(41);
                double bulge = child.Number(42, 0);
                if (localStart.HasValue) Width(localStart.Value);
                if (localEnd.HasValue) Width(localEnd.Value);
                child.Finish();
                if (spatial)
                {
                    if (bulge != 0 || localStart.GetValueOrDefault() != 0 || localEnd.GetValueOrDefault() != 0)
                        throw new NotSupportedException("A 3D polyline does not represent arc bulges or segment widths.");
                    points.Add(position);
                }
                else
                {
                    if (position.Z != 0)
                        throw new NotSupportedException("2D polyline vertex elevations must use the parent elevation.");
                    planar.Add(new Polyline2DVertex(new Vector2(position.X, position.Y), bulge)
                    {
                        StartWidthOverride = localStart ?? start,
                        EndWidthOverride = localEnd ?? end
                    });
                }
            }
            if (!terminated) throw new FormatException("Missing SEQEND in R12 polyline sequence.");
            if (spatial)
                return new Polyline3D(points, (flags & 1) != 0) { Normal = normal, LinetypeGeneration = (flags & 128) != 0 };
            return new Polyline2D(planar, (flags & 1) != 0)
            { Elevation = origin.Z, Thickness = thickness, Normal = normal, LinetypeGeneration = (flags & 128) != 0 };
        }
    }
}
