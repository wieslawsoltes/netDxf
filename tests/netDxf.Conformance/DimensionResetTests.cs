// Copyright (c) netDxf contributors. Licensed under the MIT License.
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
    private static readonly DimensionStyleOverrideType[] ResetKinds = {
        DimensionStyleOverrideType.DimArrow1, DimensionStyleOverrideType.DimArrow2,
        DimensionStyleOverrideType.LeaderArrow, DimensionStyleOverrideType.TextFillColor
    };
    private static bool ResetSelected(int row, int field) => row == field + 1 || row >= 5;
    private static DimensionStyle ResetStyle() => new("RESET_BASE") {
        DimArrow1 = new Block("RESET_BASE_ARROW", new EntityObject[] { new Line(Vector2.Zero, Vector2.UnitX) }),
        TextFillColor = new AciColor(2)
    };
    private static EntityObject ResetHost(int kind, int row, DimensionStyle style)
    {
        EntityObject host = kind == 8 ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3) }, style) : TextBlockDimension(kind);
        if (host is Dimension dim) { dim.Style = style; dim.UserText = "FIXED"; }
        host.Layer = new Layer("RESET_ROW_" + row.ToString("D2"));
        var entries = ContainerOverrides(host); entries.Add(DimensionStyleOverrideType.TextHeight, 0.75);
        var other = new Block("RESET_OTHER_ARROW", new EntityObject[] { new Line(Vector2.Zero, Vector2.UnitY) });
        for (int field = 0; field < 4; field++) if (ResetSelected(row, field))
            entries.Add(ResetKinds[field], row == 6 ? field == 3 ? (object)new AciColor(3) : other : null);
        var acad = new XData(new ApplicationRegistry("ACAD"));
        acad.XDataRecord.AddRange(new[] { DxString("NEIGHBOR"), DxString("DSTYLE"), DxOpen, DxId(43), DxReal(11), DxClose, DxString("TAIL") });
        host.XData.Add(acad);
        var keep = new XData(new ApplicationRegistry("RESET_KEEP")); keep.XDataRecord.Add(DxString("unchanged")); host.XData.Add(keep);
        return host;
    }
    private static DxfDocument ResetDocument(DxfVersion version, int kind, int placement)
    {
        var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
        var style = ResetStyle(); style.DimArrow2 = style.DimArrow1; style.LeaderArrow = style.DimArrow1;
        var hosts = Enumerable.Range(0, 7).Select(row => ResetHost(kind, row, style)).ToArray();
        if (placement == 0) foreach (var host in hosts) doc.Entities.Add(host);
        else if (placement == 1) {
            doc.Layouts.Add(new Layout("RESET_PAPER"));
            foreach (var host in hosts) doc.Layouts["RESET_PAPER"].AssociatedBlock.Entities.Add(host);
        } else {
            var block = new Block("RESET_HOLDER", hosts);
            if (placement == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block);
        }
        doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
        return doc;
    }
    private static EntityObject[] ResetHosts(DxfDocument doc) => doc.Blocks.SelectMany(b => b.Entities)
        .Where(e => e.Layer.Name.StartsWith("RESET_ROW_", StringComparison.Ordinal)).OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray();
    private static void CheckResetHost(EntityObject host, int row)
    {
        var entries = ContainerOverrides(host); var style = CompositeStyle(host);
        Equal("RESET_BASE_ARROW", style.DimArrow1.Name, "Base arrow unchanged");
        Equal((short)2, style.TextFillColor.Index, "Base fill unchanged");
        Equal(1 + Enumerable.Range(0, 4).Count(f => ResetSelected(row, f)), entries.Count, "Exact override presence");
        for (int field = 0; field < 4; field++) {
            Equal(ResetSelected(row, field), entries.ContainsType(ResetKinds[field]), "Absent is not a reset");
            if (!ResetSelected(row, field)) continue;
            object value = entries[ResetKinds[field]].Value;
            if (row != 6) Equal(null, value, "Explicit null retained");
            else if (field == 3) Equal((short)3, ((AciColor)value).Index, "Explicit fill retained");
            else Equal("RESET_OTHER_ARROW", ((Block)value).Name, "Custom arrow retained");
        }
        SameDoubleBits(0.75, (double)entries[DimensionStyleOverrideType.TextHeight].Value, "Unrelated scalar");
        Equal("unchanged", (string)host.XData["RESET_KEEP"].XDataRecord.Single().Value, "Other application");
    }
    private static void CheckResetDocument(DxfDocument doc, string[] handles, int kind)
    {
        var hosts = ResetHosts(doc); Equal(7, hosts.Length, "Reset host inventory");
        for (int row = 0; row < hosts.Length; row++) {
            var host = hosts[row]; CheckResetHost(host, row); Equal(handles[row], host.Handle, "Host identity");
            Equal(kind == 8 ? typeof(Leader) : TextBlockDimension(kind).GetType(), host.GetType(), "Dimension family");
            var clone = (EntityObject)host.Clone(); CheckResetHost(clone, row);
            ContainerOverrides(clone).Clear(); CheckResetHost(host, row);
            if (host is Dimension dim) {
                dim.Update(); Equal("FIXED", dim.Block.Entities.OfType<MText>().Single().Value, "Fixed label");
                if (kind == 1) Equal(row == 5 ? 0 : row is 1 or 2 ? 1 : 2, dim.Block.Entities.OfType<Insert>().Count(), "Regenerated custom arrow count");
                CheckResetHost(host, row);
            }
        }
        Equal(0, doc.Objects.Validate().Count, "Reset graph validation");
        var line = doc.Entities.Lines.Single(); RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
    private static void RegisterDimensionResetTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int kind = 0; kind < 9; kind++) for (int placement = 0; placement < 4; placement++) {
            if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
            int k = kind, p = placement;
            Run($"dimension-reset/wire/{version}/{binary}/{k}/{p}", () => {
                var doc = ResetDocument(version, k, p); var hosts = ResetHosts(doc);
                var unchanged = hosts.Select(DxSnapshot).ToArray(); string[] handles = hosts.Select(e => e.Handle).ToArray();
                string stem = $"dimension-reset-{version}-{binary}-{k}-{p}";
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Reset source save");
                foreach (var check in unchanged) check();
                var storedStyle = LoadRaw(source.ToArray()).Sections.SelectMany(section => section.Records)
                    .Single(record => record.Name == "DIMSTYLE" && record.Tags.Any(t => t.Code == 2 && Equals(t.Value, "RESET_BASE")));
                Check(storedStyle.Tags.Where(t => t.Code == 70).Select(t => (short)t.Value).SequenceEqual(new short[] { 0, 2 }),
                    "Table flags and fill color require distinct ordered group70 records");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Reset load");
                CheckResetDocument(loaded, handles, k);
                foreach (bool output in new[] { false, true }) {
                    var snapshot = ResetHosts(loaded).Select(DxSnapshot).ToArray();
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Reset resave");
                    foreach (var check in snapshot) check();
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0; var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Reset reload");
                    CheckResetDocument(second, handles, k);
                    foreach (var host in ResetHosts(second)) ContainerOverrides(host).Clear();
                    using var cleared = new MemoryStream(); Check(second.Save(cleared, output), "Cleared reset save"); cleared.Position = 0;
                    var third = DxfDocument.Load(cleared) ?? throw new InvalidOperationException("Cleared reset load");
                    Check(ResetHosts(third).All(e => ContainerOverrides(e).Count == 0), "Cleared overrides reappeared");
                }
                Check(source.CanRead, "Load closed source");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int scenario = 0; scenario < 6; scenario++) {
            int s = scenario;
            Run($"dimension-reset/raw/{version}/{binary}/{s}", () => {
                var doc = ResetDocument(version, 1, 0);
                using var seed = new MemoryStream();
                // The seed uses only nonnull overrides; it does not depend on the new reset writer.
                foreach (var host in ResetHosts(doc)) {
                    ContainerOverrides(host).Clear();
                    ContainerOverrides(host).Add(DimensionStyleOverrideType.DimArrow1, doc.Blocks["RESET_BASE_ARROW"]);
                    ContainerOverrides(host).Add(DimensionStyleOverrideType.TextFillColor, new AciColor(3));
                }
                Check(doc.Save(seed, binary), "Raw seed save"); var raw = LoadRaw(seed.ToArray());
                var record = raw.Sections.SelectMany(section => section.Records).Single(r => r.Name == "DIMENSION" && r.Tags.Any(t => t.Code == 8 && Equals(t.Value, "RESET_ROW_00")));
                var tags = record.Tags.ToArray();
                int arrow = Enumerable.Range(0, tags.Length - 1).Single(i => tags[i].Code == 1070 && Equals(tags[i].Value, (short)343));
                if (s < 5) {
                    tags[arrow] = new DxfTag(1070, (short)(s == 0 ? 343 : s == 1 ? 344 : 342));
                    if (s != 4) tags[arrow+1] = new DxfTag(1005, s == 3 ? "00000000" : "0");
                    if (s >= 2) {
                        int flag = Enumerable.Range(0, tags.Length - 1).Single(i => tags[i].Code == 1070 && Equals(tags[i].Value, (short)173));
                        tags[flag+1] = new DxfTag(1070, (short)0);
                    }
                } else {
                    int fill = Enumerable.Range(0, tags.Length - 1).Single(i => tags[i].Code == 1070 && Equals(tags[i].Value, (short)69));
                    tags[fill+1] = new DxfTag(1070, (short)0);
                }
                raw = raw.WithRecord(record, tags);
                using var input = new MemoryStream(SaveRaw(raw, binary));
                var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Authored raw reset load");
                var actual = ContainerOverrides(ResetHosts(loaded)[0]);
                bool first = s != 1, second = s is 1 or 2 or 3 or 4;
                Equal(first, actual.ContainsType(ResetKinds[0]), "Raw first presence"); Equal(second, actual.ContainsType(ResetKinds[1]), "Raw second presence");
                foreach (var field in new[] { 0, 1 }) if (actual.ContainsType(ResetKinds[field])) {
                    if (s < 4) Equal(null, actual[ResetKinds[field]].Value, "Raw zero is default arrow");
                    else Equal("RESET_BASE_ARROW", ((Block)actual[ResetKinds[field]].Value).Name, "Raw shared custom arrow");
                }
                if (s == 5) Equal(null, actual[ResetKinds[3]].Value, "Raw fill clear");
                else Equal((short)3, ((AciColor)actual[ResetKinds[3]].Value).Index, "Raw fill color");
                using var output = new MemoryStream(); Check(loaded.Save(output, !binary), "Raw reset serialization");
                output.Position = 0; var again = DxfDocument.Load(output) ?? throw new InvalidOperationException("Raw reset second load");
                var retained = ContainerOverrides(ResetHosts(again)[0]); Equal(actual.Count, retained.Count, "Raw reset count after resave");
                foreach (var item in actual.Values) if (item.Value == null) Equal(null, retained[item.Type].Value, "Raw reset after resave");
            });
        }
    }
}
