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

namespace netDxf.Entities
{
    /// <summary>
    /// Represents a <see cref="Polyline2D">Polyline2D</see> vertex.
    /// </summary>
    public class Polyline2DVertex :
        ICloneable
    {
        #region private fields

        private Vector2 position;
        private double startWidth;
        private double endWidth;
        private double bulge;
        private bool hasStartWidth;
        private bool hasEndWidth;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Polyline2DVertex</c> class.
        /// </summary>
        public Polyline2DVertex()
            : this(Vector2.Zero)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Polyline2DVertex</c> class.
        /// </summary>
        /// <param name="x">X coordinate.</param>
        /// <param name="y">Y coordinate.</param>
        public Polyline2DVertex(double x, double y)
            : this(new Vector2(x, y), 0.0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Polyline2DVertex</c> class.
        /// </summary>
        /// <param name="x">X coordinate.</param>
        /// <param name="y">Y coordinate.</param>
        /// <param name="bulge">Vertex bulge (default: 0.0).</param>
        public Polyline2DVertex(double x, double y, double bulge)
            : this(new Vector2(x, y), bulge)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Polyline2DVertex</c> class.
        /// </summary>
        /// <param name="position">Lightweight polyline <see cref="Vector2">vertex</see> coordinates.</param>
        public Polyline2DVertex(Vector2 position)
            : this(position, 0.0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Polyline2DVertex</c> class.
        /// </summary>
        /// <param name="position">Lightweight polyline <see cref="Vector2">vertex</see> coordinates.</param>
        /// <param name="bulge">Vertex bulge (default: 0.0).</param>
        public Polyline2DVertex(Vector2 position, double bulge)
        {
            this.position = position;
            this.bulge = bulge;
            this.startWidth = 0.0;
            this.endWidth = 0.0;
        }

        /// <summary>
        /// Copy constructor.
        /// </summary>
        /// <param name="vertex">A Polyline2D vertex.</param>
        public Polyline2DVertex(Polyline2DVertex vertex)
        {
            this.position = vertex.Position;
            this.bulge = vertex.Bulge;
            this.startWidth = vertex.startWidth;
            this.endWidth = vertex.EndWidth;
            this.hasStartWidth = vertex.hasStartWidth;
            this.hasEndWidth = vertex.hasEndWidth;
            this.VertexIdentifier = vertex.VertexIdentifier;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or sets the polyline 2D vertex <see cref="Vector2">position</see>.
        /// </summary>
        public Vector2 Position
        {
            get { return this.position; }
            set { this.position = value; }
        }

        /// <summary>
        /// Gets or sets the polyline 2D vertex start segment width.
        /// </summary>
        /// <remarks>Widths greater than zero produce wide lines.</remarks>
        public double StartWidth
        {
            get { return this.startWidth; }
            set
            {
                Polyline2D.ValidateWidth(value, nameof(value));
                this.startWidth = value;
                this.hasStartWidth = true;
            }
        }

        /// <summary>
        /// Gets or sets the polyline 2D vertex end segment width.
        /// </summary>
        /// <remarks>Widths greater than zero produce wide lines.</remarks>
        public double EndWidth
        {
            get { return this.endWidth; }
            set
            {
                Polyline2D.ValidateWidth(value, nameof(value));
                this.endWidth = value;
                this.hasEndWidth = true;
            }
        }

        /// <summary>Gets or sets the optional group 40 value. Null is absent; zero is explicitly present.</summary>
        /// <remarks>StartWidth returns zero when this property is absent. This raw value does not apply the polyline's ConstantWidth.</remarks>
        public double? StartWidthOverride
        {
            get { return this.hasStartWidth ? (double?)this.startWidth : null; }
            set
            {
                if (value.HasValue) this.StartWidth = value.Value;
                else { this.startWidth = 0; this.hasStartWidth = false; }
            }
        }

        /// <summary>Gets or sets the optional group 41 value. Null is absent; zero is explicitly present.</summary>
        /// <remarks>EndWidth returns zero when this property is absent. This raw value does not apply the polyline's ConstantWidth.</remarks>
        public double? EndWidthOverride
        {
            get { return this.hasEndWidth ? (double?)this.endWidth : null; }
            set
            {
                if (value.HasValue) this.EndWidth = value.Value;
                else { this.endWidth = 0; this.hasEndWidth = false; }
            }
        }

        /// <summary>Gets or sets the optional signed group 91 vertex identifier.</summary>
        /// <remarks>The identifier belongs to the vertex position, not its outgoing segment.
        /// Values are preserved as opaque identifiers; netDxf does not allocate IDs or interpret their application semantics.</remarks>
        public int? VertexIdentifier { get; set; }

        /// <summary>
        /// Gets or set the polyline 2D vertex bulge.
        /// </summary>
        /// <remarks>
        /// The bulge is the tangent of one fourth the included angle for an arc segment, 
        /// made negative if the arc goes clockwise from the start point to the endpoint. 
        /// A bulge of 0 indicates a straight segment, and a bulge of 1 is a semicircle.
        /// </remarks>
        public double Bulge
        {
            get { return this.bulge; }
            set { this.bulge = value; }
        }

        #endregion

        #region overrides

        /// <summary>
        /// Converts the value of this instance to its equivalent string representation.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return string.Format("{0}: ({1})", "Polyline2DVertex", this.position);
        }

        /// <summary>
        /// Creates a new Polyline2DVertex that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Polyline2DVertex that is a copy of this instance.</returns>
        public object Clone()
        {
            return new Polyline2DVertex(this);
        }

        #endregion
    }
}