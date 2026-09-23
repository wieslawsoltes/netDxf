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
    private static readonly (double upper, double lower)[] TransitionBounds = {
        (.25, .125), (-.5, .25), (double.Epsilon, 0),
        (0, BitConverter.Int64BitsToDouble(long.MinValue)), (1e-300, -1e-300), (.25, .25)
    };

    private static double TransitionLower(int row, bool baseStyle)
    {
        var pair = TransitionBounds[row % TransitionBounds.Length];
        bool symmetric = baseStyle || row / TransitionBounds.Length == 1;
        return symmetric && pair.upper != pair.lower ? pair.upper : pair.lower;
    }

    private static void RegisterToleranceModeTransitionTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool leader in new[] { false, true }) for (int placement = 0; placement < 4; placement++)
        {
            int p = placement;
            Run($"tolerance-mode-transition/{version}/{binary}/{leader}/{p}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var hosts = Enumerable.Range(0, 4 * TransitionBounds.Length).Select(row => TransitionHost(row, leader)).ToArray();
                if (p == 0) foreach (var host in hosts) doc.Entities.Add(host);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("TRANSITION_PAPER"));
                    foreach (var host in hosts) doc.Layouts["TRANSITION_PAPER"].AssociatedBlock.Entities.Add(host);
                }
                else
                {
                    var block = new Block("TRANSITION_HOLDER", hosts);
                    if (p == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block);
                }
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                string[] handles = hosts.Select(h => h.Handle).ToArray();
                string stem = $"tolerance-mode-transition-{version}-{binary}-{leader}-{p}";
                for (int pass = 0; pass < 3; pass++)
                {
                    bool output = pass == 0 ? binary : pass == 1 ? false : true;
                    using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Transition save");
                    byte[] bytes = stream.ToArray(); CheckTransitionPacket(LoadRaw(bytes));
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + (pass == 0 ? "-source.dxf" : pass == 1 ? "-False.dxf" : "-True.dxf")), bytes);
                    for (int row = 0; row < hosts.Length; row++)
                    {
                        // Retain source identity, sparse presence and inactive lower bits after every save.
                        var original = hosts[row]; var pair = TransitionBounds[row % TransitionBounds.Length];
                        SameDoubleBits(pair.lower, CompositeStyle(original).Tolerances.LowerLimit, "Source inactive bound mutated");
                        Equal(2, ContainerOverrides(original).Count, "Source sparse dictionary expanded");
                    }
                    stream.Position = 0;
                    doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Transition reload");
                    Check(stream.CanRead, "Caller stream closed");
                    CheckTransitionDocument(doc, handles, leader);
                }
            });
        }
    }

    private static EntityObject TransitionHost(int row, bool leader)
    {
        var pair = TransitionBounds[row % TransitionBounds.Length];
        var style = new DimensionStyle("TRANSITION_" + row.ToString("D2"));
        style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Symmetrical;
        style.Tolerances.UpperLimit = pair.upper; style.Tolerances.LowerLimit = pair.lower;
        EntityObject host = leader ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3) }, style)
            : new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = "FIXED" };
        host.Layer = new Layer("TRANSITION_" + row.ToString("D2"));
        ContainerOverrides(host).Add(DimensionStyleOverrideType.TolerancesDisplayMethod,
            (DimensionStyleTolerancesDisplayMethod)(row / TransitionBounds.Length));
        ContainerOverrides(host).Add(DimensionStyleOverrideType.TextHeight, .75);
        var data = new XData(new ApplicationRegistry("TRANSITION_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); host.XData.Add(data);
        return host;
    }

    private static void CheckTransitionPacket(DxfRawDocument raw)
    {
        var records = raw.Sections.SelectMany(s => s.Records).ToArray();
        for (int row = 0; row < 4 * TransitionBounds.Length; row++)
        {
            string name = "TRANSITION_" + row.ToString("D2");
            var style = records.Single(r => r.Name == "DIMSTYLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, name)));
            SameDoubleBits(TransitionLower(row, true), (double)style.Tags.Single(t => t.Code == 48).Value, "Serialized base lower");
            var host = records.Single(r => (r.Name == "DIMENSION" || r.Name == "LEADER") && r.Tags.Any(t => t.Code == 8 && Equals(t.Value, name)));
            var tags = host.Tags;
            var values = new Dictionary<short, object>();
            for (int i = 0; i < tags.Count - 1; i++) if (tags[i].Code == 1070)
            {
                short id = (short)tags[i].Value;
                Check(!values.ContainsKey(id), "Duplicate DSTYLE identifier"); values.Add(id, tags[i + 1].Value); i++; // Consume the value, even when it is also group 1070.
            }
            bool lowerNeeded = TransitionLower(row, true) != TransitionLower(row, false);
            var wanted = lowerNeeded ? new short[] { 48, 71, 72, 140 } : new short[] { 71, 72, 140 };
            Check(wanted.OrderBy(x => x).SequenceEqual(values.Keys.OrderBy(x => x)), "Minimal exact DSTYLE field set");
            if (lowerNeeded) SameDoubleBits(TransitionLower(row, false), (double)values[48], "Reactivated lower field");
            int mode = row / TransitionBounds.Length;
            Equal((short)(mode is 1 or 2 ? 1 : 0), (short)values[71], "DIMTOL mode");
            Equal((short)(mode == 3 ? 1 : 0), (short)values[72], "DIMLIM mode");
            SameDoubleBits(.75, (double)values[140], "Unrelated scalar");
        }
    }

    private static void CheckTransitionDocument(DxfDocument doc, string[] handles, bool leader)
    {
        var hosts = doc.Blocks.SelectMany(b => b.Entities).Where(e => e is Dimension || e is Leader)
            .OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(handles.Length, hosts.Length, "Transition host inventory");
        for (int row = 0; row < hosts.Length; row++)
        {
            var host = hosts[row]; Equal(handles[row], host.Handle, "Transition identity");
            Equal(leader, host is Leader, "Transition family");
            var pair = TransitionBounds[row % TransitionBounds.Length];
            var style = CompositeStyle(host); var overrides = ContainerOverrides(host);
            SameDoubleBits(pair.upper, style.Tolerances.UpperLimit, "Base upper");
            SameDoubleBits(TransitionLower(row, true), style.Tolerances.LowerLimit, "Base lower");
            bool needed = TransitionLower(row, true) != TransitionLower(row, false);
            Equal(needed, overrides.ContainsType(DimensionStyleOverrideType.TolerancesLowerLimit), "Loaded minimal lower presence");
            Equal(false, overrides.ContainsType(DimensionStyleOverrideType.TolerancesUpperLimit), "Unselected upper materialized");
            Equal(needed ? 3 : 2, overrides.Count, "Loaded override count");
            double effective = needed ? (double)overrides[DimensionStyleOverrideType.TolerancesLowerLimit].Value : style.Tolerances.LowerLimit;
            SameDoubleBits(TransitionLower(row, false), effective, "Effective lower survived mode transition");
            int mode = row / TransitionBounds.Length;
            if (mode == 2 && pair.upper == effective) mode = 1; // Native equal bounds have symmetric representation.
            Equal((DimensionStyleTolerancesDisplayMethod)mode, (DimensionStyleTolerancesDisplayMethod)overrides[DimensionStyleOverrideType.TolerancesDisplayMethod].Value, "Native mode");
            Equal("unchanged", (string)host.XData["TRANSITION_KEEP"].XDataRecord.Single().Value, "Unrelated application data");
            var clone = (EntityObject)host.Clone();
            ContainerOverrides(clone).Clear();
            Equal(needed ? 3 : 2, overrides.Count, "Clone dictionary isolation");
            if (host is Dimension dim) { dim.Update(); Equal("FIXED", dim.Block.Entities.OfType<MText>().Single().Value, "Literal label"); }
        }
        Equal(0, doc.Objects.Validate().Count, "Transition object graph");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint); RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
