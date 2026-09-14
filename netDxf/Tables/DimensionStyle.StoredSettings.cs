using System;

namespace netDxf.Tables
{
    public partial class DimensionStyle
    {
        private double tickSize;
        private double textVerticalPosition;

        /// <summary>Gets or sets the stored oblique stroke size (DIMTSZ). Zero selects arrowheads; positive values select strokes. The default is zero.</summary>
        /// <remarks>This setting is retained in DXF. Dimension block generation does not implement every dimension style setting.</remarks>
        public double TickSize
        {
            get { return this.tickSize; }
            set { ValidateStoredDimensionReal(value, true, nameof(value)); this.tickSize = value; }
        }

        /// <summary>Gets or sets the stored vertical text offset as a multiple of text height (DIMTVP). The default is zero.</summary>
        /// <remarks>This finite signed value applies when DIMTAD is zero. It is stored independently of TextVerticalPlacement.</remarks>
        public double TextVerticalPosition
        {
            get { return this.textVerticalPosition; }
            set { ValidateStoredDimensionReal(value, false, nameof(value)); this.textVerticalPosition = value; }
        }

        /// <summary>Gets or sets the stored user positioned text flag (DIMUPT). The default is false.</summary>
        /// <remarks>This setting controls interactive dimension placement in CAD applications. It does not change an entity's manually positioned text flag.</remarks>
        public bool UserPositionedText { get; set; }

        internal static void ValidateStoredDimensionReal(double value, bool nonnegative, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || (nonnegative && value < 0))
                throw new ArgumentOutOfRangeException(parameter, value, nonnegative ? "The value must be finite and nonnegative." : "The value must be finite.");
        }
    }
}
