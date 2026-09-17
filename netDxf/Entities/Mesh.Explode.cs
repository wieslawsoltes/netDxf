// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Entities
{
    public partial class Mesh
    {
        /// <summary>Decomposes an unsmoothed mesh into detached triangular 3DFACE entities.</summary>
        /// <returns>Triangles preserving boundary edges, winding and ordinary entity appearance.</returns>
        /// <remarks>Nonzero subdivision levels reject. Use ExplodeControlMesh to explicitly select the unsmoothed cage.
        /// Simple planar faces only; no source mutation, smoothing, dependency copying or document conversion is performed.</remarks>
        public List<Face3D> Explode()
        {
            if (this.SubdivisionLevel != 0)
                throw new NotSupportedException("A smoothed mesh requires surface evaluation. Use ExplodeControlMesh to select its unsmoothed cage explicitly.");
            return this.ExplodeControlMesh();
        }

        /// <summary>Triangulates the stored level-zero control polygons, independently of subdivision metadata.</summary>
        /// <param name="maximumTriangles">Maximum output count, checked before triangulation or allocation.</param>
        /// <returns>Detached triangles. Introduced diagonals and each repeated-corner edge are invisible.</returns>
        /// <remarks>Each ring is limited to 1024 vertices. Self-intersecting, touching, degenerate and nonplanar rings reject.
        /// Planarity uses a relative 1e-10 tolerance. Original vertices are retained, never projected or flattened.
        /// Associated object graphs and XData handle references reject rather than being silently discarded.
        /// Crease weights and subdivision are not evaluated. This method does not replace the source in its document.</remarks>
        public List<Face3D> ExplodeControlMesh(int maximumTriangles = 1000000)
        {
            if (maximumTriangles < 0) throw new ArgumentOutOfRangeException(nameof(maximumTriangles));
            if (this.ExtensionDictionary != null || this.PersistentReactors.Count != 0 || this.Reactors.Count != 0)
                throw new NotSupportedException("Associated mesh object graphs require an explicit dependency conversion.");
            foreach (XData data in this.XData.Values)
                foreach (XDataRecord record in data.XDataRecord)
                    if (record.Code == XDataCode.DatabaseHandle)
                        throw new NotSupportedException("Mesh XData handles require an explicit dependency conversion.");
            foreach (Vector3 point in this.vertexes) MeshPolygonTriangulator.CheckFinite(point);
            Vector3 normal = base.Normal;
            MeshPolygonTriangulator.CheckFinite(normal);
            if (Math.Abs(Vector3.DotProduct(normal, normal) - 1) > 2e-15)
                throw new InvalidOperationException("The mesh auxiliary normal must be a finite unit vector.");
            long count = 0;
            foreach (int[] face in this.faces)
            {
                int length = MeshPolygonTriangulator.RingLength(face);
                foreach (int index in face)
                    if (index < 0 || index >= this.vertexes.Count)
                        throw new InvalidOperationException("Mesh face index is outside the vertex list.");
                count += length - 2;
                if (count > maximumTriangles)
                    throw new InvalidOperationException("Mesh triangulation exceeds the requested output budget.");
            }
            var result = new List<Face3D>((int)count);
            foreach (int[] face in this.faces)
            {
                int length = MeshPolygonTriangulator.RingLength(face);
                foreach (int[] triangle in MeshPolygonTriangulator.Triangulate(this.vertexes, face, length))
                {
                    int a = triangle[0], b = triangle[1], c = triangle[2];
                    Face3DEdgeFlags flags = Face3DEdgeFlags.Third;
                    if (!MeshPolygonTriangulator.Boundary(a, b, length)) flags |= Face3DEdgeFlags.First;
                    if (!MeshPolygonTriangulator.Boundary(b, c, length)) flags |= Face3DEdgeFlags.Second;
                    if (!MeshPolygonTriangulator.Boundary(c, a, length)) flags |= Face3DEdgeFlags.Fourth;
                    var entity = new Face3D(this.vertexes[face[a]], this.vertexes[face[b]], this.vertexes[face[c]])
                    {
                        Layer = (Layer)this.Layer.Clone(), Linetype = (Linetype)this.Linetype.Clone(),
                        Color = (AciColor)this.Color.Clone(), Transparency = (Transparency)this.Transparency.Clone(),
                        Lineweight = this.Lineweight, LinetypeScale = this.LinetypeScale, IsVisible = this.IsVisible,
                        Normal = normal, EdgeFlags = flags, ColorName = this.ColorName, ShadowMode = this.ShadowMode
                    };
                    foreach (XData data in this.XData.Values) entity.XData.Add((XData)data.Clone());
                    // Identity, ownership, associations and stale proxy graphics are never copied.
                    result.Add(entity);
                }
            }
            return result;
        }
    }
}
