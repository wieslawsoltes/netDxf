using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterColorWireReviewTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"color-wire-review/{version}/{binary}", () => ColorWireReview(version, binary));
    }

    private static void ColorWireReview(DxfVersion version, bool binary)
    {
        const int rgb = 0x12AB34;
        var color = new AciColor(18, 171, 52);
        var doc = new DxfDocument(version);
        var layer = new Layer("RGB_LAYER") { Color = (AciColor)color.Clone() };
        doc.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX) { Color = (AciColor)color.Clone(), Layer = layer });
        var block = new Block("RGB_BLOCK");
        block.AttributeDefinitions.Add(new AttributeDefinition("RGB_TAG") { Color = (AciColor)color.Clone() });
        var insert = new Insert(block);
        insert.Attributes.Single().Color = (AciColor)color.Clone();
        doc.Entities.Add(insert);
        var vertices = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };
        var face = new PolyfaceMeshFace(new short[] { 1, 2, 3 }) { Color = (AciColor)color.Clone() };
        doc.Entities.Add(new PolyfaceMesh(vertices, new[] { face }) { Color = (AciColor)color.Clone() });
        var mline = new MLineStyle("RGB_MLINE", new[] { new MLineStyleElement(.5, (AciColor)color.Clone(), Linetype.Continuous) })
        { FillColor = (AciColor)color.Clone() };
        doc.MlineStyles.Add(mline);
        if (version >= DxfVersion.AutoCad2004)
        {
            var path = new HatchBoundaryPath(new[] { new Line(Vector2.Zero, Vector2.UnitX),
                new Line(Vector2.UnitX, Vector2.UnitY), new Line(Vector2.UnitY, Vector2.Zero) });
            doc.Entities.Add(new Hatch(new HatchGradientPattern((AciColor)color.Clone(), (AciColor)color.Clone(), HatchGradientPatternType.Linear),
                new[] { path }, false));
        }
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "RGB writer save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"color-wire-review-{version}-{binary}.dxf"), output.ToArray());
        output.Position = 0; var raw = DxfRawDocument.Load(output);
        CheckAuthoredRgbTags(raw, version, rgb);
        output.Position = 0; var reload = DxfDocument.Load(output)!;
        var line = reload.Entities.Lines.Single();
        Equal((short)102, line.Color.Index, "frozen nearest-palette fallback");
        Equal(version >= DxfVersion.AutoCad2004, line.Color.UseTrueColor, "profile-aware authored RGB presence");
        if (line.Color.UseTrueColor) Equal(rgb, AciColor.ToTrueColor(line.Color) & 0xFFFFFF, "RGB roundtrip");
        Check(color.UseTrueColor && color.R == 18 && color.G == 171 && color.B == 52, "writer mutated input color");
        using var again = new MemoryStream(); Check(reload.Save(again, !binary), "RGB opposite-transport save");
        again.Position = 0; CheckAuthoredRgbTags(DxfRawDocument.Load(again), version, rgb);
    }

    private static void CheckAuthoredRgbTags(DxfRawDocument raw, DxfVersion version, int rgb)
    {
        var records = raw.Sections.SelectMany(s => s.Records).ToArray();
        var values = records.SelectMany(r => r.Tags).Where(t => t.Code is 420 or 421).ToArray();
        if (version == DxfVersion.AutoCad2000) Equal(0, values.Length, "R2000 authored output gained RGB extension tags");
        else
        {
            Equal(13, values.Length, "all authored RGB writer paths");
            Check(values.All(t => (int)t.Value == rgb), "standard RGB tags contain a packed color method byte");
            foreach (string kind in new[] { "LAYER", "LINE", "ATTDEF", "ATTRIB", "POLYLINE", "VERTEX", "MLINESTYLE", "HATCH" })
                Check(records.Any(r => r.Name == kind && r.Tags.Any(t => t.Code is 420 or 421)), "untested RGB writer path: " + kind);
        }
    }
}
