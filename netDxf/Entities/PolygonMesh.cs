#region netDxf library licensed under the MIT License
// 
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>
    /// Represents a mesh grid <see cref="EntityObject">entity</see>.
    /// </summary>
    public partial class PolygonMesh :
        EntityObject
    {
        #region private fields

        private static short defaultSurfU = 6;
        private static short defaultSurfV = 6;

        private readonly Vector3[] vertexes;
        private readonly short u;
        private readonly short v;
        private short densityU;
        private short densityV;
        private PolylineTypeFlags flags;
        private PolylineSmoothType smoothType;

        #endregion

        #region constructor

        /// <summary>
        /// Initializes a new instance of the <c>PolygonMesh</c> class.
        /// </summary>
        /// <param name="u">Number of vertexes along the U direction (local X axis).</param>
        /// <param name="v">Number of vertexes along the V direction (local Y axis).</param>
        /// <param name="vertexes">Array of UxV vertexes that represents the mesh grid.</param>
        public PolygonMesh(short u, short v, IEnumerable<Vector3> vertexes)
            : base(EntityType.PolygonMesh, DxfObjectCode.Polyline)
        {
            if (u < 2 || u > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(this.u), this.u, "The number of vertexes along the U direction must be between 2 and 256.");
            }
            this.u = u;

            if (v < 2 || v > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(this.v), this.v, "The number of vertexes along the V direction must be between 2 and 256.");
            }
            this.v = v;

            if (vertexes == null)
            {
                throw new ArgumentNullException(nameof(vertexes));
            }

            this.vertexes = vertexes.ToArray();

            if (this.vertexes.Length != u * v)
            {
                throw new ArgumentException("The number of vertexes must be equal to UxV.", nameof(vertexes));
            }

            this.densityU = 0;
            this.densityV = 0;
            this.smoothType = PolylineSmoothType.NoSmooth;
            this.flags = PolylineTypeFlags.PolygonMesh;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets the mesh vertexes.
        /// </summary>
        public Vector3[] Vertexes
        {
            get { return this.vertexes; }
        }

        /// <summary>
        /// Set a PolygonMesh vertex by its indexes.
        /// </summary>
        /// <param name="i0">Index of the vertex in the U direction.</param>
        /// <param name="i1">Index of the vertex in the V direction.</param>
        /// <param name="vertex">A Vector3.</param>
        public void SetVertex(int i0, int i1, Vector3 vertex)
        {
            if (0 <= i0 && i0 < this.u && 0 <= i1 && i1 < this.v)
            {
                this.vertexes[i0 + this.u * i1] = vertex;
            }
        }

        /// <summary>
        /// Gets a PolygonMesh vertex by its indexes.
        /// </summary>
        /// <param name="i0">Index of the vertex in the U direction.</param>
        /// <param name="i1">Index of the vertex in the V direction.</param>
        public Vector3 GetVertex(int i0, int i1)
        {
            if (0 <= i0 && i0 < this.u && 0 <= i1 && i1 < this.v)
            {
                return this.vertexes[i0 + this.u * i1];
            }

            return this.vertexes[0];
        }
        
        /// <summary>
        /// Gets the number of vertexes along the U direction (local X axis).
        /// </summary>
        public short U
        {
            get { return this.u; }
        }

        /// <summary>
        /// Gets the number of vertexes along the V direction (local Y axis).
        /// </summary>
        public short V
        {
            get { return this.v; }
        }

        /// <summary>
        /// Smooth surface U density.
        /// </summary>
        /// <remarks>Valid values range from 3 to 201.</remarks>
        public short DensityU
        {
            get { return this.densityU; }
            set
            {
                if (value < 3 || value > 201)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The density value must be between 3 and 201.");
                }
                this.densityU = value;
            }
        }

        /// <summary>
        /// Smooth surface V density
        /// </summary>
        /// <remarks>Valid values range from 3 to 201.</remarks>
        public short DensityV
        {
            get { return this.densityV; }
            set
            {
                if (value < 3 || value > 201)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The density value must be between 3 and 201.");
                }
                this.densityV = value;
            }
        }

        /// <summary>
        /// Gets or sets if the polygon mesh is closed along the U direction (local X axis).
        /// </summary>
        public bool IsClosedInU
        {
            get { return this.flags.HasFlag(PolylineTypeFlags.ClosedPolylineOrClosedPolygonMeshInM); }
            set
            {
                if (value)
                {
                    this.flags |= PolylineTypeFlags.ClosedPolylineOrClosedPolygonMeshInM;
                }
                else
                {
                    this.flags &= ~PolylineTypeFlags.ClosedPolylineOrClosedPolygonMeshInM;
                }
            }
        }

        /// <summary>
        /// Gets or sets if the polygon mesh is closed along the V direction (local Y axis).
        /// </summary>
        public bool IsClosedInV
        {
            get { return this.flags.HasFlag(PolylineTypeFlags.ClosedPolygonMeshInN); }
            set
            {
                if (value)
                {
                    this.flags |= PolylineTypeFlags.ClosedPolygonMeshInN;
                }
                else
                {
                    this.flags &= ~PolylineTypeFlags.ClosedPolygonMeshInN;
                }
            }
        }

        /// <summary>
        /// Gets or sets the polyline smooth type.
        /// </summary>
        /// <remarks>
        /// The additional polygon meshes vertexes corresponding to the SplineFit will be created when writing the DXF file.
        /// </remarks>
        public PolylineSmoothType SmoothType
        {
            get { return this.smoothType; }
            set
            {
                if (value != PolylineSmoothType.NoSmooth && value != PolylineSmoothType.Quadratic && value != PolylineSmoothType.Cubic && value != PolylineSmoothType.BezierSurface)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The polygon mesh surface type must be NoSmooth, Quadratic, Cubic, or BezierSurface.");
                if (value == PolylineSmoothType.NoSmooth)
                {
                    this.flags &= ~PolylineTypeFlags.SplineFit;
                }
                else
                {
                    this.flags |= PolylineTypeFlags.SplineFit;
                }
                this.smoothType = value;
            }
        }

        /// <summary>
        /// Gets or sets if the default SurfU value.
        /// </summary>
        /// <remarks>
        /// This value is used by smoothed polygon meshes when they not belong to a DXF document and the density values are left at the default 0.<br/>
        /// The minimum vertexes generated for smoothed polygon meshes is 3.
        /// </remarks>
        public static short DefaultSurfU
        {
            get { return defaultSurfU; }
            set
            {
                if (value < 0 || value > 200)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Values must be between 0 and 200.");
                }
                defaultSurfU = value;
            }
        }

        /// <summary>
        /// Gets or sets if the default SurfV value.
        /// </summary>
        /// <remarks>
        /// This value is used by smoothed polygon meshes when they not belong to a DXF document and the density values are left at the default 0.<br/>
        /// The minimum vertexes generated for smoothed polygon meshes is 3.
        /// </remarks>
        public static short DefaultSurfV
        {
            get { return defaultSurfV; }
            set
            {
                if (value < 0 || value > 200)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Values must be between 0 and 200.");
                }
                defaultSurfV = value;
            }
        }

        #endregion

        #region internal properties

        /// <summary>
        /// Gets the polygon mesh flags.
        /// </summary>
        internal PolylineTypeFlags Flags
        {
            get { return this.flags; }
            set { this.flags = value; }
        }

        // Used before sampling and before writer preprocessing allocates child handles.
        internal void ValidateSurface()
        {
            int degree = this.smoothType == PolylineSmoothType.Quadratic ? 2 : this.smoothType == PolylineSmoothType.Cubic ? 3 : 0;
            if (degree == 0 && this.smoothType != PolylineSmoothType.NoSmooth && this.smoothType != PolylineSmoothType.BezierSurface)
                throw new InvalidOperationException("POLYGONMESH group 75 has an unsupported surface type.");
            if (degree != 0 && (this.u < degree + (this.IsClosedInU ? 0 : 1) || this.v < degree + (this.IsClosedInV ? 0 : 1)))
                throw new InvalidOperationException("POLYGONMESH groups 71/72 have too few control vertices for the surface degree and closure.");
            if (this.smoothType == PolylineSmoothType.BezierSurface && (this.IsClosedInU || this.IsClosedInV))
                throw new NotSupportedException("Closed Bezier polygon mesh evaluation requires explicit closure semantics.");
            foreach (Vector3 vertex in this.vertexes)
                if (double.IsNaN(vertex.X) || double.IsInfinity(vertex.X) || double.IsNaN(vertex.Y) || double.IsInfinity(vertex.Y) ||
                    double.IsNaN(vertex.Z) || double.IsInfinity(vertex.Z))
                    throw new InvalidOperationException("POLYGONMESH groups 10/20/30 require finite vertex coordinates.");
        }

        #endregion

        #region public methods

        /// <summary>
        /// Obtains a list of vertexes that represent the polygon mesh approximating the surface faces as necessary.
        /// </summary>
        /// <returns>A list of vertexes that represent the mesh.</returns>
        /// <remarks>
        /// The minimum vertexes generated for smoothed polygon meshes is 3 in each direction.
        /// </remarks>
        public List<Vector3> MeshVertexes()
        {
            this.ResolveSurfaceDensity(out int precisionU, out int precisionV);
            return this.MeshVertexes(precisionU, precisionV);
        }

        /// <summary>
        /// Obtains a list of vertexes that represent the polygon mesh approximating the surface faces as necessary.
        /// </summary>
        /// <param name="precisionU">Number of vertexes created along the U direction.</param>
        /// <param name="precisionV">Number of vertexes created along the V direction.</param>
        /// <returns>A list of vertexes that represent the mesh.</returns>
        public List<Vector3> MeshVertexes(int precisionU, int precisionV)
        {
            if (precisionU < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(precisionU), precisionU, "The precisionU must be equal or greater than three.");
            }
            if (precisionV < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(precisionV), precisionV, "The precisionV must be equal or greater than three.");
            }

            this.ValidateSurface();
            if (this.smoothType != PolylineSmoothType.NoSmooth && (long)precisionU * precisionV > MaximumSurfaceSamples)
                throw new ArgumentOutOfRangeException(nameof(precisionU), "The surface sampling grid exceeds MaximumSurfaceSamples.");
            if (this.smoothType == PolylineSmoothType.BezierSurface)
                return this.BezierMeshVertexes(precisionU, precisionV);
            int degree;
            if (this.smoothType == PolylineSmoothType.Quadratic)
            {
                degree = 2;
            }
            else if (this.smoothType == PolylineSmoothType.Cubic)
            {
                degree = 3;
            }
            else
            {
                return new List<Vector3>(this.vertexes);
            }

            List<Vector3> controlsUV = new List<Vector3>();
            int numU = this.u;
            int numV = this.v;

            // duplicate vertexes to handle periodic BSpline surfaces
            if (this.IsClosedInU)
            {
                numU += degree;
                for (int i = 0; i < this.v; i++)
                {
                    for (int j = 0; j < this.u + degree; j++)
                    {
                        if (j < this.u)
                        {
                            controlsUV.Add(this.vertexes[i * this.u + j]);
                        }
                        else
                        {
                            for (int k = 0; k < degree; k++, j++)
                            {
                                controlsUV.Add(this.vertexes[i * this.u + k]);
                            }
                        }
                    }
                }
                if (this.IsClosedInV)
                {
                    numV += degree;
                    for (int i = 0; i < degree; i++)
                    {
                        for (int j = 0; j < numU; j++)
                        {
                            controlsUV.Add(controlsUV[i * numU + j]);
                        }
                    }
                }
            }
            else if (this.IsClosedInV)
            {
                controlsUV.AddRange(this.vertexes);
                numV += degree;
                for (int i = 0; i < degree; i++)
                {
                    for (int j = 0; j < this.u; j++)
                    {
                        controlsUV.Add(this.vertexes[i * this.u + j]);
                    }
                }
            }
            else
            {
                controlsUV.AddRange(this.vertexes);
            }

            GTE.BasisFunctionInput bfU = new GTE.BasisFunctionInput(numU, degree);
            GTE.BasisFunctionInput bfV = new GTE.BasisFunctionInput(numV, degree);

            GTE.BSplineSurface surface = new GTE.BSplineSurface(bfU, bfV, controlsUV.ToArray());

            //change the knot vector to handle periodic BSplines
            if (this.IsClosedInU)
            {
                double factor = 1.0 / this.u;
                for (int i = 0; i < surface.BasisFunction(0).NumKnots; i++)
                {
                    surface.BasisFunction(0).Knots[i] = (i - surface.BasisFunction(0).Degree) * factor;
                }
            }

            if (this.IsClosedInV)
            {
                double factor = 1.0 / this.v;
                for (int i = 0; i < surface.BasisFunction(1).NumKnots; i++)
                {
                    surface.BasisFunction(1).Knots[i] = (i - surface.BasisFunction(1).Degree) * factor;
                }
            }

            double stepU = this.IsClosedInU ? 1.0 / precisionU : 1.0 / (precisionU - 1);
            double stepV = this.IsClosedInV ? 1.0 / precisionV : 1.0 / (precisionV - 1);
            double tU = 0.0;
            double tV = 0.0;
            List<Vector3> ocsVertexes = new List<Vector3>(precisionU * precisionV);
            for (int i = 0; i < precisionV; i++)
            {
                for (int j = 0; j < precisionU; j++)
                {
                    ocsVertexes.Add(surface.GetPosition(tU, tV));
                    tU += stepU;
                }

                tV += stepV;
                tU = 0;
            }

            return ocsVertexes;
        }

        /// <summary>
        /// Converts the actual polygon mesh into a mesh entity approximating the surface faces as necessary.
        /// </summary>
        /// <returns>A detached <see cref="Mesh">Mesh entity</see> with independently cloned ordinary appearance.</returns>
        public Mesh ToMesh()
        {
            this.ResolveSurfaceDensity(out int precisionU, out int precisionV);
            return this.ToMesh(precisionU, precisionV);
        }

        /// <summary>
        /// Converts the actual polygon mesh into a mesh entity approximating the surface faces as necessary.
        /// </summary>
        /// <param name="precisionU">Number of vertexes created along the U direction.</param>
        /// <param name="precisionV">Number of vertexes created along the V direction.</param>
        /// <returns>A detached <see cref="Mesh">Mesh entity</see> with independently cloned ordinary appearance.</returns>
        /// <remarks>
        /// The minimum vertexes generated for smoothed polygon meshes is 3 in each direction.
        /// </remarks>
        public Mesh ToMesh(int precisionU, int precisionV)
        {
            this.ValidateConversion();
            List<Vector3> points = this.MeshVertexes(precisionU, precisionV);
            int countU = this.smoothType == PolylineSmoothType.NoSmooth ? this.u : precisionU;
            int countV = this.smoothType == PolylineSmoothType.NoSmooth ? this.v : precisionV;
            var result = new Mesh(points, this.ConversionFaces(countU, countV));
            this.CopyConversionAppearance(result);
            return result;
        }

        /// <summary>
        /// Decompose the actual polygon mesh into <see cref="Face3D">faces 3D</see>.
        /// </summary>
        /// <returns>Detached faces, including closure seams, with independently cloned ordinary appearance.</returns>
        /// <remarks>Associated object graphs and decorated retained child records require explicit dependency conversion.
        /// Identity and proxy graphics are not copied. The source is never replaced or modified.</remarks>
        public List<Face3D> Explode()
        {
            this.ValidateConversion();
            this.ResolveSurfaceDensity(out int precisionU, out int precisionV);
            List<Vector3> points = this.MeshVertexes(precisionU, precisionV);
            int countU = this.smoothType == PolylineSmoothType.NoSmooth ? this.u : precisionU;
            int countV = this.smoothType == PolylineSmoothType.NoSmooth ? this.v : precisionV;
            var result = new List<Face3D>();
            foreach (int[] face in this.ConversionFaces(countU, countV))
            {
                var entity = new Face3D(points[face[0]], points[face[1]], points[face[2]], points[face[3]]);
                this.CopyConversionAppearance(entity);
                result.Add(entity);
            }
            return result;
        }

        #endregion

        #region overrides

        /// <summary>
        /// Moves, scales, and/or rotates the current entity given a 3x3 transformation matrix and a translation vector.
        /// </summary>
        /// <param name="transformation">Transformation matrix.</param>
        /// <param name="translation">Translation vector.</param>
        /// <remarks>Matrix3 adopts the convention of using column vectors to represent a transformation matrix.</remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            for (int i = 0; i < this.vertexes.Length; i++)
            {
                this.vertexes[i] = transformation * this.vertexes[i] + translation;
            }

            Vector3 newNormal = transformation * this.Normal;
            if (Vector3.Equals(Vector3.Zero, newNormal))
            {
                newNormal = this.Normal;
            }
            this.Normal = newNormal;
        }

        /// <summary>
        /// Creates a new PolygonMesh that is a copy of the current instance.
        /// </summary>
        /// <returns>A new PolygonMesh that is a copy of this instance.</returns>
        public override object Clone()
        {
            this.RejectStoredRecordClone();
            PolygonMesh entity = new PolygonMesh(this.u, this.v, this.vertexes)
            {
                //EntityObject properties
                Layer = (Layer) this.Layer.Clone(),
                Linetype = (Linetype) this.Linetype.Clone(),
                Color = (AciColor) this.Color.Clone(),
                Lineweight = this.Lineweight,
                Transparency = (Transparency) this.Transparency.Clone(),
                LinetypeScale = this.LinetypeScale,
                Normal = this.Normal,
                IsVisible = this.IsVisible,
                //PolygonMesh properties
                densityU = this.densityU,
                densityV = this.densityV,
                smoothType = this.smoothType,
                flags = this.flags
            };

            foreach (XData data in this.XData.Values)
            {
                entity.XData.Add((XData) data.Clone());
            }

            this.CopyCommonDataTo(entity);
            this.CopyStoredRecordsTo(entity);
            return entity;
        }

        #endregion
    }
}
