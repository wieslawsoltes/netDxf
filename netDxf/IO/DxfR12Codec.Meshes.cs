// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        private sealed partial class PrimitiveWriter
        {
            private LayerPacket RegisterLayer(Layer value)
            {
                LayerPacket layer = LayerPacket.Capture(value);
                layer.PatternName = this.RegisterLinetype(value.Linetype, true);
                if (this.layers.TryGetValue(layer.Name, out LayerPacket existing))
                {
                    if (existing.Color != layer.Color || existing.Flags != layer.Flags
                        || !string.Equals(existing.PatternName, layer.PatternName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Conflicting same-named R12 layer definitions.");
                    return existing;
                }
                if (this.layers.Count == short.MaxValue) throw new NotSupportedException("R12 layer-table count limit exceeded.");
                this.layers.Add(layer.Name, layer); this.orderedLayers.Add(layer);
                return layer;
            }

            private void Polygon(PolygonMesh mesh, string layer, Vector3 normal)
            {
                if (mesh.HasStoredRecords || mesh.SmoothType != PolylineSmoothType.NoSmooth
                    || ((int)mesh.Flags & ~177) != 0 || ((int)mesh.Flags & 16) == 0)
                    throw new NotSupportedException("R12 polygon selection cannot discard retained records or fitted-surface semantics.");
                this.Tag(66, (short)1); this.Point(10, Vector3.Zero); this.Tag(70, (short)mesh.Flags);
                this.Tag(71, mesh.U); this.Tag(72, mesh.V);
                this.Tag(73, mesh.DensityU); this.Tag(74, mesh.DensityV); this.Tag(75, (short)0);
                this.Point(210, normal);
                // DXF advances N first. PolygonMesh's public array advances U first.
                // Using the public (U,V) accessor keeps rectangular grids untransposed.
                for (int u = 0; u < mesh.U; u++)
                for (int v = 0; v < mesh.V; v++)
                {
                    this.Child("VERTEX", layer); this.Point(10, mesh.GetVertex(u, v)); this.Tag(70, (short)64);
                }
                this.Child("SEQEND", layer);
            }

            private void Polyface(PolyfaceMesh mesh, string layer, Vector3 normal)
            {
                if (mesh.HasStoredRecords || ((int)mesh.Flags & ~192) != 0 || ((int)mesh.Flags & 64) == 0)
                    throw new NotSupportedException("R12 polyface selection cannot discard retained child metadata or unknown flags.");
                this.Tag(66, (short)1); this.Point(10, Vector3.Zero); this.Tag(70, (short)mesh.Flags);
                // Polyface counts are advisory, not admission or allocation sizes.
                // Zero is used when an actual count cannot fit a signed group value.
                this.Tag(71, mesh.Vertexes.Length <= short.MaxValue ? (short)mesh.Vertexes.Length : (short)0);
                this.Tag(72, mesh.Faces.Count <= short.MaxValue ? (short)mesh.Faces.Count : (short)0);
                this.Point(210, normal);
                foreach (Vector3 vertex in mesh.Vertexes)
                {
                    this.Child("VERTEX", layer); this.Point(10, vertex); this.Tag(70, (short)192);
                }
                foreach (PolyfaceMeshFace face in mesh.Faces)
                {
                    if (face == null || face.GetType() != typeof(PolyfaceMeshFace))
                        throw new NotSupportedException("R12 polyface faces must be plain index records.");
                    face.ValidateVertexIndexes(mesh.Vertexes.Length);
                    LayerPacket faceLayer = this.RegisterLayer(face.Layer ?? mesh.Layer);
                    AciColor color = face.Color ?? mesh.Color;
                    if (color.UseTrueColor) throw new NotSupportedException("R12 polyface colors must be indexed.");
                    // Materialize effective face styling, independent of parent changes
                    // after typed selection. The original raw packet keeps its provenance.
                    this.Child("VERTEX", faceLayer.Name); this.Tag(62, color.Index);
                    this.Point(10, Vector3.Zero); this.Tag(70, (short)128);
                    for (int i = 0; i < 4; i++)
                        this.Tag((short)(71 + i), i < face.VertexIndexes.Length ? face.VertexIndexes[i] : (short)0);
                }
                this.Child("SEQEND", layer);
            }
        }

        private static EntityObject ReadLegacyMesh(Fields header, IReadOnlyList<DxfRawRecord> records,
            ref int index, string parentLayer, short parentColor, Vector3 normal, double thickness,
            HashSet<string> handles, Dictionary<string, Layer> layers)
        {
            short follows = header.Integer(66, 1), flags = header.Integer(70, 0);
            bool polygon = (flags & 16) != 0;
            int allowed = polygon ? 177 : 192;
            if ((follows != 0 && follows != 1) || (flags & ~allowed) != 0
                || (!polygon && (flags & 64) == 0))
                throw new NotSupportedException("Unsupported fitted, mixed or unknown mesh flags.");
            Vector3 origin = header.Vector(10, Vector3.Zero, false);
            if (origin.X != 0 || origin.Y != 0 || origin.Z != 0 || thickness != 0
                || header.Number(40, 0) != 0 || header.Number(41, 0) != 0)
                throw new NotSupportedException("R12 mesh header must not carry polyline elevation, thickness or widths.");
            short m = header.Integer(71, 0), n = header.Integer(72, 0);
            short densityM = header.Integer(73, 0), densityN = header.Integer(74, 0);
            if (header.Integer(75, 0) != 0 || (!polygon && (densityM != 0 || densityN != 0)))
                throw new NotSupportedException("Fitted mesh data requires a surface codec.");
            header.Finish();
            // Match the existing typed PolygonMesh representation. Never allocate from
            // untrusted polyface hints; even negative/stale polyface counts are ignored.
            if (polygon && (m < 2 || m > 256 || n < 2 || n > 256))
                throw new NotSupportedException("Typed PolygonMesh dimensions must be between 2 and 256.");
            var vertices = new List<Vector3>();
            var faces = polygon ? null : new List<PolyfaceMeshFace>();
            bool terminated = false;
            while (++index < records.Count)
            {
                var record = records[index];
                bool end = string.Equals(record.Name, "SEQEND", StringComparison.OrdinalIgnoreCase);
                if (!end && !string.Equals(record.Name, "VERTEX", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("A mesh sequence must end with SEQEND before another entity.");
                var child = new Fields(record);
                string identity = child.Identity();
                if (identity != null && !handles.Add(identity)) throw new FormatException("Duplicate mesh child or terminator identity.");
                short vertexFlags = end ? (short)0 : child.Integer(70, 0);
                bool face = !end && !polygon && vertexFlags == 128;
                string layer = ResourceName(child.Text(8, parentLayer));
                short color = child.Integer(62, face ? parentColor : (short)256);
                if (color < 0 || color > 256 || child.Integer(67, 0) != 0
                    || BuiltinLinetype(child.Text(6, "BYLAYER")) != "BYLAYER")
                    throw new NotSupportedException("Unsupported mesh child attributes.");
                if (end)
                {
                    if (!string.Equals(layer, parentLayer, StringComparison.OrdinalIgnoreCase) || color != 256)
                        throw new NotSupportedException("Mesh terminator styling cannot be discarded.");
                    child.Finish(); terminated = true; break;
                }
                if (!face && vertexFlags != (polygon ? 64 : 192))
                    throw new NotSupportedException("Unsupported mesh vertex flags.");
                if (!face && (!string.Equals(layer, parentLayer, StringComparison.OrdinalIgnoreCase)
                    || color != 256))
                    throw new NotSupportedException("Coordinate-vertex styling cannot be discarded.");
                Vector3 position = child.Vector(10, Vector3.Zero, !face);
                if (child.Number(40, 0) != 0 || child.Number(41, 0) != 0
                    || child.Number(42, 0) != 0 || child.Number(50, 0) != 0)
                    throw new NotSupportedException("Mesh vertices cannot represent widths, bulges or fit tangents.");
                var indices = face ? new short[4] : null;
                for (int i = 0; i < 4; i++)
                {
                    short value = child.Integer((short)(71 + i), 0);
                    if (face) indices[i] = value;
                    else if (value != 0) throw new FormatException("Coordinate vertex contains face indices.");
                }
                child.Finish();
                if (face)
                {
                    // Face location is ignored by the DXF definition. Preserve signed
                    // slots (including ignored trailing slots), then validate against
                    // the final coordinate list so interleaved/forward references work.
                    if (!layers.TryGetValue(layer, out Layer resolved))
                    { resolved = new Layer(layer); layers.Add(layer, resolved); }
                    faces.Add(new PolyfaceMeshFace(indices) { Layer = resolved, Color = AciColor.FromCadIndex(color) });
                }
                else
                {
                    if (polygon && vertices.Count == m * n) throw new FormatException("Polygon mesh exceeds its M x N vertex count.");
                    vertices.Add(position);
                }
            }
            if (!terminated) throw new FormatException("Missing SEQEND in R12 mesh.");
            if (!polygon) return new PolyfaceMesh(vertices, faces) { Normal = normal, Flags = (PolylineTypeFlags)flags };
            if (vertices.Count != m * n) throw new FormatException("Polygon mesh requires exactly M x N vertices.");
            var points = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++) points[i / n + i % n * m] = vertices[i];
            var mesh = new PolygonMesh(m, n, points) { Normal = normal, Flags = (PolylineTypeFlags)flags };
            mesh.RestoreSurfaceDensities(densityM, densityN);
            return mesh;
        }

    }
}
