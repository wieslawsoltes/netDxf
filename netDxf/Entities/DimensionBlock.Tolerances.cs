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
        // Tolerance values are already expressed in display units. DIMLFAC scales
        // the measurement, never DIMTP/DIMTM. Angular bounds use DIMAUNIT itself.
        private static string FormatToleranceText(double scaledMeasurement, string nominal,
            DimensionType dimensionType, string prefix, DimensionStyle style)
        {
            DimensionStyleTolerances tolerance = style.Tolerances;
            DimensionStyleTolerancesDisplayMethod method = tolerance.DisplayMethod;
            if (method != DimensionStyleTolerancesDisplayMethod.Symmetrical &&
                method != DimensionStyleTolerancesDisplayMethod.Deviation &&
                method != DimensionStyleTolerancesDisplayMethod.Limits)
                throw new ArgumentOutOfRangeException(nameof(style), "Unknown tolerance display method.");
            UnitFormatMath.CheckFinite(tolerance.UpperLimit, nameof(tolerance.UpperLimit));
            double lower = DimensionToleranceValue.Lower(tolerance);
            UnitFormatMath.CheckFinite(lower, nameof(tolerance.LowerLimit));
            UnitFormatMath.CheckFinite(style.TextFractionHeightScale, nameof(style.TextFractionHeightScale));
            if (style.TextFractionHeightScale <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(style), "Tolerance text scale must be positive.");
            int alignment = (int)tolerance.VerticalPlacement;
            if (alignment < 0 || alignment > 2)
                throw new ArgumentOutOfRangeException(nameof(style), "Unknown tolerance vertical placement.");

            bool angular = dimensionType == DimensionType.Angular || dimensionType == DimensionType.Angular3Point;
            double measurement = scaledMeasurement;
            if (angular)
            {
                if (style.DimAngularUnits == AngleUnitType.Gradians) measurement *= MathHelper.DegToGrad;
                else if (style.DimAngularUnits == AngleUnitType.Radians) measurement *= MathHelper.DegToRad;
            }
            string text = ToleranceComponent(measurement, nominal, tolerance.UpperLimit, lower,
                style, angular, false, prefix, style.DimSuffix);
            if (!angular && style.AlternateUnits.Enabled)
            {
                // Reuse the existing alternate formatter and its validation. Precision
                // and zero suppression for tolerances are independent of the nominal.
                string alternate = FormatAlternateUnits(scaledMeasurement, style);
                double multiplier = style.AlternateUnits.Multiplier;
                string content = ToleranceComponent(scaledMeasurement * multiplier,
                    alternate.Substring(1, alternate.Length - 2), tolerance.UpperLimit * multiplier,
                    lower * multiplier, style, false, true,
                    style.AlternateUnits.Prefix, style.AlternateUnits.Suffix);
                text += "[" + content + "]";
            }
            return text;
        }

        private static string ToleranceComponent(double measurement, string nominal, double upper, double lower,
            DimensionStyle style, bool angular, bool alternate, string prefix, string suffix)
        {
            UnitFormatMath.CheckFinite(upper, nameof(upper));
            UnitFormatMath.CheckFinite(lower, nameof(lower));
            DimensionStyleTolerances t = style.Tolerances;
            string height = style.TextFractionHeightScale.ToString("G17", CultureInfo.InvariantCulture);
            string size = "{\\H" + height + "x;";
            if (t.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Limits)
            {
                // Limit numbers use the unrounded measurement plus/minus the bounds,
                // not DIMRND or a rounded primary/alternate label.
                double maximum = measurement + upper, minimum = measurement - lower;
                string maximumText = ToleranceNumber(maximum, style, angular, alternate);
                string minimumText = ToleranceNumber(minimum, style, angular, alternate);
                return prefix + size + ToleranceStack(maximumText, minimumText) + "}" + suffix;
            }

            // DIMTOL stores one flag for both names. Exact equal bounds use +/-;
            // unequal bounds use deviations, consistent before and after typed IO.
            string variation = upper == lower
                ? "±" + ToleranceNumber(Math.Abs(upper), style, angular, alternate)
                : ToleranceStack(ToleranceSignedNumber(upper, style, angular, alternate),
                    ToleranceSignedNumber(-lower, style, angular, alternate));
            return "{\\A" + ((int)t.VerticalPlacement).ToString(CultureInfo.InvariantCulture) + ";"
                + nominal + size + variation + "}}";
        }

        private static string ToleranceSignedNumber(double value, DimensionStyle style, bool angular, bool alternate)
        {
            // Both signs of exact zero render without a sign, but remain untouched
            // in the source fields. The lower stored value has the opposite sign.
            string sign = value > 0.0 ? "+" : value < 0.0 ? "-" : string.Empty;
            return sign + ToleranceNumber(Math.Abs(value), style, angular, alternate);
        }

        private static string ToleranceNumber(double value, DimensionStyle style, bool angular, bool alternate)
        {
            UnitFormatMath.CheckFinite(value, nameof(value));
            DimensionStyleTolerances t = style.Tolerances;
            var format = new UnitStyleFormat
            {
                LinearDecimalPlaces = alternate ? t.AlternatePrecision : t.Precision,
                AngularDecimalPlaces = t.Precision,
                DecimalSeparator = style.DecimalSeparator.ToString(),
                FractionType = FractionFormatType.NotStacked,
                FractionHeightScale = style.TextFractionHeightScale,
                SuppressLinearLeadingZeros = alternate ? t.AlternateSuppressLinearLeadingZeros : t.SuppressLinearLeadingZeros,
                SuppressLinearTrailingZeros = alternate ? t.AlternateSuppressLinearTrailingZeros : t.SuppressLinearTrailingZeros,
                SuppressAngularLeadingZeros = t.SuppressLinearLeadingZeros,
                SuppressAngularTrailingZeros = t.SuppressLinearTrailingZeros,
                SuppressZeroFeet = alternate ? t.AlternateSuppressZeroFeet : t.SuppressZeroFeet,
                SuppressZeroInches = alternate ? t.AlternateSuppressZeroInches : t.SuppressZeroInches
            };
            if (angular)
            {
                switch (style.DimAngularUnits)
                {
                    case AngleUnitType.DecimalDegrees:
                    case AngleUnitType.SurveyorUnits: return AngleUnitFormat.ToDecimal(value, format);
                    case AngleUnitType.DegreesMinutesSeconds: return AngleUnitFormat.ToDegreesMinutesSeconds(value, format);
                    // value is already expressed in the selected angular unit.
                    case AngleUnitType.Gradians: return LinearUnitFormat.ToDecimal(value, format) + format.GradiansSymbol;
                    case AngleUnitType.Radians: return LinearUnitFormat.ToDecimal(value, format) + format.RadiansSymbol;
                    default: throw new ArgumentOutOfRangeException(nameof(style), "Unknown tolerance angle format.");
                }
            }
            LinearUnitType units = alternate ? style.AlternateUnits.LengthUnits : style.DimLengthUnits;
            switch (units)
            {
                case LinearUnitType.Scientific: return LinearUnitFormat.ToScientific(value, format);
                case LinearUnitType.Decimal: return LinearUnitFormat.ToDecimal(value, format);
                case LinearUnitType.Engineering: return LinearUnitFormat.ToEngineering(value, format);
                case LinearUnitType.Architectural: return LinearUnitFormat.ToArchitectural(value, format);
                case LinearUnitType.Fractional: return LinearUnitFormat.ToFractional(value, format);
                case LinearUnitType.WindowsDesktop:
                    format.DecimalSeparator = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                    // DIMTDEC/DIMALTTD, not the desktop's nominal precision, control tolerances.
                    return LinearUnitFormat.ToDecimal(value, format);
                default: throw new ArgumentOutOfRangeException(nameof(style), "Unknown tolerance linear format.");
            }
        }

        private static string ToleranceStack(string upper, string lower)
        {
            return "\\S" + EscapeToleranceStack(upper) + "^ " + EscapeToleranceStack(lower) + ";";
        }

        private static string EscapeToleranceStack(string value)
        {
            // Fractions in a row must not become the outer stack separator. Do not
            // nest MTEXT stacks; fractional rows use the existing unstacked format.
            // The space after ^ prevents mandatory MTEXT caret decoding.
            var text = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '\\' || c == '/' || c == '#' || c == '^' || c == ';' || c == '{' || c == '}') text.Append('\\');
                text.Append(c);
            }
            return text.ToString();
        }

        private static void ApplyToleranceOverride(DimensionStyleTolerances t, DimensionStyleOverride item)
        {
            switch (item.Type)
            {
                case DimensionStyleOverrideType.TolerancesDisplayMethod: t.DisplayMethod = (DimensionStyleTolerancesDisplayMethod)item.Value; break;
                case DimensionStyleOverrideType.TolerancesUpperLimit: t.UpperLimit = (double)item.Value; break;
                case DimensionStyleOverrideType.TolerancesLowerLimit: t.LowerLimit = (double)item.Value; break;
                case DimensionStyleOverrideType.TolerancesVerticalPlacement: t.VerticalPlacement = (DimensionStyleTolerancesVerticalPlacement)item.Value; break;
                case DimensionStyleOverrideType.TolerancesPrecision: t.Precision = (short)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressLinearLeadingZeros: t.SuppressLinearLeadingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressLinearTrailingZeros: t.SuppressLinearTrailingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressZeroFeet: t.SuppressZeroFeet = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressZeroInches: t.SuppressZeroInches = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAlternatePrecision: t.AlternatePrecision = (short)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressLinearLeadingZeros: t.AlternateSuppressLinearLeadingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressLinearTrailingZeros: t.AlternateSuppressLinearTrailingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressZeroFeet: t.AlternateSuppressZeroFeet = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressZeroInches: t.AlternateSuppressZeroInches = (bool)item.Value; break;
            }
        }
    }
}
