// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly double[] ToleranceLowerValues = {
        0.0, -0.0, double.Epsilon, -double.Epsilon, 1e-300, -1e-300,
        1e-13, -1e-13, 1.0, Math.BitIncrement(1.0)
    };
    private static DimensionStyle ToleranceFidelityStyle(string name) => new(name) {
        Tolerances = new DimensionStyleTolerances {
            DisplayMethod = DimensionStyleTolerancesDisplayMethod.Deviation, UpperLimit = .125, LowerLimit = .25
        }
    };
    private static DimensionStyleOverrideType TolType(int field) => field == 0 ? DimensionStyleOverrideType.TolerancesDisplayMethod
        : field == 1 ? DimensionStyleOverrideType.TolerancesUpperLimit : DimensionStyleOverrideType.TolerancesLowerLimit;
    private static bool TolSelected(int row, int field) => field == 0 ? row is >= 1 and <= 9
        : field == 1 ? row is 2 or 4 or 5 or 6 or 7 or 8 or 9 or 11
        : row is 3 or 4 or 5 or 6 or 7 or 8 or 9 or 11;
    private static double TolValue(int row, bool upper) => row switch {
        2 => .25, 3 => .125, 4 => upper ? 1.0 : Math.BitIncrement(1.0),
        5 => upper ? 1e-13 : 0.0, 6 => upper ? 0.0 : -0.0,
        7 => upper ? .5 : .6, 8 => upper ? -.125 : -.25, 9 => .125,
        11 => upper ? 1e-300 : -1e-300, _ => upper ? .125 : .25
    };
    private static DimensionStyleTolerancesDisplayMethod TolMethod(int row, bool loaded) => row switch {
        7 => DimensionStyleTolerancesDisplayMethod.None,
        8 => DimensionStyleTolerancesDisplayMethod.Limits,
        9 => DimensionStyleTolerancesDisplayMethod.Symmetrical,
        2 or 3 or 6 when loaded => DimensionStyleTolerancesDisplayMethod.Symmetrical,
        _ => DimensionStyleTolerancesDisplayMethod.Deviation
    };
    private static EntityObject[] ToleranceHosts(DxfDocument doc) => doc.Blocks.SelectMany(b => b.Entities)
        .Where(e => e.Layer.Name.StartsWith("TOL_ROW_", StringComparison.Ordinal)).OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray();
    private static void CheckToleranceHost(EntityObject entity, int row, bool loaded)
    {
        var style = CompositeStyle(entity); var entries = ContainerOverrides(entity);
        SameDoubleBits(.125, style.Tolerances.UpperLimit, "Base upper retained");
        SameDoubleBits(.25, style.Tolerances.LowerLimit, "Base lower retained");
        Equal(DimensionStyleTolerancesDisplayMethod.Deviation, style.Tolerances.DisplayMethod, "Base method");
        Equal(1 + Enumerable.Range(0, 3).Count(f => TolSelected(row, f)), entries.Count, "Sparse override count");
        for (int field = 0; field < 3; field++) {
            Equal(TolSelected(row, field), entries.ContainsType(TolType(field)), "Omitted limit must remain inherited");
            if (!TolSelected(row, field)) continue;
            if (field == 0) Equal(TolMethod(row, loaded), (DimensionStyleTolerancesDisplayMethod)entries[TolType(0)].Value, "Exact effective tolerance mode");
            else SameDoubleBits(TolValue(row, field == 1), (double)entries[TolType(field)].Value, "Stored tolerance bound");
        }
        SameDoubleBits(.75, (double)entries[DimensionStyleOverrideType.TextHeight].Value, "Unrelated override");
        Equal("unchanged", (string)entity.XData["TOL_KEEP"].XDataRecord.Single().Value, "Other application");
    }
    private static DxfDocument ToleranceDocument(DxfVersion version, bool leader, int placement)
    {
        var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
        var style = ToleranceFidelityStyle("TOL_BASE"); var hosts = Enumerable.Range(0, 12).Select(row => {
            EntityObject host = leader ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3) }, style)
                : new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = "FIXED" };
            host.Layer = new Layer("TOL_ROW_" + row.ToString("D2")); var entries = ContainerOverrides(host);
            entries.Add(DimensionStyleOverrideType.TextHeight, .75);
            for (int field = 0; field < 3; field++) if (TolSelected(row, field))
                entries.Add(TolType(field), field == 0 ? (object)TolMethod(row, false) : TolValue(row, field == 1));
            var acad = new XData(new ApplicationRegistry("ACAD"));
            acad.XDataRecord.AddRange(new[] { DxString("NEIGHBOR"), DxString("DSTYLE"), DxOpen, DxId(43), DxReal(11), DxClose, DxString("TAIL") }); host.XData.Add(acad);
            var keep = new XData(new ApplicationRegistry("TOL_KEEP")); keep.XDataRecord.Add(DxString("unchanged")); host.XData.Add(keep);
            return host;
        }).ToArray();
        if (placement == 0) foreach (var host in hosts) doc.Entities.Add(host);
        else if (placement == 1) {
            doc.Layouts.Add(new Layout("TOL_PAPER"));
            foreach (var host in hosts) doc.Layouts["TOL_PAPER"].AssociatedBlock.Entities.Add(host);
        } else {
            var block = new Block("TOL_HOLDER", hosts);
            if (placement == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block);
        }
        doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
        return doc;
    }
    private static void RegisterToleranceFidelityTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int index = 0; index < ToleranceLowerValues.Length; index++) {
            int i = index;
            Run($"tolerance-fidelity/header/{version}/{binary}/{i}", () => {
                var doc = new DxfDocument(version); doc.Comments.Clear();
                var style = ToleranceFidelityStyle("TOL_HEADER"); style.Tolerances.UpperLimit = 1.0; style.Tolerances.LowerLimit = ToleranceLowerValues[i];
                doc.DimensionStyles.Add(style); doc.DrawingVariables.DimStyle = style.Name;
                doc.Entities.Add(new Line(Vector2.Zero, Vector2.UnitX));
                foreach (string phase in new[] { "source", "again" }) {
                    using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Tolerance header save");
                    var raw = LoadRaw(stream.ToArray()); var tags = raw.Tags;
                    int at = Enumerable.Range(0, tags.Count - 1).Single(n => tags[n].Code == 9 && Equals(tags[n].Value, "$DIMTM"));
                    Equal((short)40, tags[at + 1].Code, "Header type"); SameDoubleBits(ToleranceLowerValues[i], (double)tags[at + 1].Value, "DIMTM must not be replaced by epsilon");
                    var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "DIMSTYLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "TOL_HEADER")));
                    SameDoubleBits(1, (double)record.Tags.Single(t => t.Code == 47).Value, "Table upper");
                    SameDoubleBits(ToleranceLowerValues[i], (double)record.Tags.Single(t => t.Code == 48).Value, "Table lower");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"tolerance-fidelity-header-{version}-{binary}-{i}-{phase}.dxf"), stream.ToArray());
                    stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Tolerance header load");
                    var loaded = doc.DimensionStyles["TOL_HEADER"].Tolerances;
                    SameDoubleBits(1, loaded.UpperLimit, "Loaded upper"); SameDoubleBits(ToleranceLowerValues[i], loaded.LowerLimit, "Loaded lower");
                    Equal(i == 8 ? DimensionStyleTolerancesDisplayMethod.Symmetrical : DimensionStyleTolerancesDisplayMethod.Deviation, loaded.DisplayMethod, "Exact table tolerance mode");
                    Equal(0, doc.Objects.Validate().Count, "Header graph");
                }
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool leader in new[] { false, true }) for (int placement = 0; placement < 4; placement++) {
            int p = placement;
            Run($"tolerance-fidelity/wire/{version}/{binary}/{leader}/{p}", () => {
                var doc = ToleranceDocument(version, leader, p); var hosts = ToleranceHosts(doc);
                var checks = hosts.Select(DxSnapshot).ToArray(); string[] handles = hosts.Select(e => e.Handle).ToArray();
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Sparse tolerance save"); foreach (var check in checks) check();
                for (int row = 0; row < hosts.Length; row++) CheckToleranceHost(hosts[row], row, false);
                string stem = $"tolerance-fidelity-wire-{version}-{binary}-{leader}-{p}";
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Sparse tolerance load");
                for (int row = 0; row < 12; row++) CheckToleranceHost(ToleranceHosts(loaded)[row], row, true);
                foreach (bool output in new[] { false, true }) {
                    checks = ToleranceHosts(loaded).Select(DxSnapshot).ToArray();
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Sparse tolerance resave"); foreach (var check in checks) check();
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0; var again = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Sparse tolerance reload");
                    hosts = ToleranceHosts(again); Equal(12, hosts.Length, "Host inventory");
                    for (int row = 0; row < hosts.Length; row++) {
                        Equal(handles[row], hosts[row].Handle, "Host identity"); CheckToleranceHost(hosts[row], row, true);
                        var clone = (EntityObject)hosts[row].Clone(); CheckToleranceHost(clone, row, true); ContainerOverrides(clone).Clear(); CheckToleranceHost(hosts[row], row, true);
                        if (hosts[row] is Dimension dim) { dim.Update(); Equal("FIXED", dim.Block.Entities.OfType<MText>().Single().Value, "Literal label"); }
                    }
                    Equal(0, again.Objects.Validate().Count, "Tolerance graph");
                }
            });
        }
    }
}
