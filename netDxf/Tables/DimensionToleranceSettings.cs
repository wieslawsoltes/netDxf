// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Tables
{
    // DIMTOL/DIMLIM/DIMTP/DIMTM jointly encode the three public settings.
    internal static class DimensionToleranceSettings
    {
        internal static double Lower(DimensionStyleTolerances settings)
        {
            // Preserve exact bits of equal bounds (notably +0/-0); only an inactive
            // unequal lower property needs projection to the symmetric upper allowance.
            return settings.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Symmetrical
                && settings.UpperLimit != settings.LowerLimit ? settings.UpperLimit : settings.LowerLimit;
        }

        internal static short ToleranceFlag(DimensionStyleTolerances settings)
        {
            switch (settings.DisplayMethod)
            {
                case DimensionStyleTolerancesDisplayMethod.None:
                case DimensionStyleTolerancesDisplayMethod.Limits: return 0;
                case DimensionStyleTolerancesDisplayMethod.Symmetrical:
                case DimensionStyleTolerancesDisplayMethod.Deviation: return 1;
                default: throw new ArgumentOutOfRangeException(nameof(settings), "Unknown tolerance display method.");
            }
        }

        internal static DimensionStyleTolerancesDisplayMethod Decode(short tolerance, short limits, double upper, double lower)
        {
            // Scalar equality is exact. Geometric epsilon must not change a display mode.
            if (tolerance != 0)
                return upper == lower ? DimensionStyleTolerancesDisplayMethod.Symmetrical : DimensionStyleTolerancesDisplayMethod.Deviation;
            return limits != 0 ? DimensionStyleTolerancesDisplayMethod.Limits : DimensionStyleTolerancesDisplayMethod.None;
        }

        internal static void Apply(DimensionStyleTolerances settings, DimensionStyleOverride item)
        {
            switch (item.Type)
            {
                case DimensionStyleOverrideType.TolerancesDisplayMethod: settings.DisplayMethod = (DimensionStyleTolerancesDisplayMethod)item.Value; break;
                case DimensionStyleOverrideType.TolerancesUpperLimit: settings.UpperLimit = (double)item.Value; break;
                case DimensionStyleOverrideType.TolerancesLowerLimit: settings.LowerLimit = (double)item.Value; break;
                case DimensionStyleOverrideType.TolerancesVerticalPlacement: settings.VerticalPlacement = (DimensionStyleTolerancesVerticalPlacement)item.Value; break;
                case DimensionStyleOverrideType.TolerancesPrecision: settings.Precision = (short)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressLinearLeadingZeros: settings.SuppressLinearLeadingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressLinearTrailingZeros: settings.SuppressLinearTrailingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressZeroFeet: settings.SuppressZeroFeet = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesSuppressZeroInches: settings.SuppressZeroInches = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAlternatePrecision: settings.AlternatePrecision = (short)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressLinearLeadingZeros: settings.AlternateSuppressLinearLeadingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressLinearTrailingZeros: settings.AlternateSuppressLinearTrailingZeros = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressZeroFeet: settings.AlternateSuppressZeroFeet = (bool)item.Value; break;
                case DimensionStyleOverrideType.TolerancesAltSuppressZeroInches: settings.AlternateSuppressZeroInches = (bool)item.Value; break;
            }
        }
    }
}
