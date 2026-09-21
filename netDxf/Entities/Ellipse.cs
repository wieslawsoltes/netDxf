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
    /// Represents an ellipse <see cref="EntityObject">entity</see>.
    /// </summary>
    public partial class Ellipse :
        EntityObject
    {
        #region private fields

        private Vector3 center;
        private double majorAxis;
        private double minorAxis;
        private double rotation;
        private double startAngle;
        private double endAngle;
        private double thickness;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Ellipse</c> class.
        /// </summary>
        public Ellipse()
            : this(Vector3.Zero, 1.0, 0.5)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Ellipse</c> class.
        /// </summary>
        /// <param name="center">Ellipse <see cref="Vector2">center</see> in object coordinates.</param>
        /// <param name="majorAxis">Ellipse major axis.</param>
        /// <param name="minorAxis">Ellipse minor axis.</param>
        /// <remarks>
        /// The center Z coordinate represents the elevation of the ellipse along the normal.
        /// The major axis is always measured along the ellipse local X axis,
        /// while the minor axis is along the local Y axis.
        /// </remarks>
        public Ellipse(Vector2 center, double majorAxis, double minorAxis)
            : this(new Vector3(center.X, center.Y, 0.0), majorAxis, minorAxis)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Ellipse</c> class.
        /// </summary>
        /// <param name="center">Ellipse <see cref="Vector3">center</see> in object coordinates.</param>
        /// <param name="majorAxis">Ellipse major axis.</param>
        /// <param name="minorAxis">Ellipse minor axis.</param>
        /// <remarks>
        /// The center Z coordinate represents the elevation of the ellipse along the normal.
        /// The major axis is always measured along the ellipse local X axis,
        /// while the minor axis is along the local Y axis.
        /// </remarks>
        public Ellipse(Vector3 center, double majorAxis, double minorAxis)
            : base(EntityType.Ellipse, DxfObjectCode.Ellipse)
        {
            this.center = center;

            if (double.IsNaN(majorAxis) || double.IsInfinity(majorAxis) || majorAxis <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(majorAxis), majorAxis, "The major axis value must be greater than zero.");
            }

            if (double.IsNaN(minorAxis) || double.IsInfinity(minorAxis) || minorAxis <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minorAxis), minorAxis, "The minor axis value must be greater than zero.");
            }

            if (minorAxis > majorAxis)
            {
                throw new ArgumentException("The major axis must be greater than the minor axis.");
            }

            this.majorAxis = majorAxis;
            this.minorAxis = minorAxis;
            this.startAngle = 0.0;
            this.endAngle = 0.0;
            this.rotation = 0.0;
            this.thickness = 0.0;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or sets the ellipse <see cref="Vector3">center</see> in world coordinates.
        /// </summary>
        public Vector3 Center
        {
            get { return this.center; }
            set { this.center = value; }
        }

        /// <summary>
        /// Gets or sets the ellipse mayor axis.
        /// </summary>
        /// <remarks>The major axis is always measured along the ellipse local X axis.</remarks>
        public double MajorAxis
        {
            get { return this.majorAxis; }
        }

        /// <summary>
        /// Gets or sets the ellipse minor axis.
        /// </summary>
        /// <remarks>The minor axis is always measured along the ellipse local Y axis.</remarks>
        public double MinorAxis
        {
            get { return this.minorAxis; }
        }

        /// <summary>
        /// Gets or sets the ellipse local rotation in degrees along its normal.
        /// </summary>
        public double Rotation
        {
            get { return this.rotation; }
            set { this.rotation = NormalizeEllipseAngle(value); }
        }

        /// <summary>
        /// Gets or sets the ellipse start angle in degrees.
        /// </summary>
        /// <remarks>To get a full ellipse set the start angle equal to the end angle.</remarks>
        public double StartAngle
        {
            get { return this.startAngle; }
            set { this.startAngle = NormalizeEllipseAngle(value); }
        }

        /// <summary>
        /// Gets or sets the ellipse end angle in degrees.
        /// </summary>
        /// <remarks>To get a full ellipse set the end angle equal to the start angle.</remarks>
        public double EndAngle
        {
            get { return this.endAngle; }
            set { this.endAngle = NormalizeEllipseAngle(value); }
        }

        /// <summary>
        /// Gets or sets the ellipse thickness.
        /// </summary>
        public double Thickness
        {
            get { return this.thickness; }
            set { this.thickness = value; }
        }

        /// <summary>
        /// Checks if the actual instance is a full ellipse.
        /// </summary>
        /// <remarks>Only exactly equal normalized start and end angles denote a full ellipse; MathHelper.Epsilon does not change the sweep.</remarks>
        public bool IsFullEllipse
        {
            get { return this.startAngle == this.endAngle; }
        }

        #endregion

        #region public methods

        /// <summary>
        /// Sets the ellipse major and minor axis from the two parameters.
        /// </summary>
        /// <param name="axis1">Ellipse axis.</param>
        /// <param name="axis2">Ellipse axis.</param>
        /// <remarks>
        /// It is not required that axis1 is greater than axis2. The larger value will be assigned as major axis and the lower as minor axis.
        /// Changed axes clear stale proxy graphics. Invalid inputs and unchanged sorted axes retain them.
        /// </remarks>
        public void SetAxis(double axis1, double axis2)
        {
            if (double.IsNaN(axis1) || double.IsInfinity(axis1) || axis1 <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axis1), axis1, "The axis value must be greater than zero.");
            }

            if (double.IsNaN(axis2) || double.IsInfinity(axis2) || axis2 <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axis2), axis2, "The axis value must be greater than zero.");
            }

            // Validate both inputs before changing axes or invalidating cached graphics.
            double oldMajor = this.majorAxis, oldMinor = this.minorAxis;
            if (axis2 > axis1)
            {
                this.majorAxis = axis2;
                this.minorAxis = axis1;
            }
            else
            {
                this.majorAxis = axis1;
                this.minorAxis = axis2;
            }
            if (oldMajor != this.majorAxis || oldMinor != this.minorAxis)
                this.ClearProxyGraphics();
        }

        /// <summary>
        /// Calculate the local point on the ellipse for a given angle relative to the center.
        /// </summary>
        /// <param name="angle">Angle in degrees.</param>
        /// <returns>A local point on the ellipse for the given angle relative to the center.</returns>
        public Vector2 PolarCoordinateRelativeToCenter(double angle)
        {
            return StablePolarPoint(this.majorAxis, this.minorAxis, angle);
        }

        /// <summary>
        /// Converts the ellipse in a list of vertexes.
        /// </summary>
        /// <param name="precision">Number of vertexes generated.</param>
        /// <returns>A list vertexes that represents the ellipse expressed in object coordinate system.</returns>
        public List<Vector2> PolygonalVertexes(int precision)
        {
            return this.SampleEllipse(precision);
        }

        /// <summary>
        /// Converts the ellipse in a Polyline2D.
        /// </summary>
        /// <param name="precision">Number of vertexes generated.</param>
        /// <returns>A new instance of <see cref="Polyline2D">Polyline2D</see> that represents the ellipse.</returns>
        public Polyline2D ToPolyline2D(int precision)
        {
            List<Vector2> vertexes = this.PolygonalVertexes(precision);
            Vector3 ocsCenter = MathHelper.Transform(this.center, this.Normal, CoordinateSystem.World, CoordinateSystem.Object);
            Polyline2D poly = new Polyline2D
            {
                Layer = (Layer) this.Layer.Clone(),
                Linetype = (Linetype) this.Linetype.Clone(),
                Color = (AciColor) this.Color.Clone(),
                Lineweight = this.Lineweight,
                Transparency = (Transparency) this.Transparency.Clone(),
                LinetypeScale = this.LinetypeScale,
                Normal = this.Normal,
                Elevation = ocsCenter.Z,
                Thickness = this.Thickness,
                IsVisible = this.IsVisible,
                IsClosed = this.IsFullEllipse
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
        /// <remarks>Column-vector convention. Uses the image of the ellipse plane and its principal axes.
        /// Singular/non-finite or unrepresentable images reject before mutation. Arc endpoints are
        /// transformed from the original axes; stale proxy graphics are cleared on a changed transform.</remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            this.ApplyReviewedAffine(transformation, translation);
        }

        /// <summary>Applies a finite affine four-by-four transformation; projective matrices reject.</summary>
        public override void TransformBy(Matrix4 transformation)
        {
            CircularEntityTransform.CheckAffine(transformation);
            this.TransformBy(new Matrix3(transformation.M11, transformation.M12, transformation.M13,
                transformation.M21, transformation.M22, transformation.M23,
                transformation.M31, transformation.M32, transformation.M33),
                new Vector3(transformation.M14, transformation.M24, transformation.M34));
        }

        /// <summary>
        /// Creates a new Ellipse that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Ellipse that is a copy of this instance.</returns>
        public override object Clone()
        {
            Ellipse entity = new Ellipse
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
                //Ellipse properties
                Center = this.center,
                majorAxis = this.majorAxis,
                minorAxis = this.minorAxis,
                Rotation = this.rotation,
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