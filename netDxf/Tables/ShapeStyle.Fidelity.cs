using System;

namespace netDxf.Tables
{
    public partial class ShapeStyle
    {
        private TextStyleFlags flags = TextStyleFlags.Shape;
        private double? lastHeight;

        /// <summary>Gets or sets all stored group 70 bits, including the required shape discriminator.</summary>
        public TextStyleFlags Flags
        {
            get { return this.flags; }
            set
            {
                if ((value & TextStyleFlags.Shape) == 0) throw new ArgumentException("A ShapeStyle requires the shape flag.", nameof(value));
                this.flags = value;
            }
        }

        /// <summary>Gets or sets the stored group 71 value without assigning rendering meaning to a shape file.</summary>
        public short TextGenerationFlags { get; set; }

        /// <summary>Gets or sets optional group 42. Null omits the value.</summary>
        /// <remarks>The value is retained as stored STYLE metadata; it does not resize shapes.</remarks>
        public double? LastHeight
        {
            get { return this.lastHeight; }
            set
            {
                if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
                    throw new ArgumentOutOfRangeException(nameof(value), "The last height must be finite.");
                this.lastHeight = value;
            }
        }
    }
}
