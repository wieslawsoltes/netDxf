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
    /// Represents a wipeout <see cref="EntityObject">entity</see>.
    /// </summary>
    /// <remarks>
    /// The Wipeout DXF definition includes three variables for brightness, contrast, and fade but those variables have no effect; in AutoCad you cannot even change them.<br/>
    /// The Wipeout entity is related with the system variable WIPEOUTFRAME but this variable is not saved in a DXF.
    /// </remarks>
    public class Wipeout :
        EntityObject
    {
        #region private fields

        private ClippingBoundary clippingBoundary;
        private double elevation;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Wipeout</c> class as a rectangular wipeout.
        /// </summary>
        /// <param name="x">Rectangle x-coordinate of the bottom-left corner in local coordinates.</param>
        /// <param name="y">Rectangle y-coordinate of the bottom-left corner in local coordinates.</param>
        /// <param name="width">Rectangle width in local coordinates.</param>
        /// <param name="height">Rectangle height in local coordinates.</param>
        public Wipeout(double x, double y, double width, double height)
            : this(new ClippingBoundary(x, y, width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Wipeout</c> class as a rectangular wipeout from two opposite corners.
        /// </summary>
        /// <param name="firstCorner">Rectangle firstCorner in local coordinates.</param>
        /// <param name="secondCorner">Rectangle secondCorner in local coordinates.</param>
        public Wipeout(Vector2 firstCorner, Vector2 secondCorner)
            : this(new ClippingBoundary(firstCorner, secondCorner))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Wipeout</c> class as a polygonal wipeout.
        /// </summary>
        /// <param name="vertexes">The list of vertexes of the wipeout.</param>
        public Wipeout(IEnumerable<Vector2> vertexes)
            : this(new ClippingBoundary(vertexes))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Wipeout</c> class.
        /// </summary>
        /// <param name="clippingBoundary">The wipeout clipping boundary.</param>
        public Wipeout(ClippingBoundary clippingBoundary)
            : base(EntityType.Wipeout, DxfObjectCode.Wipeout)
        {
            this.clippingBoundary = clippingBoundary ?? throw new ArgumentNullException(nameof(clippingBoundary));
            this.elevation = 0.0;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or sets the wipeout clipping boundary.
        /// </summary>
        public ClippingBoundary ClippingBoundary
        {
            get { return this.clippingBoundary; }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(this.clippingBoundary, value)) return;
                this.clippingBoundary = value;
                this.ClearProxyGraphics();
            }
        }

        /// <summary>
        /// Gets or sets the wipeout elevation.
        /// </summary>
        /// <remarks>This is the distance from the origin to the plane of the wipeout boundary.</remarks>
        public double Elevation
        {
            get { return this.elevation; }
            set { PrimitiveGeometryMutation.Assign(this, ref this.elevation, value); }
        }

        #endregion

        #region overrides

        /// <summary>
        /// Moves, scales, and/or rotates the current entity given a 3x3 transformation matrix and a translation vector.
        /// </summary>
        /// <param name="transformation">Transformation matrix.</param>
        /// <param name="translation">Translation vector.</param>
        /// <remarks>
        /// Column-vector convention. Rectangles become polygonal when their image is not axis-aligned in the new OCS.
        /// Non-finite or numerically degenerate planes reject before mutation. Changed geometry invalidates proxy graphics.
        /// </remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            // A rectangle stores only opposite corners. Transform all four corners:
            // a rotated or sheared rectangle need not be rectangular in the new OCS.
            IReadOnlyList<Vector2> source = this.clippingBoundary.Vertexes;
            bool rectangular = this.clippingBoundary.Type == ClippingBoundaryType.Rectangular;
            Vector2[] points;
            if (rectangular)
            {
                Vector2 a = source[0], b = source[1];
                points = new[] { a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y) };
            }
            else
            {
                points = new Vector2[source.Count];
                for (int i = 0; i < points.Length; i++) points[i] = source[i];
            }

            // Derive the image plane from its two axes, not A * Normal. The same
            // finite/rank/representation policy is used by SOLID, TRACE and polylines.
            PlanarEntityTransform result = PlanarEntityTransform.Prepare(transformation, translation,
                points, base.Normal, this.elevation, 0.0);
            if (!result.Changed) return;

            Vector2[] next = result.Vertexes;
            bool axisAligned = rectangular &&
                ((next[0].Y == next[1].Y && next[1].X == next[2].X &&
                  next[2].Y == next[3].Y && next[3].X == next[0].X) ||
                 (next[0].X == next[1].X && next[1].Y == next[2].Y &&
                  next[2].X == next[3].X && next[3].Y == next[0].Y));
            ClippingBoundary boundary = axisAligned
                ? new ClippingBoundary(next[0], next[2]) : new ClippingBoundary(next);

            // Finish all validation/allocation before publishing. Bypass overridable
            // Normal accessors so a callback cannot leave half-published geometry.
            base.Normal = result.Normal;
            this.elevation = result.Elevation;
            this.clippingBoundary = boundary;
            this.ClearProxyGraphics();
        }

        /// <summary>Applies a finite affine transform; projective matrices reject before mutation.</summary>
        public override void TransformBy(Matrix4 transformation)
        {
            PlanarEntityTransform.CheckAffine(transformation);
            this.TransformBy(new Matrix3(transformation.M11, transformation.M12, transformation.M13,
                transformation.M21, transformation.M22, transformation.M23,
                transformation.M31, transformation.M32, transformation.M33),
                new Vector3(transformation.M14, transformation.M24, transformation.M34));
        }

        /// <summary>
        /// Creates a new Wipeout that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Wipeout that is a copy of this instance.</returns>
        public override object Clone()
        {
            Wipeout entity = new Wipeout((ClippingBoundary) this.ClippingBoundary.Clone())
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
                //Wipeout properties
                Elevation = this.elevation
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