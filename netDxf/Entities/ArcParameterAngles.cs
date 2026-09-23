// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Entities
{
    // Parameter storage is not a geometric near-equality test. Keep every
    // representable in-range angle, independently of MathHelper.Epsilon.
    internal static class ArcParameterAngles
    {
        internal static double Normalize(double angle)
        {
            double normalized = angle % 360.0;
            if (normalized < 0.0) normalized += 360.0;
            // A negative remainder smaller than half an ULP at 360 rounds up
            // when the turn is added. Keep the canonical half-open interval.
            // As before, both zero signs become +0 and nonfinite inputs yield NaN.
            return normalized == 0.0 || normalized == 360.0 ? 0.0 : normalized;
        }
    }
}
