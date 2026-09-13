using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchPixelSizeApiTests()
    {
        Run("hatch/pixel-size/model", HatchPixelModel);
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"hatch/pixel-size/edit-clone-fill/{v}/{b}", () => HatchPixelEdit(v, b));
            }
    }

    private static void HatchPixelModel()
    {
        var hatch = new Hatch(HatchPattern.Line, false);
        Equal((double?)0.0, hatch.PixelSize, "New hatch compatibility default");
        hatch.PixelSize = 0.125;
        foreach (double bad in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => hatch.PixelSize = bad);
            Equal((double?)0.125, hatch.PixelSize, "Rejected pixel edit changed state");
        }
        var clone = (Hatch)hatch.Clone();
        Equal(hatch.PixelSize, clone.PixelSize, "Clone lost sampling hint");
        clone.PixelSize = null;
        Equal((double?)0.125, hatch.PixelSize, "Clone edit affected original");
        hatch.PixelSize = null;
        Check(((Hatch)hatch.Clone()).PixelSize == null, "Clone invented an absent field.");
    }

    private static void HatchPixelEdit(DxfVersion version, bool binary)
    {
        foreach (HatchPattern pattern in new HatchPattern[] { HatchPattern.Line, HatchPattern.Solid, new HatchGradientPattern() })
        {
            Hatch original = (Hatch)NewSeedHatch().Clone(); original.Pattern = pattern; original.PixelSize = 0.25;
            var block = new Block("PixelBlock"); block.Entities.Add(original);
            var insert = new Insert(block, new Vector3(10, 20, 0));
            var clonedInsert = (Insert)insert.Clone();
            Equal(original.PixelSize, clonedInsert.Block.Entities.OfType<Hatch>().Single().PixelSize, "Block clone pixel size");
            Hatch hatch = insert.Explode().OfType<Hatch>().Single();
            Equal(original.PixelSize, hatch.PixelSize, "Explode pixel size");
            hatch.TransformBy(Matrix3.Scale(2), new Vector3(3, 4, 0));
            Equal((double?)0.25, hatch.PixelSize, "Transform changed the stored sampling hint");
            foreach (double? value in new double?[] { 1e-20, null, 0.0, 0.375 })
            {
                hatch.PixelSize = value;
                var doc = new DxfDocument(version); doc.Entities.Add(hatch);
                using var output = new MemoryStream(); Check(doc.Save(output, binary), "Edited pixel-size save failed.");
                Equal(value, hatch.PixelSize, "Save modified pixel size");
                output.Position = 0;
                var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Edited pixel-size load failed.");
                hatch = (Hatch)loaded.Entities.Hatches.Single().Clone();
                Equal(value, hatch.PixelSize, "Edited pixel size failed to round trip");
                Equal((double?)0.25, original.PixelSize, "Edited clone changed source");
                Check(output.CanRead, "Pixel edit closed caller stream.");
            }
        }
    }
}
