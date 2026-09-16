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
    private static void RegisterTrueColorWireReviewTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"true-color-review/wire/{version}/{binary}", () => TrueColorWireReview(version, binary));
    }

    private static void TrueColorWireReview(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var color = new AciColor(18,171,52);
        var layer = doc.Layers.Add(new Layer("REVIEW_RGB") { Color = (AciColor)color.Clone() });
        doc.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX) { Color = (AciColor)color.Clone(), Layer = layer });
        var block = new Block("REVIEW_RGB_BLOCK");
        block.AttributeDefinitions.Add(new AttributeDefinition("RGB") { Color = (AciColor)color.Clone() });
        var insert = new Insert(block) { Color = (AciColor)color.Clone() };
        insert.Attributes.Single().Color = (AciColor)color.Clone(); doc.Entities.Add(insert);
        var face = new PolyfaceMeshFace(new short[] { 1,2,3 }) { Color = (AciColor)color.Clone() };
        var mesh = new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { face }) { Color = (AciColor)color.Clone() };
        doc.Entities.Add(mesh);
        var style = new MLineStyle("REVIEW_RGB_MLINE", new[] {
            new MLineStyleElement(.5, (AciColor)color.Clone(), Linetype.Continuous),
            new MLineStyleElement(-.5, (AciColor)color.Clone(), Linetype.Continuous)
        }) { FillColor = (AciColor)color.Clone() };
        doc.Entities.Add(new MLine(new[] { Vector2.Zero, Vector2.UnitX }, style, 1) { Color = (AciColor)color.Clone() });
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "color scope save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"true-color-review-{version}-{binary}.dxf"), output.ToArray());
        output.Position = 0; var raw = DxfRawDocument.Load(output);
        var colors = raw.Tags.Where(t => t.Code == 420).ToArray();
        if (version == DxfVersion.AutoCad2000) Equal(0, colors.Length, "R2000 emitted RGB fields");
        else
        {
            Check(colors.Length >= 10, "RGB test did not exercise all writer scopes");
            Check(colors.All(t => (int)t.Value == 0x12AB34), "group-420 must contain untagged RGB");
            foreach (string kind in new[] { "LAYER", "LINE", "ATTDEF", "ATTRIB", "INSERT", "POLYLINE", "VERTEX", "MLINE", "MLINESTYLE" })
                Check(raw.Sections.SelectMany(s => s.Records).Any(r => r.Name == kind && r.Tags.Any(t => t.Code == 420)), "missing RGB scope " + kind);
        }
        Check(layer.Color.UseTrueColor && insert.Color.UseTrueColor && face.Color.UseTrueColor && style.FillColor.UseTrueColor, "version fallback mutated source colors");
        Equal(unchecked((int)0xC212AB34), AciColor.ToTrueColor(color), "packed public color API changed");
        output.Position = 0; var loaded = DxfDocument.Load(output)!;
        var loadedFace = loaded.Entities.PolyfaceMeshes.Single().Faces.First();
        loadedFace.Color = new AciColor(52,18,171);
        using var edited = new MemoryStream(); Check(loaded.Save(edited, !binary), "stored face color edit"); edited.Position = 0;
        raw = DxfRawDocument.Load(edited);
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "VERTEX" && r.Tags.Any(t => t.Code == 71));
        var updated = record.Tags.Where(t => t.Code == 420).ToArray();
        if (version == DxfVersion.AutoCad2000) Equal(0, updated.Length, "edited stored face emitted pre-R2004 RGB");
        else { Equal(1, updated.Length, "edited face RGB count"); Equal(0x3412AB, (int)updated[0].Value, "edited stored face RGB packing"); }
    }
}
