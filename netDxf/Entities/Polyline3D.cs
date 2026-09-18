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
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>
    /// Represents a generic polyline <see cref="EntityObject">entity</see>.
    /// </summary>
    public partial class Polyline3D :
        EntityObject
    {
        #region private fields

        private static short defaultSplineSegs = 8;
        private readonly List<Vector3> vertexes;
        private PolylineTypeFlags flags;
        private PolylineSmoothType smoothType;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Polyline3d</c> class.
        /// </summary>
        public Polyline3D()
            : this(new List<Vector3>(), false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Polyline3d</c> class.
        /// </summary>
        /// <param name="vertexes">3d polyline <see cref="Vector3">vertex</see> list.</param>
        public Polyline3D(IEnumerable<Vector3> vertexes)
            : this(vertexes, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Polyline3d</c> class.
        /// </summary>
        /// <param name="vertexes">3d polyline <see cref="Vector3">vertex</see> list.</param>
        /// <param name="isClosed">Sets if the polyline is closed, by default it will create an open polyline.</param>
        public Polyline3D(IEnumerable<Vector3> vertexes, bool isClosed)
            : base(EntityType.Polyline3D, DxfObjectCode.Polyline)
        {
            if (vertexes == null)
            {
                throw new ArgumentNullException(nameof(vertexes));
            }

            this.vertexes = new List<Vector3>(vertexes);
            this.flags = isClosed ? PolylineTypeFlags.ClosedPolylineOrClosedPolygonMeshInM | PolylineTypeFlags.Polyline3D : PolylineTypeFlags.Polyline3D;
            this.smoothType = PolylineSmoothType.NoSmooth;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or sets if the default SplineSegs value, this value is used by the Explode method when the current Polyline2D does not belong to a DXF document.
        /// </summary>
        public static short DefaultSplineSegs
        {
            get { return defaultSplineSegs; }
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Values must be greater than 0.");
                }
                defaultSplineSegs = value;
            }
        }

        /// <summary>
        /// Gets the polyline <see cref="Vector3">vertex</see> list.
        /// </summary>
        public List<Vector3> Vertexes
        {
            get { return this.vertexes; }
        }

        /// <summary>
        /// Gets or sets if the polyline is closed.
        /// </summary>
        public bool IsClosed
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
        /// Enable or disable if the line type pattern is generated continuously around the vertexes of the polyline.
        /// </summary>
        public bool LinetypeGeneration
        {
            get { return this.flags.HasFlag(PolylineTypeFlags.ContinuousLinetypePattern); }
            set
            {
                if (value)
                {
                    this.flags |= PolylineTypeFlags.ContinuousLinetypePattern;
                }
                else
                {
                    this.flags &= ~PolylineTypeFlags.ContinuousLinetypePattern;
                }
            }
        }

        /// <summary>
        /// Gets or sets the curve smooth type.
        /// </summary>
        /// <remarks>
        /// The additional polyline vertexes corresponding to the SplineFit will be created when writing the DXF file.
        /// </remarks>
        public PolylineSmoothType SmoothType
        {
            get { return this.smoothType; }
            set
            {
                if (value == PolylineSmoothType.BezierSurface)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "BezierSurface is a polygon mesh surface, not a polyline curve.");
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

        #endregion

        #region internal properties

        /// <summary>
        /// Gets the Polyline3D flags.
        /// </summary>
        internal PolylineTypeFlags Flags
        {
            get { return this.flags; }
            set { this.flags = value; }
        }

        #endregion

        #region public methods

        /// <summary>
        /// Switch the polyline direction.
        /// </summary>
        public void Reverse()
        {
            if (this.vertexes.Count < 2)
            {
                return;
            }

            if (this.HasStoredRecords) this.ValidateStoredRecordGeometry();
            this.vertexes.Reverse();
            if (this.HasStoredRecords) this.storedVertexRecords.Reverse();
        }

        /// <summary>
        /// Decompose the actual polyline in a list of <see cref="Line">lines</see>.
        /// </summary>
        /// <returns>A list of <see cref="Line">lines</see> that made up the polyline.</returns>
        public List<EntityObject> Explode()
        {
            return this.ExplodeLines();
        }

        /// <summary>
        /// Converts the polyline in a list of vertexes.
        /// </summary>
        /// <param name="precision">Number of vertexes generated, only applicable for smoothed polylines.</param>
        /// <returns>A list vertexes that represents the polyline.</returns>
        public List<Vector3> PolygonalVertexes(int precision)
        {
            if (precision < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(precision), precision, "The precision must be equal or greater than zero.");
            }

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
                List<Vector3> points = new List<Vector3>(this.vertexes);
                return points;
            }

            // the minimum number of vertexes generated for smoothed polylines is 2
            if (precision < 2)
            {
                precision = 2;
            }

            // closed polylines will be considered as closed and periodic
            return Spline.NurbsEvaluator(this.vertexes.ToArray(), null, null, degree, false, this.IsClosed, precision);
        }

        /// <summary>
        /// Converts the actual Polyline3D in a Polyline2D.
        /// </summary>
        /// <param name="precision">Number of vertexes generated, only applicable for smoothed polylines.</param>
        /// <returns>A Polyline2D that represents the polyline.</returns>
        /// <remarks>
        /// The resulting Polyline2D will be a projection of the actual polyline into the plane defined by its normal vector.
        /// </remarks>
        public Polyline2D ToPolyline2D(int precision)
        {
            List<Vector3> vertexes3D = this.PolygonalVertexes(precision);
            List<Vector2> vertexes2D = MathHelper.Transform(vertexes3D, this.Normal, out double _);
            Polyline2D polyline2D = new Polyline2D(vertexes2D)
            {
                Layer = (Layer) this.Layer.Clone(),
                Linetype = (Linetype) this.Linetype.Clone(),
                Color = (AciColor) this.Color.Clone(),
                Lineweight = this.Lineweight,
                Transparency = (Transparency) this.Transparency.Clone(),
                LinetypeScale = this.LinetypeScale,
                Normal = this.Normal,
                IsClosed = this.IsClosed
            };

            return polyline2D;
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
            if (this.HasStoredRecords) this.ValidateStoredRecordGeometry();
            var prepared = VertexAffineTransform.Prepare(this.vertexes, base.Normal, transformation, translation);
            if (!prepared.Changed) return;
            // Prepare every coordinate before publishing; preserve slot identities
            // and bypass caller-overridden Normal accessors during publication.
            base.Normal = prepared.Normal;
            for (int i = 0; i < prepared.Points.Length; i++) this.vertexes[i] = prepared.Points[i];
            this.ClearProxyGraphics();
        }

        /// <summary>Applies a finite affine matrix; projective input rejects before mutation.</summary>
        /// <param name="transformation">An affine four-by-four transformation matrix.</param>
        public override void TransformBy(Matrix4 transformation)
        {
            InfiniteLineTransform.CheckAffine(transformation);
            base.TransformBy(transformation);
        }

        /// <summary>
        /// Creates a new Polyline3D that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Polyline3D that is a copy of this instance.</returns>
        public override object Clone()
        {
            this.RejectStoredRecordClone();
            Polyline3D entity = new Polyline3D(this.vertexes)
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
                //Polyline3D properties
                smoothType = this.smoothType,
                Flags = this.flags
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