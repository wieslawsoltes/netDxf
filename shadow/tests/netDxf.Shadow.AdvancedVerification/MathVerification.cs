using System.Globalization;
using netDxf.Shadow;

internal static class MathVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("math/vector formatting terminates for zero and unit vectors", () =>
        {
            Require(Vec3.Zero.ToString() == "Vec3 { X = 0, Y = 0, Z = 0 }");
            Require(Vec3.UnitZ.ToString() == "Vec3 { X = 0, Y = 0, Z = 1 }");
            Require($"{Vec3.UnitX.Normalized}" == "Vec3 { X = 1, Y = 0, Z = 0 }");
        }));
        tests.Add(("math/vector diagnostics are invariant and retain round-trip coordinates", () =>
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                var vector = new Vec3(1.125, -0.25, double.Epsilon);
                Require(vector.ToString() == "Vec3 { X = 1.125, Y = -0.25, Z = 5E-324 }");
                Require(vector == new Vec3(1.125, -0.25, double.Epsilon));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }));
        tests.Add(("math/nonfinite diagnostic values do not normalize or throw", () =>
        {
            var text = new Vec3(double.NaN, double.PositiveInfinity, double.NegativeInfinity).ToString();
            Require(text == "Vec3 { X = NaN, Y = Infinity, Z = -Infinity }");
        }));
        tests.Add(("math/containing records format without recursively derived vectors", () =>
        {
            Require(Affine3.Identity.ToString().Contains("Vec3 { X = 1, Y = 0, Z = 0 }", StringComparison.Ordinal));
            Require(new Bounds2(0, 0, 10, 20).ToString().Contains("Center = Vec3 { X = 5, Y = 10, Z = 0 }", StringComparison.Ordinal));
            Require(Bounds2.Empty.ToString().Length < 1000);
        }));
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Unexpected numeric diagnostic formatting.");
    }
}
