using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchGradientAciApiTests()
    {
        for (int constructor = 0; constructor < 6; constructor++)
        {
            int c = constructor;
            Run($"hatch/gradient-aci/api-default/{c}", () => HatchGradientAciDefault(c));
        }
        for (int first = 0; first < 3; first++)
            for (int second = 0; second < 3; second++)
            {
                int a = first, b = second;
                Run($"hatch/gradient-aci/api-clone/{a}/{b}", () => HatchGradientAciClone(a, b));
            }
        foreach (bool binary in new[] { false, true })
            for (int presence = 0; presence < 4; presence++)
            {
                bool b = binary; int p = presence;
                Run($"hatch/gradient-aci/api-read/{b}/{p}", () => HatchGradientAciApiRead(b, p));
            }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"hatch/gradient-aci/api-automatic-write/{b}", () => HatchGradientAciAutomaticWrite(b));
        }
        foreach (short index in new short[] { short.MinValue, -1, 0, 1, 255, 256, short.MaxValue })
        {
            short i = index;
            Run($"hatch/gradient-aci/api-int16/{i}", () => HatchGradientAciApiInt16(i));
        }
        Run("hatch/gradient-aci/api-color-mode-independence", HatchGradientAciModeIndependence);
    }

    private static void HatchGradientAciDefault(int constructor)
    {
        HatchGradientPattern pattern = constructor switch
        {
            0 => new(), 1 => new("metadata"),
            2 => new(AciColor.Red, 0.35, HatchGradientPatternType.Linear),
            3 => new(AciColor.Red, 0.35, HatchGradientPatternType.Linear, "metadata"),
            4 => new(AciColor.Red, AciColor.Blue, HatchGradientPatternType.Linear),
            _ => new(AciColor.Red, AciColor.Blue, HatchGradientPatternType.Linear, "metadata")
        };
        Check(pattern.IsColor1AciIndexAutomatic && pattern.IsColor2AciIndexAutomatic, "Constructor changed automatic ACI defaults.");
        Equal((short?)pattern.Color1.Index, pattern.Color1AciIndex, "Automatic first index");
        Equal((short?)pattern.Color2.Index, pattern.Color2AciIndex, "Automatic second index");
        pattern.Color1 = AciColor.Green; pattern.Color2 = AciColor.Yellow;
        Equal((short?)pattern.Color1.Index, pattern.Color1AciIndex, "Automatic index after color replacement");
        pattern.Color2.Index = 42;
        Equal((short?)42, pattern.Color2AciIndex, "Automatic index after mutable color edit");
        Check(pattern.IsColor2AciIndexAutomatic, "Reading automatic index mutated its mode.");
    }

    private static void HatchGradientAciClone(int first, int second)
    {
        var pattern = new HatchGradientPattern(AciColor.FromTrueColor(0x123456), AciColor.FromTrueColor(0xABCDEF), HatchGradientPatternType.Linear);
        if (first != 0) pattern.Color1AciIndex = first == 1 ? (short)17 : null;
        if (second != 0) pattern.Color2AciIndex = second == 1 ? (short)231 : null;
        var copy = (HatchGradientPattern)pattern.Clone();
        Equal(pattern.Color1AciIndex, copy.Color1AciIndex, "Cloned first metadata");
        Equal(pattern.Color2AciIndex, copy.Color2AciIndex, "Cloned second metadata");
        Equal(first == 0, copy.IsColor1AciIndexAutomatic, "Clone froze automatic first index");
        Equal(second == 0, copy.IsColor2AciIndexAutomatic, "Clone froze automatic second index");
        copy.Color1.Index = 41; copy.Color2.Index = 42;
        Equal(first == 0 ? (short?)41 : first == 1 ? (short?)17 : null, copy.Color1AciIndex, "Cloned first mode after mutation");
        Equal(second == 0 ? (short?)42 : second == 1 ? (short?)231 : null, copy.Color2AciIndex, "Cloned second mode after mutation");
        Equal(0x123456, GradientRgb(pattern.Color1), "Clone aliased source first RGB");
        Equal(0xABCDEF, GradientRgb(pattern.Color2), "Clone aliased source second RGB");
        copy.ResetColor1AciIndex(); copy.ResetColor2AciIndex();
        Check(copy.IsColor1AciIndexAutomatic && copy.IsColor2AciIndexAutomatic, "Reset did not restore automatic modes.");
        Equal((short?)41, copy.Color1AciIndex, "Reset first index"); Equal((short?)42, copy.Color2AciIndex, "Reset second index");
        Equal(first == 0, pattern.IsColor1AciIndexAutomatic, "Clone reset changed source mode");
    }

    private static void HatchGradientAciApiRead(bool binary, int presence)
    {
        short? first = (presence & 1) != 0 ? (short)17 : null, second = (presence & 2) != 0 ? (short)231 : null;
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientAciTags(DxfVersion.AutoCad2018, HatchGradientPatternType.Linear,
            true, first, second, binary), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("ACI API fixture rejected.");
        var pattern = (HatchGradientPattern)doc.Entities.Hatches.Single().Pattern;
        Equal(first, pattern.Color1AciIndex, "Loaded first optional value"); Equal(second, pattern.Color2AciIndex, "Loaded second optional value");
        Check(!pattern.IsColor1AciIndexAutomatic && !pattern.IsColor2AciIndexAutomatic, "Import fabricated automatic metadata.");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "ACI save failed.");
        Equal(first, pattern.Color1AciIndex, "Save mutated first metadata"); Equal(second, pattern.Color2AciIndex, "Save mutated second metadata");
        Check(!pattern.IsColor1AciIndexAutomatic && !pattern.IsColor2AciIndexAutomatic, "Save mutated source ACI modes.");
        output.Position = 0;
        var raw = DxfRawDocument.Load(output);
        AssertGradientAciTags(raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "HATCH").Tags, first, second);
        pattern.ResetColor1AciIndex(); pattern.ResetColor2AciIndex();
        Equal((short?)pattern.Color1.Index, pattern.Color1AciIndex, "Reset imported first metadata");
        Equal((short?)pattern.Color2.Index, pattern.Color2AciIndex, "Reset imported second metadata");
    }

    private static void HatchGradientAciAutomaticWrite(bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientAciTags(DxfVersion.AutoCad2018,
            HatchGradientPatternType.Linear, false, null, null, false), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("ACI automatic fixture rejected.");
        var pattern = new HatchGradientPattern(AciColor.FromTrueColor(0x123456), AciColor.FromTrueColor(0xABCDEF), HatchGradientPatternType.Linear);
        doc.Entities.Hatches.Single().Pattern = pattern;
        short first = pattern.Color1.Index, second = pattern.Color2.Index;
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Automatic ACI save failed.");
        output.Position = 0;
        var raw = DxfRawDocument.Load(output);
        AssertGradientAciTags(raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "HATCH").Tags, first, second);
        Check(pattern.IsColor1AciIndexAutomatic && pattern.IsColor2AciIndexAutomatic, "Automatic save froze caller metadata.");
    }

    private static void HatchGradientAciApiInt16(short value)
    {
        var pattern = new HatchGradientPattern { Color1AciIndex = value, Color2AciIndex = value };
        Equal((short?)value, pattern.Color1AciIndex, "Exact first Int16 metadata");
        Equal((short?)value, pattern.Color2AciIndex, "Exact second Int16 metadata");
        var copy = (HatchGradientPattern)pattern.Clone();
        Equal(pattern.Color1AciIndex, copy.Color1AciIndex, "Clone retained Int16 value");
        pattern.Color1AciIndex = null; pattern.Color2AciIndex = null;
        Check(!pattern.Color1AciIndex.HasValue && !pattern.Color2AciIndex.HasValue, "Null must retain explicit absence.");
        Check(!pattern.IsColor1AciIndexAutomatic && !pattern.IsColor2AciIndexAutomatic, "Null selected automatic rather than absent.");
    }

    private static void HatchGradientAciModeIndependence()
    {
        var pattern = new HatchGradientPattern(AciColor.Red, 0.35, HatchGradientPatternType.Linear)
            { Color1AciIndex = 17, Color2AciIndex = null };
        int rgb = GradientRgb(pattern.Color2);
        pattern.Color2AciIndex = 231;
        Check(pattern.SingleColor, "ACI edit incorrectly selected two-color mode.");
        Equal(rgb, GradientRgb(pattern.Color2), "ACI edit recolored RGB stop");
        pattern.Tint = 0.75;
        Equal((short?)231, pattern.Color2AciIndex, "Tint edit overwrote explicit ACI metadata");
        pattern.Color2AciIndex = null; pattern.SingleColor = true;
        Check(!pattern.Color2AciIndex.HasValue, "Single-color derivation fabricated absent ACI metadata.");
        pattern.Color1 = AciColor.Green;
        Equal((short?)17, pattern.Color1AciIndex, "RGB replacement overwrote explicit first metadata");
        pattern.ResetColor2AciIndex(); pattern.Tint = 0.25;
        Equal((short?)pattern.Color2.Index, pattern.Color2AciIndex, "Automatic ACI failed to follow tint-derived RGB");
    }
}
