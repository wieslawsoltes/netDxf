// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    // Null means inherited, while an empty string explicitly clears that half.
    private static readonly string?[][] AffixCases = {
        new string?[] { null, null, null, null },
        new string?[] { "O:", null, null, null },
        new string?[] { null, ":TAIL", null, null },
        new string?[] { "", null, null, null },
        new string?[] { null, "", null, null },
        new string?[] { "", "", null, null },
        new string?[] { null, null, "Q:", null },
        new string?[] { null, null, null, ":TAIL" },
        new string?[] { null, null, "", null },
        new string?[] { null, null, null, "" },
        new string?[] { null, null, "", "" },
        new string?[] { " \t", "\u00b5\u03a9", " \t", "\u00b5\u03a9" },
        new string?[] { "P:", "::<>tail", "Q:", "::[]tail" },
        new string?[] { "O:", ":TAIL", "Q:", ":ALT2" },
        new string?[] { "", ":TAIL", "Q:", "" }
    };
    private static readonly string[] AffixBase = { "S:", ":END", "A:", ":ALT" };
    private static readonly DimensionStyleOverrideType[] AffixTypes = {
        DimensionStyleOverrideType.DimPrefix, DimensionStyleOverrideType.DimSuffix,
        DimensionStyleOverrideType.AltUnitsPrefix, DimensionStyleOverrideType.AltUnitsSuffix
    };

    private static void RegisterDimensionAffixFidelityTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            for (int kind = 0; kind < 9; kind++) for (int placement = 0; placement < 4; placement++)
            {
                if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
                int k = kind, p = placement;
                Run($"dimension-affix/wire/{version}/{binary}/{p}/{k}", () =>
                {
                    var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                    var entities = Enumerable.Range(0, AffixCases.Length).Select(v => AffixEntity(k, v)).ToArray();
                    if (p == 0) foreach (var entity in entities) doc.Entities.Add(entity);
                    else if (p == 1)
                    {
                        doc.Layouts.Add(new Layout("AFFIX_PAPER"));
                        foreach (var entity in entities) doc.Layouts["AFFIX_PAPER"].AssociatedBlock.Entities.Add(entity);
                    }
                    else
                    {
                        var holder = new Block("AFFIX_HOLDER", entities);
                        if (p == 2) doc.Entities.Add(new Insert(holder));
                        else doc.Blocks.Add(holder);
                    }
                    doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                    string[] handles = entities.Select(e => e.Handle).ToArray();
                    for (int v = 0; v < entities.Length; v++) CheckAffixEntity(entities[v], k, v, false);
                    string stem = $"dimension-affix-{version}-{binary}-{p}-{k}";
                    using var source = new MemoryStream(); Check(doc.Save(source, binary), "Affix source save");
                    // Serialization must not materialize extra entries in the authored dictionary.
                    for (int v = 0; v < entities.Length; v++) CheckAffixEntity(entities[v], k, v, false);
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                    CheckAffixPacket(LoadRaw(source.ToArray()), version);
                    source.Position = 0; doc = DxfDocument.Load(source) ?? throw new InvalidOperationException("Affix load");
                    CheckAffixDocument(doc, k, handles);
                    foreach (bool output in new[] { false, true })
                    {
                        using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Affix resave");
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                        CheckAffixPacket(LoadRaw(stream.ToArray()), version);
                        stream.Position = 0;
                        var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Affix reload");
                        CheckAffixDocument(second, k, handles);
                    }
                    Check(source.CanRead, "Affix load closed caller stream");
                });
            }
            // Exercise each primary/alternate base component independently, including no prefix.
            for (int mask = 0; mask < 16; mask++)
            {
                int m = mask;
                Run($"dimension-affix/style-header/{version}/{binary}/{m}", () =>
                {
                    var doc = new DxfDocument(version); doc.Comments.Clear();
                    var style = new DimensionStyle("AFFIX_STYLE_HEADER") {
                        DimPrefix = (m & 1) != 0 ? "P:" : "", DimSuffix = (m & 2) != 0 ? ":S" : ""
                    };
                    style.AlternateUnits.Prefix = (m & 4) != 0 ? "A:" : "";
                    style.AlternateUnits.Suffix = (m & 8) != 0 ? ":B" : "";
                    doc.DimensionStyles.Add(style); doc.DrawingVariables.DimStyle = style.Name;
                    using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Affix header save");
                    byte[] data = stream.ToArray();
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"dimension-affix-style-{version}-{binary}-{m}.dxf"), data);
                    var raw = LoadRaw(data); var table = raw.Sections.SelectMany(s => s.Records)
                        .Single(r => r.Name == "DIMSTYLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, style.Name)));
                    string post = style.DimPrefix + (style.DimPrefix.Length == 0 ? "" : "<>") + style.DimSuffix;
                    string apost = style.AlternateUnits.Prefix + (style.AlternateUnits.Prefix.Length == 0 ? "" : "[]") + style.AlternateUnits.Suffix;
                    Equal(post, (string)table.Tags.Single(t => t.Code == 3).Value, "DIMPOST table");
                    Equal(apost, (string)table.Tags.Single(t => t.Code == 4).Value, "DIMAPOST table");
                    foreach (var item in new[] { ("$DIMPOST", post), ("$DIMAPOST", apost) })
                    {
                        int at = Enumerable.Range(0, raw.Tags.Count).Single(i => raw.Tags[i].Code == 9 && Equals(raw.Tags[i].Value, item.Item1));
                        Equal(item.Item2, (string)raw.Tags[at + 1].Value, "Active header " + item.Item1);
                    }
                    stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Affix header load");
                    var copy = loaded.DimensionStyles[style.Name];
                    Check(new[] { style.DimPrefix, style.DimSuffix, style.AlternateUnits.Prefix, style.AlternateUnits.Suffix }
                        .SequenceEqual(new[] { copy.DimPrefix, copy.DimSuffix, copy.AlternateUnits.Prefix, copy.AlternateUnits.Suffix }), "Style components changed");
                });
            }
            foreach (bool alternate in new[] { false, true }) foreach (int kind in new[] { 1, 8 })
            foreach (string text in new[] { "", "<>", "[]", "literal", "<>tail", "[]tail", "P<>S", "A[]B" })
            {
                bool alt = alternate; int k = kind; string value = text;
                Run($"dimension-affix/raw-pair/{version}/{binary}/{alt}/{k}/{value}", () =>
                {
                    var doc = new DxfDocument(version) { BuildDimensionBlocks = true };
                    var entity = AffixEntity(k, alt ? 10 : 5); doc.Entities.Add(entity);
                    using var output = new MemoryStream(); Check(doc.Save(output, binary), "Raw pair seed");
                    var raw = LoadRaw(output.ToArray()); var r = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == (k == 8 ? "LEADER" : "DIMENSION"));
                    var tags = r.Tags.ToArray(); int at = Enumerable.Range(0, tags.Length - 1)
                        .Single(i => tags[i].Code == 1070 && Equals(tags[i].Value, (short)(alt ? 4 : 3)));
                    tags[at + 1] = new DxfTag(1000, value); raw = raw.WithRecord(r, tags);
                    using var input = new MemoryStream(SaveRaw(raw, binary));
                    var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Raw pair load");
                    var e = loaded.Blocks.SelectMany(b => b.Entities).Single(e => e is Dimension || e is Leader);
                    var pair = value.IndexOf(alt ? "[]" : "<>", StringComparison.Ordinal);
                    string prefix = pair < 0 ? "" : value[..pair], suffix = pair < 0 ? value : value[(pair + 2)..];
                    var dictionary = ContainerOverrides(e);
                    Equal(prefix, (string)dictionary[AffixTypes[alt ? 2 : 0]].Value, "Raw explicit prefix");
                    Equal(suffix, (string)dictionary[AffixTypes[alt ? 3 : 1]].Value, "Raw explicit suffix");
                    Equal(3, dictionary.Count, "Two affix halves and unrelated scalar");
                    Check(input.CanRead, "Raw pair input ownership");
                });
            }
        }
        Run("dimension-affix/materialization-and-inheritance", () =>
        {
            var doc = new DxfDocument { BuildDimensionBlocks = true };
            var entity = AffixEntity(1, 1); doc.Entities.Add(entity);
            var style = ((Dimension)entity).Style;
            style.DimSuffix = ":LATEST";
            using var stream = new MemoryStream(); Check(doc.Save(stream), "Current base save");
            Equal(2, ContainerOverrides(entity).Count, "Writer must not change sparse dictionary");
            stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Materialization load");
            var dim = loaded.Entities.Dimensions.Single();
            Equal(":LATEST", (string)dim.StyleOverrides[DimensionStyleOverrideType.DimSuffix].Value, "Inherited half at save");
            dim.Style.DimSuffix = ":LATER";
            dim.Update(); Equal("O:10.0000:LATEST", dim.Block.Entities.OfType<MText>().Single().Value, "Complete stored pair must not inherit later changes");
            dim.StyleOverrides.Remove(DimensionStyleOverrideType.DimSuffix); dim.Update();
            Equal("O:10.0000:LATER", dim.Block.Entities.OfType<MText>().Single().Value, "Removing a half restores live inheritance");
        });
    }

    private static EntityObject AffixEntity(int kind, int variant)
    {
        EntityObject entity = kind == 8 ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3), new Vector2(9, 3) }, new DimensionStyle("TEXT_BLOCK_STYLE")) : TextBlockDimension(kind);
        var style = entity is Dimension d ? d.Style : ((Leader)entity).Style;
        style.DimPrefix = AffixBase[0]; style.DimSuffix = AffixBase[1];
        style.AlternateUnits.Prefix = AffixBase[2]; style.AlternateUnits.Suffix = AffixBase[3];
        entity.Layer = new Layer("AFFIX_" + variant.ToString("D2"));
        if (entity is Dimension dim) { dim.UserText = "<>"; dim.TextReferencePoint = new Vector2(17.25, 0); }
        var data = new XData(new ApplicationRegistry("AFFIX_KEEP")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); entity.XData.Add(data);
        var overrides = ContainerOverrides(entity); overrides.Add(DimensionStyleOverrideType.TextHeight, 0.75);
        for (int i = 0; i < 4; i++) if (AffixCases[variant][i] is string value) overrides.Add(AffixTypes[i], value);
        return entity;
    }

    private static void CheckAffixEntity(EntityObject entity, int kind, int variant, bool loaded)
    {
        var style = entity is Dimension d ? d.Style : ((Leader)entity).Style;
        var actual = new[] { style.DimPrefix, style.DimSuffix, style.AlternateUnits.Prefix, style.AlternateUnits.Suffix };
        Check(AffixBase.SequenceEqual(actual), "Base style mutated");
        var dictionary = ContainerOverrides(entity); int count = 1;
        for (int i = 0; i < 4; i++)
        {
            int pair = i / 2 * 2;
            bool exists = loaded ? AffixCases[variant][pair] != null || AffixCases[variant][pair + 1] != null : AffixCases[variant][i] != null;
            Equal(exists, dictionary.ContainsType(AffixTypes[i]), "Affix presence " + AffixTypes[i]);
            if (exists) { count++; Equal(AffixCases[variant][i] ?? AffixBase[i], (string)dictionary[AffixTypes[i]].Value, "Affix value " + AffixTypes[i]); }
        }
        Equal(count, dictionary.Count, "Exact override inventory");
        SameDoubleBits(0.75, (double)dictionary[DimensionStyleOverrideType.TextHeight].Value, "Other override lost");
        Equal("unchanged", (string)entity.XData["AFFIX_KEEP"].XDataRecord.Single().Value, "Other application XData lost");
        if (entity is Dimension dim)
        {
            string[] measured = { "8.6603", "10.0000", "90°", "10.0000", "5.0000", "90°", "2.0000", "7.8540" };
            string prefix = AffixCases[variant][0] ?? AffixBase[0], suffix = AffixCases[variant][1] ?? AffixBase[1];
            if (prefix.Length == 0 && kind == 3) prefix = "Ø";
            if (prefix.Length == 0 && kind == 4) prefix = "R";
            Equal(prefix + measured[kind] + suffix, dim.Block.Entities.OfType<MText>().Single().Value, "Affix generated label");
        }
    }

    private static void CheckAffixDocument(DxfDocument doc, int kind, string[] handles)
    {
        var entities = doc.Blocks.SelectMany(b => b.Entities).Where(e => e is Dimension || e is Leader).OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(AffixCases.Length, entities.Length, "Affix entity count");
        for (int v = 0; v < entities.Length; v++)
        {
            var entity = entities[v]; Equal(handles[v], entity.Handle, "Affix handle"); CheckAffixEntity(entity, kind, v, true);
            if (entity is Dimension dim) { dim.Update(); dim.Update(); CheckAffixEntity(entity, kind, v, true); }
            var clone = (EntityObject)entity.Clone(); var overrides = ContainerOverrides(clone);
            if (clone is Dimension copy) copy.Block = DimensionBlock.Build(copy);
            CheckAffixEntity(clone, kind, v, true);
            overrides.Remove(DimensionStyleOverrideType.DimPrefix);
            overrides.Add(DimensionStyleOverrideType.DimPrefix, "CLONE");
            CheckAffixEntity(entity, kind, v, true);
        }
        Equal(0, doc.Objects.Validate().Count, "Affix graph validation");
        var line = doc.Entities.Lines.Single(); RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }

    private static void CheckAffixPacket(DxfRawDocument raw, DxfVersion version)
    {
        var entities = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name is "DIMENSION" or "ARC_DIMENSION" or "LEADER")
            .OrderBy(r => (string)r.Tags.Single(t => t.Code == 8).Value, StringComparer.Ordinal).ToArray();
        Equal(AffixCases.Length, entities.Length, "Physical affix inventory");
        for (int v = 0; v < entities.Length; v++) for (int pair = 0; pair < 2; pair++)
        {
            var tags = entities[v].Tags;
            var positions = Enumerable.Range(0, tags.Count - 1).Where(i => tags[i].Code == 1070 && Equals(tags[i].Value, (short)(3 + pair))).ToArray();
            bool exists = AffixCases[v][pair * 2] != null || AffixCases[v][pair * 2 + 1] != null;
            Equal(exists ? 1 : 0, positions.Length, "Physical pair presence");
            if (exists)
            {
                var tag = tags[positions[0] + 1]; Equal((short)1000, tag.Code, "Affix XData string type");
                string expected = (AffixCases[v][pair * 2] ?? AffixBase[pair * 2]) + (pair == 0 ? "<>" : "[]") + (AffixCases[v][pair * 2 + 1] ?? AffixBase[pair * 2 + 1]);
                if (version < DxfVersion.AutoCad2007) expected = expected.Replace("µ", "\\U+00B5").Replace("Ω", "\\U+03A9");
                Equal(expected, (string)tag.Value, "Complete physical affix pair");
            }
        }
    }
}
