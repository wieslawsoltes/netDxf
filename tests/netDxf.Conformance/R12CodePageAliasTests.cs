// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12CodePageAliasTests()
    {
        // Independent expected mapping values from the Microsoft WindowsCodePage table.
        // Also exercise the explicit portable fallback for numeric non-OEM aliases.
        foreach (var profile in new[] {
            ("DOS437", 437, 1252, "Grüße ╬"),
            ("DOS850", 850, 1252, "Café ║"),
            ("dos932", 932, 932, "日本語"),
            ("DOS720", 720, 1256, "مرحبا"),
            ("DOS737", 737, 1253, "Ελληνικά"),
            ("DOS775", 775, 1257, "Āžu"),
            ("DOS852", 852, 1250, "Zażółć"),
            ("DOS855", 855, 1252, "Привет"),
            ("DOS857", 857, 1254, "İstanbul"),
            ("DOS858", 858, 1252, "Café €"),
            ("DOS860", 860, 1252, "Português"),
            ("DOS861", 861, 1252, "Ísland"),
            ("DOS862", 862, 1255, "שלום"),
            ("DOS863", 863, 1252, "Québec"),
            ("DOS864", 864, 1256, "ﻻ"),
            ("DOS865", 865, 1252, "Norsk æø"),
            ("DOS866", 866, 1251, "Привет"),
            ("DOS869", 869, 1253, "Ελλάδα"),
            ("DOS874", 874, 874, "ภาษาไทย"),
            ("DOS936", 936, 936, "中文简体"),
            ("DOS949", 949, 949, "한국어"),
            ("DOS950", 950, 950, "繁體中文"),
            ("dos0001250", 1250, 1250, "Zażółć"),
            ("DOS20127", 20127, 1252, "ASCII"),
            ("DOS28591", 28591, 1252, "Café"),
            ("DOS65001", 65001, 1252, "日本語"),
        })
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
