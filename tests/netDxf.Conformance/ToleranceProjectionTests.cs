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
    private static readonly double[] ProjectionUpper = { .25, .25, 2 * double.Epsilon, 0.0 };
    private static readonly double[] ProjectionLower = { .125, BitConverter.Int64BitsToDouble(long.MinValue), -double.Epsilon, BitConverter.Int64BitsToDouble(long.MinValue) };
    private static DimensionStyleTolerancesDisplayMethod ProjectionBase(int row) => row % 10 < 5
        ? DimensionStyleTolerancesDisplayMethod.Symmetrical : DimensionStyleTolerancesDisplayMethod.Deviation;
    private static DimensionStyleTolerancesDisplayMethod ProjectionMode(int row) => row % 5 == 4
        ? ProjectionBase(row) : (DimensionStyleTolerancesDisplayMethod)(row % 5);
    private static double ProjectionNative(int row, bool entity)
    {
        var mode = entity ? ProjectionMode(row) : ProjectionBase(row);
        double upper = ProjectionUpper[row / 10], lower = ProjectionLower[row / 10];
        return mode == DimensionStyleTolerancesDisplayMethod.Symmetrical && upper != lower ? upper : lower;
    }
    private static bool ProjectionWritesLower(int row) => row % 5 != 4 && ProjectionNative(row, false) != ProjectionNative(row, true);

    private static void RegisterToleranceProjectionTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool leader in new[] { false, true }) for (int placement = 0; placement < 4; placement++)
        {
            int place = placement;
            Run($"tolerance-projection/{version}/{binary}/{leader}/{place}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var hosts = Enumerable.Range(0, 40).Select(row => ProjectionEntity(row, leader)).ToArray();
                if (place == 0) foreach (var host in hosts) doc.Entities.Add(host);
                else if (place == 1)
                {
                    doc.Layouts.Add(new Layout("PROJECTION_PAPER"));
                    foreach (var host in hosts) doc.Layouts["PROJECTION_PAPER"].AssociatedBlock.Entities.Add(host);
                }
                else
                {
                    var holder = new Block("PROJECTION_HOLDER", hosts);
                    if (place == 2) doc.Entities.Add(new Insert(holder)); else doc.Blocks.Add(holder);
                }
                string[] handles = hosts.Select(e => e.Handle).ToArray();
                var states = hosts.Select(DxSnapshot).ToArray();
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Projection save");
                for (int row = 0; row < hosts.Length; row++)
                {
                    states[row]();
                    SameDoubleBits(ProjectionLower[row / 10], CompositeStyle(hosts[row]).Tolerances.LowerLimit, "Save changed inactive source lower");
                    Equal(row % 5 == 4 ? 1 : 2, ContainerOverrides(hosts[row]).Count, "Save expanded sparse source");
                }
                string stem = $"tolerance-projection-{version}-{binary}-{leader}-{place}";
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), stream.ToArray());
                ProjectionPacket(LoadRaw(stream.ToArray())); stream.Position = 0;
                var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Projection load failed");
                ProjectionCheck(loaded, handles);
                foreach (bool output in new[] { false, true })
                {
                    using var result = new MemoryStream(); Check(loaded.Save(result, output), "Projection resave");
                    ProjectionPacket(LoadRaw(result.ToArray()));
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), result.ToArray());
                    result.Position = 0;
                    ProjectionCheck(DxfDocument.Load(result) ?? throw new InvalidOperationException("Projection reload failed"), handles);
                }
                Check(stream.CanRead, "Load closed caller stream");
            });
        }
    }

    private static EntityObject ProjectionEntity(int row, bool leader)
    {
        var style = new DimensionStyle("PROJECTION_STYLE_" + row.ToString("D2"));
        style.Tolerances.DisplayMethod = ProjectionBase(row);
        style.Tolerances.UpperLimit = ProjectionUpper[row / 10]; style.Tolerances.LowerLimit = ProjectionLower[row / 10];
        EntityObject host = leader ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3), new Vector2(9, 3) }, style)
            : new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = "FIXED" };
        host.Layer = new Layer("PROJECTION_" + row.ToString("D2"));
        ContainerOverrides(host).Add(DimensionStyleOverrideType.TextColor, new AciColor(3));
        if (row % 5 != 4) ContainerOverrides(host).Add(DimensionStyleOverrideType.TolerancesDisplayMethod, ProjectionMode(row));
        var data = new XData(new ApplicationRegistry("PROJECTION_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); host.XData.Add(data);
        return host;
    }

    private static void ProjectionPacket(DxfRawDocument raw)
    {
        var records = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name is "DIMENSION" or "LEADER").ToArray();
        Equal(40, records.Length, "Projection physical host count");
        foreach (var r in records)
        {
            int row = int.Parse(((string)r.Tags.Single(t => t.Code == 8).Value).Substring(11));
            int start = Enumerable.Range(0, r.Tags.Count).Single(i => r.Tags[i].Code == 1000 && Equals(r.Tags[i].Value, "DSTYLE")) + 2;
            int end = Enumerable.Range(start, r.Tags.Count - start).First(i => r.Tags[i].Code == 1002);
            Equal(0, (end - start) % 2, "Complete DSTYLE pairs");
            var ids = Enumerable.Range(0, (end - start) / 2).Select(i => start + 2 * i).ToArray();
            Check(ids.All(i => r.Tags[i].Code == 1070), "Typed field identifiers");
            Equal(1 + (row % 5 == 4 ? 0 : 2) + (ProjectionWritesLower(row) ? 1 : 0), ids.Length, "Exact DSTYLE field count");
            var lower = ids.Where(i => Equals(r.Tags[i].Value, (short)48)).ToArray();
            Equal(ProjectionWritesLower(row) ? 1 : 0, lower.Length, "Necessary-only lower field");
            Check(!ids.Any(i => Equals(r.Tags[i].Value, (short)47)), "Inherited upper was materialized");
            if (lower.Length != 0)
            {
                Equal((short)1040, r.Tags[lower[0] + 1].Code, "Lower wire type");
                SameDoubleBits(ProjectionNative(row, true), (double)r.Tags[lower[0] + 1].Value, "Lower wire bits");
            }
        }
    }

    private static void ProjectionCheck(DxfDocument doc, string[] handles)
    {
        var hosts = doc.Blocks.SelectMany(b => b.Entities).Where(e => e is Dimension || e is Leader).OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(40, hosts.Length, "Projection typed hosts");
        for (int row = 0; row < hosts.Length; row++)
        {
            var host = hosts[row]; Equal(handles[row], host.Handle, "Projection identity");
            var overrides = ContainerOverrides(host);
            SameDoubleBits(ProjectionNative(row, false), CompositeStyle(host).Tolerances.LowerLimit, "Native base lower");
            Check(!overrides.ContainsType(DimensionStyleOverrideType.TolerancesUpperLimit), "Read materialized upper");
            Equal(ProjectionWritesLower(row), overrides.ContainsType(DimensionStyleOverrideType.TolerancesLowerLimit), "Read lower presence");
            double lower = overrides.ContainsType(DimensionStyleOverrideType.TolerancesLowerLimit)
                ? (double)overrides[DimensionStyleOverrideType.TolerancesLowerLimit].Value : CompositeStyle(host).Tolerances.LowerLimit;
            SameDoubleBits(ProjectionNative(row, true), lower, "Effective lower preserved");
            var clone = (EntityObject)host.Clone();
            Equal(overrides.Count, ContainerOverrides(clone).Count, "Clone sparse fields");
            if (ProjectionWritesLower(row)) SameDoubleBits(lower, (double)ContainerOverrides(clone)[DimensionStyleOverrideType.TolerancesLowerLimit].Value, "Clone lower bits");
            Equal("unchanged", (string)host.XData["PROJECTION_KEEP"].XDataRecord.Single().Value, "Other XData");
        }
        Equal(0, doc.Objects.Validate().Count, "Projection graph");
    }
}
