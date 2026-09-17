// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Vector3 LegacyCachedDirection(int sample, int operation)
    {
        double scale = new[] { 1e-161, 7e-162, 1e-160, 2e-160, 9e-162 }[sample];
        var value = new Vector3(scale, -2 * scale, 3 * scale);
        double epsilon = MathHelper.Epsilon;
        try
        {
            // The unchanged public utility can cache an inaccurate result when
            // its unscaled sum of squares rounds in the subnormal range.
            MathHelper.Epsilon = double.Epsilon;
            if (operation == 0) value = Vector3.Normalize(value);
            else
            {
                value.Normalize();
                if (operation == 2) value = -value;
            }
        }
        finally { MathHelper.Epsilon = epsilon; }
        Check(value.IsNormalized, "Regression input must carry the public utility's cache flag");
        Check(double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z), "Regression input must be finite");
        Check(Math.Abs(Vector3.DotProduct(value, value) - 1) > 2e-15, "Regression input must expose an inaccurate cached length");
        return value;
    }

    private static void RegisterDirectionCachedNormalizationTests()
    {
        foreach (string kind in DirectionSlotKinds)
            for (int sample = 0; sample < 5; sample++)
                for (int operation = 0; operation < 3; operation++)
                {
                    int s = sample, op = operation;
                    Run($"direction-cache/{kind}/{s}/{op}", () => DirectionAccept(kind, LegacyCachedDirection(s, op)));
                }
        foreach (bool ray in new[] { false, true })
            for (int sample = 0; sample < 5; sample++)
                for (int operation = 0; operation < 3; operation++)
                {
                    int s = sample, op = operation;
                    Run($"direction-cache/constructor/{ray}/{s}/{op}", () =>
                        DirectionConstructor(ray, false, LegacyCachedDirection(s, op), true));
                }
    }
}
