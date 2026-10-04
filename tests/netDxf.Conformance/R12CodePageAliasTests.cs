// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12CodePageAliasTests()
    {
        foreach (var profile in new[] { ("DOS437", 437, 1252, "Grüße ╬"), ("DOS850", 850, 1252, "Café ║"), ("dos932", 932, 932, "日本語") })
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"r12-codepage/modern-alias/{profile.Item1}/{version}/{binary}",
                () => RcpModernAlias(profile.Item1, profile.Item2, profile.Item3, profile.Item4, version, binary));
    }

    private static void RcpModernAlias(string alias, int originalPage, int windowsPage, string text, DxfVersion version, bool binary)
    {
        using var source = new MemoryStream();
        DxfR12Codec.SaveWithCodePage(source, RcpSeeds(text), alias, !binary);
        byte[] before = source.ToArray(); source.Position = 0;
        var raw = DxfRawDocument.Load(source);
        var plan = DxfR12SelectionPlan.Prepare(raw);
        Equal(alias, plan.DrawingCodePage, "DOS source declaration");
        Equal(originalPage, plan.EncodingCodePage, "DOS source encoding");
        Equal("ANSI_" + windowsPage, plan.ModernDrawingCodePage, "Modern Windows encoding counterpart");
        var editable = plan.CreateDocument(version); RcpCheck(editable.Entities.All, text);
        Equal(plan.ModernDrawingCodePage, (string)editable.DrawingVariables.KnownValues().Single(v => v.Name == "$DWGCODEPAGE").Value,
            "Editable modern document declaration");
        using var output = new MemoryStream(); plan.Save(output, version, binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,
            $"r12-dos-modern-{alias}-{version}-{(binary ? "binary" : "text")}.dxf"), output.ToArray());
        output.Position = 0; var outputRaw = DxfRawDocument.Load(output);
        Equal(plan.ModernDrawingCodePage, RcpDeclaration(outputRaw), "Modern DOS projection header");
        Equal(version >= DxfVersion.AutoCad2007 ? 65001 : windowsPage, outputRaw.EncodingCodePage, "Modern actual encoding");
        output.Position = 0; RcpCheck(DxfDocument.Load(output)!.Entities.All, text);
        RcpCheck(editable.Entities.All, text);
        using var restored = new MemoryStream(); plan.Save(restored, DxfVersion.AutoCad12, !binary); restored.Position = 0;
        var restoredRaw = DxfRawDocument.Load(restored);
        Equal(alias, RcpDeclaration(restoredRaw), "Modern output changed retained DOS spelling");
        Equal(originalPage, restoredRaw.EncodingCodePage, "Modern output changed retained DOS encoding");
        RcpCheck(DxfR12Codec.ReadEntities(restoredRaw), text);
        Check(before.SequenceEqual(R12Bytes(raw, !binary)), "DOS conversion changed caller bytes");
    }
}
