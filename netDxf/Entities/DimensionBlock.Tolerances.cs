// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Globalization;
using System.Threading;
using netDxf.Tables;
using netDxf.Units;

namespace netDxf.Entities
{
    public static partial class DimensionBlock
    {
        private static string FormatToleranceLabel(string nominal, double measurement, DimensionType type, DimensionStyle style, bool alternate)
        {
            DimensionStyleTolerances tolerance = style.Tolerances;
            if (tolerance.DisplayMethod == DimensionStyleTolerancesDisplayMethod.None) return nominal;
            DimensionToleranceSettings.ToleranceFlag(tolerance); // Validate the enum before producing text.
            double upper = tolerance.UpperLimit;
            double lower = DimensionToleranceSettings.Lower(tolerance);
            double factor = alternate ? style.AlternateUnits.Multiplier : 1.0;
            FiniteTolerance(upper); FiniteTolerance(lower); FiniteTolerance(measurement);
            FiniteTolerance(factor); FiniteTolerance(style.TextFractionHeightScale);
            if (factor <= 0.0 || style.TextFractionHeightScale <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(style), "Tolerance conversion and text-height factors must be positive.");
            string height = style.TextFractionHeightScale.ToString("R", CultureInfo.InvariantCulture);
            if (tolerance.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Limits)
            {
                // Limits use the unrounded scaled measurement. DIMLFAC is not applied to DIMTP/TM.
                string high = ToleranceNumber((measurement + upper) * factor, type, style, alternate);
                string low = ToleranceNumber((measurement - lower) * factor, type, style, alternate);
                string prefix = alternate ? style.AlternateUnits.Prefix : style.DimPrefix;
                if (!alternate && string.IsNullOrEmpty(prefix))
                    prefix = type == DimensionType.Radius ? "R" : type == DimensionType.Diameter ? "Ø" : string.Empty;
                string suffix = alternate ? style.AlternateUnits.Suffix : style.DimSuffix;
                return prefix + "{\\H" + height + "x;\\S" + StackLiteral(high) + "^ " + StackLiteral(low) + ";}" + suffix;
            }
            int alignment = (int)tolerance.VerticalPlacement;
            if (alignment < 0 || alignment > 2)
                throw new ArgumentOutOfRangeException(nameof(style), "Tolerance vertical placement must be bottom, middle or top.");
            string values;
            if (upper == lower)
                values = "±" + ToleranceNumber(Math.Abs(upper) * factor, type, style, alternate);
            else
                values = "\\S" + StackLiteral(SignedTolerance(upper * factor, type, style, alternate))
                    + "^ " + StackLiteral(SignedTolerance(-lower * factor, type, style, alternate)) + ";";
            // Scope alignment and height so repeated placeholders and suffix text cannot inherit them.
            return "{\\A" + alignment.ToString(CultureInfo.InvariantCulture) + ";" + nominal + "{\\H" + height + "x;" + values + "}}";
        }

        private static void FiniteTolerance(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Tolerance values and computed limits must be finite.");
        }

        private static string SignedTolerance(double value, DimensionType type, DimensionStyle style, bool alternate)
        {
            FiniteTolerance(value);
            return (value > 0.0 ? "+" : value < 0.0 ? "-" : " ") + ToleranceNumber(Math.Abs(value), type, style, alternate);
        }

        private static string StackLiteral(string value)
        {
            // Stack rows use character escaping, not the outer MTEXT Unicode syntax.
            // Escape backslashes first so generated escapes are not escaped again.
            // Caret-space survives caret decoding as a literal caret before stack parsing.
            return value.Replace("\\", "\\\\").Replace(";", "\\;").Replace("^", "\\^ ")
                .Replace("/", "\\/").Replace("#", "\\#").Replace("{", "\\{").Replace("}", "\\}");
        }

        private static string ToleranceNumber(double value, DimensionType type, DimensionStyle style, bool alternate)
        {
            FiniteTolerance(value);
            var tolerance = style.Tolerances;
            var format = new UnitStyleFormat
            {
                LinearDecimalPlaces = alternate ? tolerance.AlternatePrecision : tolerance.Precision,
                AngularDecimalPlaces = tolerance.Precision,
                DecimalSeparator = style.DecimalSeparator.ToString(),
                FractionHeightScale = 1.0,
                // Fractions within a tolerance stack stay inline; nested MTEXT stacks are invalid.
                FractionType = FractionFormatType.NotStacked,
                SuppressLinearLeadingZeros = alternate ? tolerance.AlternateSuppressLinearLeadingZeros : tolerance.SuppressLinearLeadingZeros,
                SuppressLinearTrailingZeros = alternate ? tolerance.AlternateSuppressLinearTrailingZeros : tolerance.SuppressLinearTrailingZeros,
                SuppressAngularLeadingZeros = tolerance.SuppressLinearLeadingZeros,
                SuppressAngularTrailingZeros = tolerance.SuppressLinearTrailingZeros,
                SuppressZeroFeet = alternate ? tolerance.AlternateSuppressZeroFeet : tolerance.SuppressZeroFeet,
                SuppressZeroInches = alternate ? tolerance.AlternateSuppressZeroInches : tolerance.SuppressZeroInches
            };
            if (type == DimensionType.Angular || type == DimensionType.Angular3Point)
                return AngleUnitFormat.Format(value, style.DimAngularUnits == AngleUnitType.SurveyorUnits ? AngleUnitType.DecimalDegrees : style.DimAngularUnits, format);
            switch (alternate ? style.AlternateUnits.LengthUnits : style.DimLengthUnits)
            {
                case LinearUnitType.Scientific: return LinearUnitFormat.ToScientific(value, format);
                case LinearUnitType.Decimal: return LinearUnitFormat.ToDecimal(value, format);
                case LinearUnitType.Engineering: return LinearUnitFormat.ToEngineering(value, format);
                case LinearUnitType.Architectural: return LinearUnitFormat.ToArchitectural(value, format);
                case LinearUnitType.Fractional: return LinearUnitFormat.ToFractional(value, format);
                case LinearUnitType.WindowsDesktop:
                    // Explicit tolerance precision remains DIMTDEC/DIMALTTD, not the nominal precision.
                    format.DecimalSeparator = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                    return LinearUnitFormat.ToDecimal(value, format);
                default: throw new ArgumentOutOfRangeException(nameof(style), "Unknown tolerance unit format.");
            }
        }
    }
}
