// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Objects
{
    /// <summary>Stored non-structural TABLEFORMAT scalar values.</summary>
    /// <remarks>Immutable stored values. Numeric flags/codes are not normalized; floating-point values must be finite.</remarks>
    public sealed class DxfCellStyleFormatValues
    {
        /// <summary>Creates an immutable set of stored values; no geometry or formatting is evaluated.</summary>
        public DxfCellStyleFormatValues(int storedPropertyOverrides, int storedMergeFlags, short storedBackgroundColor, int storedContentLayout)
        {
            this.StoredPropertyOverrides = storedPropertyOverrides;
            this.StoredMergeFlags = storedMergeFlags;
            this.StoredBackgroundColor = storedBackgroundColor;
            this.StoredContentLayout = storedContentLayout;
        }
        /// <summary>Gets the StoredPropertyOverrides stored value.</summary>
        public int StoredPropertyOverrides { get; }
        /// <summary>Gets the StoredMergeFlags stored value.</summary>
        public int StoredMergeFlags { get; }
        /// <summary>Gets the StoredBackgroundColor stored value.</summary>
        public short StoredBackgroundColor { get; }
        /// <summary>Gets the StoredContentLayout stored value.</summary>
        public int StoredContentLayout { get; }
    }
    /// <summary>Stored CONTENTFORMAT values, without evaluating the format expression.</summary>
    /// <remarks>Immutable stored values. Numeric flags/codes are not normalized; floating-point values must be finite.</remarks>
    public sealed class DxfCellContentFormatValues
    {
        /// <summary>Creates an immutable set of stored values; no geometry or formatting is evaluated.</summary>
        public DxfCellContentFormatValues(int storedPropertyOverrides, int storedPropertyFlags, int storedDataType, int storedUnitType, string formatString, double rotation, double blockScale, int storedAlignment, short storedColor, double textHeight)
        {
            this.StoredPropertyOverrides = storedPropertyOverrides;
            this.StoredPropertyFlags = storedPropertyFlags;
            this.StoredDataType = storedDataType;
            this.StoredUnitType = storedUnitType;
            DxfStoredTableContent.CheckEditableText(formatString, nameof(formatString));
            this.FormatString = formatString;
            if (double.IsNaN(rotation) || double.IsInfinity(rotation)) throw new ArgumentOutOfRangeException(nameof(rotation));
            this.Rotation = rotation;
            if (double.IsNaN(blockScale) || double.IsInfinity(blockScale)) throw new ArgumentOutOfRangeException(nameof(blockScale));
            this.BlockScale = blockScale;
            this.StoredAlignment = storedAlignment;
            this.StoredColor = storedColor;
            if (double.IsNaN(textHeight) || double.IsInfinity(textHeight)) throw new ArgumentOutOfRangeException(nameof(textHeight));
            this.TextHeight = textHeight;
        }
        /// <summary>Gets the StoredPropertyOverrides stored value.</summary>
        public int StoredPropertyOverrides { get; }
        /// <summary>Gets the StoredPropertyFlags stored value.</summary>
        public int StoredPropertyFlags { get; }
        /// <summary>Gets the StoredDataType stored value.</summary>
        public int StoredDataType { get; }
        /// <summary>Gets the StoredUnitType stored value.</summary>
        public int StoredUnitType { get; }
        /// <summary>Gets the FormatString stored value.</summary>
        public string FormatString { get; }
        /// <summary>Gets the Rotation stored value.</summary>
        public double Rotation { get; }
        /// <summary>Gets the BlockScale stored value.</summary>
        public double BlockScale { get; }
        /// <summary>Gets the StoredAlignment stored value.</summary>
        public int StoredAlignment { get; }
        /// <summary>Gets the StoredColor stored value.</summary>
        public short StoredColor { get; }
        /// <summary>Gets the TextHeight stored value.</summary>
        public double TextHeight { get; }
    }
    /// <summary>Stored CELLMARGIN values in their source order.</summary>
    /// <remarks>Immutable stored values. Numeric flags/codes are not normalized; floating-point values must be finite.</remarks>
    public sealed class DxfCellMargins
    {
        /// <summary>Creates an immutable set of stored values; no geometry or formatting is evaluated.</summary>
        public DxfCellMargins(double verticalMargin, double horizontalMargin, double bottomMargin, double rightMargin, double horizontalSpacing, double verticalSpacing)
        {
            if (double.IsNaN(verticalMargin) || double.IsInfinity(verticalMargin)) throw new ArgumentOutOfRangeException(nameof(verticalMargin));
            this.VerticalMargin = verticalMargin;
            if (double.IsNaN(horizontalMargin) || double.IsInfinity(horizontalMargin)) throw new ArgumentOutOfRangeException(nameof(horizontalMargin));
            this.HorizontalMargin = horizontalMargin;
            if (double.IsNaN(bottomMargin) || double.IsInfinity(bottomMargin)) throw new ArgumentOutOfRangeException(nameof(bottomMargin));
            this.BottomMargin = bottomMargin;
            if (double.IsNaN(rightMargin) || double.IsInfinity(rightMargin)) throw new ArgumentOutOfRangeException(nameof(rightMargin));
            this.RightMargin = rightMargin;
            if (double.IsNaN(horizontalSpacing) || double.IsInfinity(horizontalSpacing)) throw new ArgumentOutOfRangeException(nameof(horizontalSpacing));
            this.HorizontalSpacing = horizontalSpacing;
            if (double.IsNaN(verticalSpacing) || double.IsInfinity(verticalSpacing)) throw new ArgumentOutOfRangeException(nameof(verticalSpacing));
            this.VerticalSpacing = verticalSpacing;
        }
        /// <summary>Gets the VerticalMargin stored value.</summary>
        public double VerticalMargin { get; }
        /// <summary>Gets the HorizontalMargin stored value.</summary>
        public double HorizontalMargin { get; }
        /// <summary>Gets the BottomMargin stored value.</summary>
        public double BottomMargin { get; }
        /// <summary>Gets the RightMargin stored value.</summary>
        public double RightMargin { get; }
        /// <summary>Gets the HorizontalSpacing stored value.</summary>
        public double HorizontalSpacing { get; }
        /// <summary>Gets the VerticalSpacing stored value.</summary>
        public double VerticalSpacing { get; }
    }
    /// <summary>Stored GRIDFORMAT scalar values, without interpreting visibility or override bits.</summary>
    /// <remarks>Immutable stored values. Numeric flags/codes are not normalized; floating-point values must be finite.</remarks>
    public sealed class DxfCellGridFormatValues
    {
        /// <summary>Creates an immutable set of stored values; no geometry or formatting is evaluated.</summary>
        public DxfCellGridFormatValues(int storedPropertyOverrides, int storedBorderType, short storedColor, int storedLineweight, int storedVisibility, double doubleLineSpacing)
        {
            this.StoredPropertyOverrides = storedPropertyOverrides;
            this.StoredBorderType = storedBorderType;
            this.StoredColor = storedColor;
            this.StoredLineweight = storedLineweight;
            this.StoredVisibility = storedVisibility;
            if (double.IsNaN(doubleLineSpacing) || double.IsInfinity(doubleLineSpacing)) throw new ArgumentOutOfRangeException(nameof(doubleLineSpacing));
            this.DoubleLineSpacing = doubleLineSpacing;
        }
        /// <summary>Gets the StoredPropertyOverrides stored value.</summary>
        public int StoredPropertyOverrides { get; }
        /// <summary>Gets the StoredBorderType stored value.</summary>
        public int StoredBorderType { get; }
        /// <summary>Gets the StoredColor stored value.</summary>
        public short StoredColor { get; }
        /// <summary>Gets the StoredLineweight stored value.</summary>
        public int StoredLineweight { get; }
        /// <summary>Gets the StoredVisibility stored value.</summary>
        public int StoredVisibility { get; }
        /// <summary>Gets the DoubleLineSpacing stored value.</summary>
        public double DoubleLineSpacing { get; }
    }
}
