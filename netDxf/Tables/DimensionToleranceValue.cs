// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Tables
{
    internal static class DimensionToleranceValue
    {
        // Symmetrical uses UpperLimit. Preserve the existing bit representation
        // when both values are numerically equal, including either sign of zero.
        internal static double Lower(DimensionStyleTolerancesDisplayMethod method, double upper, double lower)
        {
            return method == DimensionStyleTolerancesDisplayMethod.Symmetrical && upper != lower ? upper : lower;
        }

        internal static double Lower(DimensionStyleTolerances tolerance)
        {
            return Lower(tolerance.DisplayMethod, tolerance.UpperLimit, tolerance.LowerLimit);
        }
    }
}
