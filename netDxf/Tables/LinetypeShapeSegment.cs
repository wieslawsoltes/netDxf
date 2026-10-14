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

namespace netDxf.Tables
{
    /// <summary>
    /// Represents a shape linetype segment.
    /// </summary>
    public class LinetypeShapeSegment :
        LinetypeSegment
    {
        #region delegates and events

        public delegate void ShapeStyleChangedEventHandler(LinetypeShapeSegment sender, TableObjectChangedEventArgs<ShapeStyle> e);
        public event ShapeStyleChangedEventHandler ShapeStyleChanged;
        protected virtual ShapeStyle OnShapeStyleChangedEvent(ShapeStyle oldShapeStyle, ShapeStyle newShapeStyle)
        {
            ShapeStyleChangedEventHandler ae = this.ShapeStyleChanged;
            if (ae != null)
            {
                TableObjectChangedEventArgs<ShapeStyle> eventArgs = new TableObjectChangedEventArgs<ShapeStyle>(oldShapeStyle, newShapeStyle);
                ae(this, eventArgs);
                return eventArgs.NewValue;
            }
            return newShapeStyle;
        }

        #endregion

        #region private fields

        private string name;
        private ShapeStyle style;
        private Vector2 offset;
        private LinetypeSegmentRotationType rotationType;
        private double rotation;
        private double scale;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>LinetypeShapeSegment</c> class.
        /// </summary>
        /// <param name="name">Shape name of the linetype segment.</param>
        /// <param name="style">File where the shape of the linetype segment is defined.</param>
        /// <remarks>
        /// DXF stores a numeric shape identifier rather than its name. Set <see cref="ShapeNumber"/>
        /// to save without resolving a local shape file; otherwise the style resolves the name when saving.
        /// </remarks>
        public LinetypeShapeSegment(string name, ShapeStyle style) : this(name, style, 1.0, Vector2.Zero, LinetypeSegmentRotationType.Relative, 0.0, 1.0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>LinetypeShapeSegment</c> class.
        /// </summary>
        /// <param name="name">Shape name of the linetype segment.</param>
        /// <param name="style">File where the shape of the linetype segment is defined.</param>
        /// <param name="length">Dash, dot, or space length of the linetype segment.</param>
        /// <remarks>
        /// Set <see cref="ShapeNumber"/> to preserve a numeric reference independently of local shape files.
        /// </remarks>
        public LinetypeShapeSegment(string name, ShapeStyle style, double length) : this(name, style, length, Vector2.Zero, LinetypeSegmentRotationType.Relative, 0.0, 1.0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>LinetypeShapeSegment</c> class.
        /// </summary>
        /// <param name="name">Shape name of the linetype segment.</param>
        /// <param name="style">File where the shape of the linetype segment is defined.</param>
        /// <param name="length">Dash, dot, or space length of the linetype segment.</param>
        /// <param name="offset">Shift of the shape along the line.</param>
        /// <param name="rotationType">Type of rotation defined by the rotation value.</param>
        /// <param name="rotation">Rotation of the shape.</param>
        /// <param name="scale">Scale of the shape.</param>
        /// <remarks>
        /// Set <see cref="ShapeNumber"/> to preserve a numeric reference independently of local shape files.
        /// </remarks>
        public LinetypeShapeSegment(string name, ShapeStyle style, double length, Vector2 offset, LinetypeSegmentRotationType rotationType, double rotation, double scale)
            : base(LinetypeSegmentType.Shape, length)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name), "The linetype shape name should be at least one character long.");
            }
            this.name = name;
            this.style = style ?? throw new ArgumentNullException(nameof(style));
            this.offset = offset;
            this.rotationType = rotationType;
            this.rotation = MathHelper.NormalizeAngle(rotation);
            this.scale = scale;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or sets the authored DXF group-75 shape identifier, or null to resolve <see cref="Name"/> when saving.
        /// </summary>
        /// <remarks>
        /// A loaded identifier is retained even when the shape resource is unavailable. The signed 16-bit value
        /// preserves the exact DXF word, including zero. A non-null value is authoritative and is copied by Clone.
        /// Changing Name or Style does not implicitly discard it; explicitly assign null to resume name lookup.
        /// This property does not authorize access to any external resource.
        /// </remarks>
        public short? ShapeNumber { get; set; }

        /// <summary>
        /// Gets or sets the name of the shape.
        /// </summary>
        /// <remarks>
        /// DXF stores a shape number. When <see cref="ShapeNumber"/> is non-null it remains authoritative;
        /// set it to null to obtain a new numeric identifier from this name on the next save.
        /// </remarks>
        public string Name
        {
            get { return this.name; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentNullException(nameof(value), "The linetype shape name should be at least one character long.");
                }
                this.name = value;
            }
        }

        /// <summary>
        /// Gets or sets the shape style.
        /// </summary>
        public ShapeStyle Style
        {
            get { return this.style; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                this.style = this.OnShapeStyleChangedEvent(this.style, value);
            }
        }

        /// <summary>
        /// Gets or sets the shift of the shape along the line.
        /// </summary>
        public Vector2 Offset
        {
            get { return this.offset; }
            set { this.offset = value; }
        }

        /// <summary>
        /// Gets or sets the type of rotation defined by the rotation value upright, relative, or absolute.
        /// </summary>
        public LinetypeSegmentRotationType RotationType
        {
            get { return this.rotationType; }
            set { this.rotationType = value; }
        }

        /// <summary>
        /// Gets or sets the angle in degrees of the shape.
        /// </summary>
        public double Rotation
        {
            get { return this.rotation; }
            set { this.rotation = MathHelper.NormalizeAngle(value); }
        }

        /// <summary>
        /// Gets or sets the scale of the shape relative to the scale of the line type.
        /// </summary>
        public double Scale
        {
            get { return this.scale; }
            set { this.scale = value; }
        }

        #endregion

        #region overrides

        /// <summary>
        /// Creates a new <c>LinetypeShapeSegment</c> that is a copy of the current instance.
        /// </summary>
        /// <returns>A new <c>LinetypeShapeSegment</c> that is a copy of this instance.</returns>
        public override object Clone()
        {
            return new LinetypeShapeSegment(this.name, (ShapeStyle) this.style.Clone(), this.Length)
            {
                Offset = this.offset,
                RotationType = this.rotationType,
                Rotation = this.rotation,
                Scale = this.scale,
                ShapeNumber = this.ShapeNumber
            };
        }

        #endregion
    }
}
