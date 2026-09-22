// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Threading;
using netDxf.Tables;
using netDxf.Units;

namespace netDxf.Entities
{
    public static partial class DimensionBlock
    {
        // Alternate rounding operates on the scaled measurement, not the already
        // rounded/displayed primary text. No source style or geometry is changed.
        private static string FormatAlternateUnits(double scaledMeasurement, DimensionStyle style)
        {
            DimensionStyleAlternateUnits alternate = style.AlternateUnits;
            if (double.IsNaN(alternate.Multiplier) || double.IsInfinity(alternate.Multiplier) || alternate.Multiplier <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(style), "Alternate-unit multiplier must be finite and positive.");
            if (double.IsNaN(alternate.Roundoff) || double.IsInfinity(alternate.Roundoff) || alternate.Roundoff < 0.0)
                throw new ArgumentOutOfRangeException(nameof(style), "Alternate-unit rounding must be finite and nonnegative.");
            double measurement = scaledMeasurement * alternate.Multiplier;
            if (double.IsNaN(measurement) || double.IsInfinity(measurement))
                throw new ArgumentOutOfRangeException(nameof(scaledMeasurement), "Alternate measurement must be finite.");
            if (alternate.Roundoff > 0.0) measurement = MathHelper.RoundToNearest(measurement, alternate.Roundoff);
            if (double.IsNaN(measurement) || double.IsInfinity(measurement))
                throw new ArgumentOutOfRangeException(nameof(style), "Rounded alternate measurement must be finite.");
            var format = new UnitStyleFormat
            {
                LinearDecimalPlaces = alternate.LengthPrecision,
                DecimalSeparator = style.DecimalSeparator.ToString(),
                FractionHeightScale = style.TextFractionHeightScale,
                FractionType = alternate.StackUnits ? FractionFormatType.Horizontal : FractionFormatType.NotStacked,
                SuppressLinearLeadingZeros = alternate.SuppressLinearLeadingZeros,
                SuppressLinearTrailingZeros = alternate.SuppressLinearTrailingZeros,
                SuppressZeroFeet = alternate.SuppressZeroFeet,
                SuppressZeroInches = alternate.SuppressZeroInches
            };
            string text;
            switch (alternate.LengthUnits)
            {
                case LinearUnitType.Scientific: text = LinearUnitFormat.ToScientific(measurement, format); break;
                case LinearUnitType.Decimal: text = LinearUnitFormat.ToDecimal(measurement, format); break;
                case LinearUnitType.Engineering: text = LinearUnitFormat.ToEngineering(measurement, format); break;
                case LinearUnitType.Architectural: text = LinearUnitFormat.ToArchitectural(measurement, format); break;
                case LinearUnitType.Fractional: text = LinearUnitFormat.ToFractional(measurement, format); break;
                case LinearUnitType.WindowsDesktop:
                    format.LinearDecimalPlaces = (short)Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalDigits;
                    format.DecimalSeparator = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                    text = LinearUnitFormat.ToDecimal(measurement, format);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(style), "Unknown alternate-unit format.");
            }
            // Limit the scope of stacked MTEXT formatting to the alternate value.
            if (text.StartsWith("\\A1;", StringComparison.Ordinal)) text = "{" + text + "}";
            return "[" + alternate.Prefix + text + alternate.Suffix + "]";
        }

        private static void ApplyAlternateUnitOverride(DimensionStyleAlternateUnits alternate, DimensionStyleOverride item)
        {
            switch (item.Type)
            {
                case DimensionStyleOverrideType.AltUnitsEnabled: alternate.Enabled = (bool)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsLengthUnits: alternate.LengthUnits = (LinearUnitType)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsStackedUnits: alternate.StackUnits = (bool)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsLengthPrecision: alternate.LengthPrecision = (short)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsMultiplier: alternate.Multiplier = (double)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsRoundoff: alternate.Roundoff = (double)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsPrefix: alternate.Prefix = (string)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsSuffix: alternate.Suffix = (string)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsSuppressLinearLeadingZeros: alternate.SuppressLinearLeadingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsSuppressLinearTrailingZeros: alternate.SuppressLinearTrailingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsSuppressZeroFeet: alternate.SuppressZeroFeet = (bool)item.Value; break;
                case DimensionStyleOverrideType.AltUnitsSuppressZeroInches: alternate.SuppressZeroInches = (bool)item.Value; break;
            }
        }
    }
}
