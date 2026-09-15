using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSectionProducerTests()
    {
        foreach (var version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (bool sourceBinary in new[] { false, true }) foreach (bool outputBinary in new[] { false, true })
        {
            var v = version; bool s = sourceBinary, b = outputBinary;
            Run($"section/producer/{v}/{s}/{b}", () => SectionProducer(v, s, b));
        }
    }
    private static void SectionProducer(DxfVersion version, bool sourceBinary, bool binary)
    {
        string year = version.ToString().Replace("AutoCad", "");
        string path = Path.Combine("tests", "fixtures", "section-producer", "extracted", $"extracted-section-R{year}-{(sourceBinary ? "binary" : "ascii")}.dxf");
        var doc = DxfDocument.Load(path) ?? throw new Exception("Independent SECTION packet load"); var section = doc.Entities.Sections.Single();
        Equal("SECTION", section.CodeName, "Independent producer spelling"); Equal("F1000", section.Handle, "Declared extraction handle map");
        Equal(7, section.State, "Producer state"); Equal(17, section.Flags, "Producer flags"); Equal("Producer section", section.Name, "Producer name");
        Equal(new Vector3(0.25, -0.5, 2), section.VerticalDirection, "Producer independent vertical vector"); Equal(17.125, section.TopHeight, "Producer top height"); Equal(-2.75, section.BottomHeight, "Producer bottom height");
        Equal((short)37, section.IndicatorTransparency, "Producer transparency"); Equal((short)5, section.StoredIndicatorColor!.Value, "Producer documented indicator63");
        Check(section.StoredNativeIndicatorColor == null && section.IndicatorColorName == "ProducerColor", "Producer optional indicator fields");
        Check(!section.HasStoredGeometrySettings && section.GeometrySettings == null, "Producer absent360 was materialized");
        Check(section.Vertices.SequenceEqual(new[] { new Vector3(1.125,2.25,3.5),new Vector3(-4.75,5.125,-6.25),new Vector3(7.5,-8.75,9.125) }), "Producer ordered vertices");
        Check(section.BackLineVertices.SequenceEqual(new[] { new Vector3(10.25,-11.5,12.75),new Vector3(-13.125,14.25,-15.5) }), "Producer ordered back-line vertices");
        Equal((int?)0x02000000, section.Transparency.StoredAlphaValue, "Producer exact common440");
        Equal(string.Empty, section.ColorName, "Producer explicit empty common430"); Equal(EntityShadowMode.CastAndReceive, section.ShadowMode!.Value, "Producer explicit common284"); Equal((short)0, (short)section.Lineweight, "Producer common370");
        var bytes = SectionBytes(doc, binary); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-producer-{version}-{sourceBinary}-{binary}.dxf"), bytes);
        var loaded = DxfDocument.Load(new MemoryStream(bytes))!.Entities.Sections.Single(); Check(!loaded.HasStoredGeometrySettings && loaded.StoredNativeIndicatorColor == null, "Producer round-trip inserted absent fields");
        var clone = (Section)loaded.Clone(); Check(!clone.HasStoredGeometrySettings && clone.CodeName == "SECTION", "Producer clone changed stored presence or spelling");
    }
}
