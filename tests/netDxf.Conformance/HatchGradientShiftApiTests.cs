using netDxf;
using netDxf.Blocks;
using netDxf.Entities;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchGradientShiftApiTests()
    {
        Run("hatch/gradient-shift/api/defaults", () =>
        {
            foreach (var pattern in new[] { new HatchGradientPattern(), new HatchGradientPattern("description"),
                new HatchGradientPattern(AciColor.Red, 0.4, HatchGradientPatternType.Curved),
                new HatchGradientPattern(AciColor.Red, AciColor.Blue, HatchGradientPatternType.Linear) })
            {
                Equal(0.0, pattern.Shift, "Default shift");
                Check(pattern.Centered, "Default compatibility flag changed.");
            }
        });
        foreach (double shift in new[] { 0.0, double.Epsilon, 0.125, 0.5, 0.875, Math.BitDecrement(1.0), 1.0 })
        {
            double s = shift;
            Run($"hatch/gradient-shift/api/bridge-clone/{s:R}", () =>
            {
                var source = new HatchGradientPattern { Shift = s };
                Equal(s == 0.0, source.Centered, "Exact endpoint projection");
                var copy = (HatchGradientPattern)source.Clone();
                Equal(BitConverter.DoubleToInt64Bits(s), BitConverter.DoubleToInt64Bits(copy.Shift), "Pattern clone preserves numeric blend");
                copy.Centered = true; Equal(0.0, copy.Shift, "True assigns historical zero endpoint");
                copy.Centered = false; Equal(1.0, copy.Shift, "False assigns historical one endpoint");
                Equal(s, source.Shift, "Clone's edits changed source blend");
                var hatch = new Hatch(source, new List<HatchBoundaryPath> { new HatchBoundaryPath(new EntityObject[] { new Circle(Vector3.Zero, 3) }) }, false);
                var block = new Block("ApiShift"); block.Entities.Add(hatch);
                var clone = (Insert)new Insert(block).Clone();
                var nested = clone.Block.Entities.OfType<Hatch>().Single();
                Equal(s, ((HatchGradientPattern)nested.Pattern).Shift, "Nested clone blend");
                nested.TransformBy(Matrix3.RotationZ(Math.PI / 3), new Vector3(4, -3, 2));
                Equal(s, ((HatchGradientPattern)nested.Pattern).Shift, "Rotation/translation retains blend");
            });
        }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -double.Epsilon, -1.0, Math.BitIncrement(1.0), 2.0 })
        {
            double value = invalid;
            Run($"hatch/gradient-shift/api/reject/{value:R}", () =>
            {
                var pattern = new HatchGradientPattern { Shift = 0.375 };
                Throws<ArgumentOutOfRangeException>(() => pattern.Shift = value);
                Equal(0.375, pattern.Shift, "Failed setter changed existing value");
                Check(!pattern.Centered, "Failed setter changed compatibility projection.");
            });
        }
    }
}
