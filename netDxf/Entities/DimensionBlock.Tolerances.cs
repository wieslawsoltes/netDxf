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
        // Bounds are in displayed primary units, independent of DIMLFAC/DIMRND.
        // The incoming measurement is already DIMLFAC-scaled but not rounded.
        private static string FormatToleranceText(string nominal, double measurement,
            DimensionStyle style, bool angular, bool alternate)
        {
            DimensionStyleTolerances tolerance = style.Tolerances;
            DimensionStyleTolerancesDisplayMethod mode = tolerance.DisplayMethod;
            if (mode == DimensionStyleTolerancesDisplayMethod.None) return nominal;
            if (mode != DimensionStyleTolerancesDisplayMethod.Symmetrical &&
                mode != DimensionStyleTolerancesDisplayMethod.Deviation && mode != DimensionStyleTolerancesDisplayMethod.Limits)
                throw new ArgumentOutOfRangeException(nameof(style), "Unknown tolerance presentation.");
            UnitFormatMath.CheckFinite(tolerance.UpperLimit, nameof(style));
            UnitFormatMath.CheckFinite(tolerance.LowerLimit, nameof(style));
            UnitFormatMath.CheckFinite(style.TextFractionHeightScale, nameof(style));
            if (style.TextFractionHeightScale <= 0 || (int)tolerance.VerticalPlacement < 0 || (int)tolerance.VerticalPlacement > 2)
                throw new ArgumentOutOfRangeException(nameof(style), "Tolerance height and alignment must be valid.");
            double multiplier = alternate ? style.AlternateUnits.Multiplier : 1;
            double upper = tolerance.UpperLimit * multiplier, lower = tolerance.LowerLimit * multiplier;
            UnitFormatMath.CheckFinite(upper, nameof(style)); UnitFormatMath.CheckFinite(lower, nameof(style));
            if (angular)
            {
                if (style.DimAngularUnits == AngleUnitType.Radians) measurement *= MathHelper.DegToRad;
                else if (style.DimAngularUnits == AngleUnitType.Gradians) measurement *= MathHelper.DegToGrad;
            }
            var format = new UnitStyleFormat
            {
                LinearDecimalPlaces = alternate ? tolerance.AlternatePrecision : tolerance.Precision,
                AngularDecimalPlaces = tolerance.Precision,
                DecimalSeparator = style.DecimalSeparator.ToString(),
                FractionHeightScale = 1,
                // A fraction within a tolerance row is linear text, not a nested stack.
                FractionType = FractionFormatType.NotStacked,
                SuppressLinearLeadingZeros = alternate ? tolerance.AlternateSuppressLinearLeadingZeros : tolerance.SuppressLinearLeadingZeros,
                SuppressLinearTrailingZeros = alternate ? tolerance.AlternateSuppressLinearTrailingZeros : tolerance.SuppressLinearTrailingZeros,
                SuppressAngularLeadingZeros = tolerance.SuppressLinearLeadingZeros,
                SuppressAngularTrailingZeros = tolerance.SuppressLinearTrailingZeros,
                SuppressZeroFeet = alternate ? tolerance.AlternateSuppressZeroFeet : tolerance.SuppressZeroFeet,
                SuppressZeroInches = alternate ? tolerance.AlternateSuppressZeroInches : tolerance.SuppressZeroInches
            };
            string height = "{\\H" + style.TextFractionHeightScale.ToString("R", CultureInfo.InvariantCulture) + "x;";
            if (mode == DimensionStyleTolerancesDisplayMethod.Limits)
            {
                UnitFormatMath.CheckFinite(measurement, nameof(measurement));
                string top = FormatToleranceNumber(measurement + upper, style, format, angular, alternate);
                string bottom = FormatToleranceNumber(measurement - lower, style, format, angular, alternate);
                return height + "\\S" + EscapeToleranceStack(top) + "^ " + EscapeToleranceStack(bottom) + ";}";
            }
            string text;
            if (mode == DimensionStyleTolerancesDisplayMethod.Symmetrical || upper == lower)
                text = "±" + FormatToleranceNumber(Math.Abs(upper), style, format, angular, alternate);
            else
                text = "\\S" + EscapeToleranceStack(ToleranceSign(upper) + FormatToleranceNumber(Math.Abs(upper), style, format, angular, alternate))
                    + "^ " + EscapeToleranceStack(ToleranceSign(-lower) + FormatToleranceNumber(Math.Abs(lower), style, format, angular, alternate)) + ";";
            // Scope both alignment and relative height; following alternate/user text
            // must not inherit a tolerance-specific font size or baseline.
            return "{\\A" + ((int)tolerance.VerticalPlacement).ToString(CultureInfo.InvariantCulture) + ";" + nominal + height + text + "}}";
        }

        private static string ToleranceSign(double value) { return value > 0 ? "+" : value < 0 ? "-" : string.Empty; }

        private static string EscapeToleranceStack(string text)
        {
            return text.Replace("\\", "\\\\").Replace("/", "\\/").Replace("#", "\\#")
                .Replace("^", "\\^").Replace(";", "\\;").Replace("{", "\\{").Replace("}", "\\}");
        }

        private static string FormatToleranceNumber(double value, DimensionStyle style, UnitStyleFormat format, bool angular, bool alternate)
        {
            UnitFormatMath.Validate(value, format, angular);
            if (angular)
            {
                switch (style.DimAngularUnits)
                {
                    case AngleUnitType.DecimalDegrees:
                    case AngleUnitType.SurveyorUnits: return AngleUnitFormat.ToDecimal(value, format);
                    case AngleUnitType.DegreesMinutesSeconds: return AngleUnitFormat.ToDegreesMinutesSeconds(value, format);
                    case AngleUnitType.Gradians:
                    case AngleUnitType.Radians:
                        return UnitFormatMath.Fixed(value, format.AngularDecimalPlaces, format.DecimalSeparator,
                            format.SuppressAngularLeadingZeros, format.SuppressAngularTrailingZeros)
                            + (style.DimAngularUnits == AngleUnitType.Gradians ? format.GradiansSymbol : format.RadiansSymbol);
                    default: throw new ArgumentOutOfRangeException(nameof(style), "Unknown angular tolerance units.");
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
                    // DIMTDEC/DIMALTTD still determine tolerance precision; Windows
                    // controls the decimal separator, not the tolerance precision.
                    format.DecimalSeparator = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                    return LinearUnitFormat.ToDecimal(value, format);
                default: throw new ArgumentOutOfRangeException(nameof(style), "Unknown linear tolerance units.");
            }
        }

        private static void ApplyToleranceOverride(DimensionStyleTolerances tolerance, DimensionStyleOverride item)
        {
            switch (item.Type)
            {
                case DimensionStyleOverrideType.TolerancesDisplayMethod: tolerance.DisplayMethod = (DimensionStyleTolerancesDisplayMethod)item.Value; break;
                case DimensionStyleOverrideType.TolerancesUpperLimit: tolerance.UpperLimit = (double)item.Value; break;
                case DimensionStyleOverrideType.TolerancesLowerLimit: tolerance.LowerLimit = (double)item.Value; break;
                case DimensionStyleOverrideType.TolerancesVerticalPlacement: tolerance.VerticalPlacement = (DimensionStyleTolerancesVerticalPlacement)item.Value; break;
                case DimensionStyleOverrideType.TolerancesPrecision: tolerance.Precision = (short)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressLinearLeadingZeros: tolerance.SuppressLinearLeadingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressLinearTrailingZeros: tolerance.SuppressLinearTrailingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressZeroFeet: tolerance.SuppressZeroFeet = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressZeroInches: tolerance.SuppressZeroInches = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAlternatePrecision: tolerance.AlternatePrecision = (short)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressLinearLeadingZeros: tolerance.AlternateSuppressLinearLeadingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressLinearTrailingZeros: tolerance.AlternateSuppressLinearTrailingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressZeroFeet: tolerance.AlternateSuppressZeroFeet = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressZeroInches: tolerance.AlternateSuppressZeroInches = (bool)item.Value; break;
            }
        }
    }
}
