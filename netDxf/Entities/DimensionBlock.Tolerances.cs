// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Globalization;
using System.Text;
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
            // Angular allowances are already expressed in DIMAUNIT. Convert only
            // the degree-valued nominal measurement, before adding limit allowances.
            if (type == DimensionType.Angular || type == DimensionType.Angular3Point)
            {
                if (style.DimAngularUnits == AngleUnitType.Gradians) measurement *= MathHelper.DegToGrad;
                else if (style.DimAngularUnits == AngleUnitType.Radians) measurement *= MathHelper.DegToRad;
                FiniteTolerance(measurement);
            }
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
            // MTEXT stack content uses backslash escapes, not Unicode commands.
            // Caret decoding precedes stack parsing, so a literal caret also needs
            // its space terminator: \^ becomes a literal only as \^ followed by space.
            var text = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '\\' || c == '/' || c == '#' || c == '^' || c == ';' || c == '{' || c == '}')
                    text.Append('\\');
                text.Append(c);
                if (c == '^') text.Append(' ');
            }
            return text.ToString();
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
            {
                // value is now in display units. Reapplying degree conversion
                // would scale the allowances and already-converted limit endpoints.
                switch (style.DimAngularUnits)
                {
                    case AngleUnitType.DecimalDegrees:
                    case AngleUnitType.SurveyorUnits: return AngleUnitFormat.ToDecimal(value, format);
                    case AngleUnitType.DegreesMinutesSeconds: return AngleUnitFormat.ToDegreesMinutesSeconds(value, format);
                    case AngleUnitType.Gradians: return LinearUnitFormat.ToDecimal(value, format) + format.GradiansSymbol;
                    case AngleUnitType.Radians: return LinearUnitFormat.ToDecimal(value, format) + format.RadiansSymbol;
                    default: throw new ArgumentOutOfRangeException(nameof(style), "Unknown tolerance angle format.");
                }
            }
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
