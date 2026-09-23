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
    /// Represents a circular arc <see cref="EntityObject">entity</see>.
    /// </summary>
    public class Arc :
        EntityObject
    {
        #region private fields

        private Vector3 center;
        private double radius;
        private double startAngle;
        private double endAngle;
        private double thickness;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Arc</c> class.
        /// </summary>
        public Arc()
            : this(Vector3.Zero, 1.0, 0.0, 180.0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Arc</c> class.
        /// </summary>
        /// <param name="center">Arc <see cref="Vector2">center</see> in world coordinates.</param>
        /// <param name="radius">Arc radius.</param>
        /// <param name="startAngle">Arc start angle in degrees.</param>
        /// <param name="endAngle">Arc end angle in degrees.</param>
        public Arc(Vector2 center, double radius, double startAngle, double endAngle)
            : this(new Vector3(center.X, center.Y, 0.0), radius, startAngle, endAngle)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Arc</c> class.
        /// </summary>
        /// <param name="center">Arc <see cref="Vector3">center</see> in world coordinates.</param>
        /// <param name="radius">Arc radius.</param>
        /// <param name="startAngle">Arc start angle in degrees.</param>
        /// <param name="endAngle">Arc end angle in degrees.</param>
        public Arc(Vector3 center, double radius, double startAngle, double endAngle)
            : base(EntityType.Arc, DxfObjectCode.Arc)
        {
            this.center = center;
            if (radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "The arc radius must be greater than zero.");
            }
            this.radius = radius;
            this.startAngle = ArcParameterAngles.Normalize(startAngle);
            this.endAngle = ArcParameterAngles.Normalize(endAngle);
            this.thickness = 0.0;
        }

        /// <summary>
        /// Initializes a new instance of the <c>Arc</c> class.
        /// </summary>
        /// <param name="startPoint">Arc start point.</param>
        /// <param name="endPoint">Arc end point.</param>
        /// <param name="bulge">Bulge value.</param>
        public Arc(Vector2 startPoint, Vector2 endPoint, double bulge)
            : base(EntityType.Arc, DxfObjectCode.Arc)
        {
            Tuple<Vector2, double, double, double> data = MathHelper.ArcFromBulge(startPoint, endPoint, bulge);
            if (data.Item2 <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "The arc radius must be greater than zero.");
            }
            this.center = new Vector3(data.Item1.X, data.Item1.Y, 0.0) ;
            this.radius = data.Item2;
            this.startAngle = data.Item3;
            this.endAngle = data.Item4;
            this.thickness = 0.0;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or sets the arc <see cref="Vector3">center</see> in world coordinates.
        /// </summary>
        public Vector3 Center
        {
            get { return this.center; }
            set { PrimitiveGeometryMutation.Assign(this, ref this.center, value); }
        }

        /// <summary>
        /// Gets or sets the arc radius.
        /// </summary>
        public double Radius
        {
            get { return this.radius; }
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The arc radius must be greater than zero.");
                }
                PrimitiveGeometryMutation.Assign(this, ref this.radius, value);
            }
        }

        /// <summary>
        /// Gets or sets the arc start angle in degrees.
        /// Stored angles are normalized to [0,360) without geometric tolerance snapping.
        /// </summary>
        public double StartAngle
        {
            get { return this.startAngle; }
            set { PrimitiveGeometryMutation.Assign(this, ref this.startAngle, ArcParameterAngles.Normalize(value)); }
        }

        /// <summary>
        /// Gets or sets the arc end angle in degrees.
        /// Stored angles are normalized to [0,360) without geometric tolerance snapping.
        /// </summary>
        public double EndAngle
        {
            get { return this.endAngle; }
            set { PrimitiveGeometryMutation.Assign(this, ref this.endAngle, ArcParameterAngles.Normalize(value)); }
        }

        /// <summary>
        /// Gets or sets the arc thickness.
        /// </summary>
        public double Thickness
        {
            get { return this.thickness; }
            set { PrimitiveGeometryMutation.Assign(this, ref this.thickness, value); }
        }

        #endregion

        #region public methods

        /// <summary>
        /// Converts the arc in a list of vertexes.
        /// </summary>
        /// <param name="precision">Number of vertexes generated.</param>
        /// <returns>A list vertexes that represents the arc expressed in object coordinate system.</returns>
        public List<Vector2> PolygonalVertexes(int precision)
        {
            if (precision < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(precision), precision, "The arc precision must be equal or greater than two.");
            }

            List<Vector2> ocsVertexes = new List<Vector2>();
            double start = this.startAngle * MathHelper.DegToRad;
            double end = this.endAngle * MathHelper.DegToRad;
            if (end < start)
            {
                end += MathHelper.TwoPI;
            }

            double delta = (end - start) / (precision - 1);
            for (int i = 0; i < precision; i++)
            {
                double angle = start + delta*i;
                double sine = this.radius * Math.Sin(angle);
                double cosine = this.radius * Math.Cos(angle);
                ocsVertexes.Add(new Vector2(cosine, sine));
            }

            return ocsVertexes;
        }

        /// <summary>
        /// Converts the arc in a Polyline2D.
        /// </summary>
        /// <param name="precision">Number of divisions.</param>
        /// <returns>A new instance of <see cref="Polyline2D">Polyline2D</see> that represents the arc.</returns>
        public Polyline2D ToPolyline2D(int precision)
        {
            IEnumerable<Vector2> vertexes = this.PolygonalVertexes(precision);
            Vector3 ocsCenter = MathHelper.Transform(this.center, this.Normal, CoordinateSystem.World, CoordinateSystem.Object);

            Polyline2D poly = new Polyline2D
            {
                Layer = (Layer) this.Layer.Clone(),
                Linetype = (Linetype) this.Linetype.Clone(),
                Color = (AciColor) this.Color.Clone(),
                Lineweight = this.Lineweight,
                Transparency = (Transparency) this.Transparency.Clone(),
                LinetypeScale = this.LinetypeScale,
                IsVisible = this.IsVisible,
                Normal = this.Normal,
                Elevation = ocsCenter.Z,
                Thickness = this.Thickness,
                IsClosed = false
            };
            foreach (Vector2 v in vertexes)
            {
                poly.Vertexes.Add(new Polyline2DVertex(v.X + ocsCenter.X, v.Y + ocsCenter.Y));
            }
            return poly;
        }

        #endregion

        #region overrides

        /// <summary>
        /// Moves, scales, and/or rotates the current entity given a 3x3 transformation matrix and a translation vector.
        /// </summary>
        /// <param name="transformation">Transformation matrix.</param>
        /// <param name="translation">Translation vector.</param>
        /// <remarks>
        /// The transformed circular plane must remain orthogonal and equally scaled (relative tolerance 1e-12).<br />
        /// Plane orientation, counterclockwise sweep and signed thickness follow the actual transformed axes, including mirrors.<br />
        /// Unsupported elliptic/sheared/collapsed geometry rejects before mutation; successful changes clear stale proxy graphics.<br />
        /// Matrix3 adopts the convention of using column vectors to represent a transformation matrix.
        /// </remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            var result = CircularEntityTransform.Prepare(transformation, translation,
                this.center, this.Normal, this.radius, this.thickness);
            double start = result.Angle(this.startAngle), end = result.Angle(this.endAngle);
            if (result.IsIdentity) return;
            // All geometry validation is complete before publishing any state.
            base.Normal = result.Normal;
            this.center = result.Center; this.radius = result.Radius; this.thickness = result.Thickness;
            this.startAngle = start; this.endAngle = end;
            this.ClearProxyGraphics();
        }

        /// <summary>Applies a finite affine four-by-four transform; projective matrices reject.</summary>
        /// <remarks>The same circular-plane and extrusion requirements as the three-by-three overload apply.</remarks>
        public override void TransformBy(Matrix4 transformation)
        {
            CircularEntityTransform.CheckAffine(transformation);
            base.TransformBy(transformation);
        }

        /// <summary>
        /// Creates a new Arc that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Arc that is a copy of this instance.</returns>
        public override object Clone()
        {
            Arc entity = new Arc
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
                //Arc properties
                Center = this.center,
                Radius = this.radius,
                StartAngle = this.startAngle,
                EndAngle = this.endAngle,
                Thickness = this.thickness
            };

            foreach (XData data in this.XData.Values)
            {
                entity.XData.Add((XData) data.Clone());
            }

            this.CopyCommonDataTo(entity);
            return entity;
        }

        #endregion
    }
}