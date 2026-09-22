// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly LinearUnitType[] AltuModes = {
        LinearUnitType.Scientific, LinearUnitType.Decimal, LinearUnitType.Engineering,
        LinearUnitType.Architectural, LinearUnitType.Fractional, LinearUnitType.Architectural,
        LinearUnitType.Fractional, LinearUnitType.WindowsDesktop
    };
    private static bool AltuStack(int code) => code is 4 or 5;
    private static int AltuBaseCode(int row) => row == 2 ? 4 : 8;
    private static int AltuOverrideCode(int row) => row == 0 ? 0 : row is 1 or 2 or 11 ? 8 : row - 2;

    private static void RegisterAlternateUnitModeTests()
    {
        for (int kind = 0; kind < 9; kind++) foreach (bool stack in new[] { false, true })
        {
            int k = kind;
            Run($"alternate-unit-mode/family/{k}/{stack}", () =>
            {
                var doc = new DxfDocument { BuildDimensionBlocks = true }; doc.Comments.Clear();
                EntityObject entity = k == 8 ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3) }, new DimensionStyle("ALTU_FAMILY"))
                    : TextBlockDimension(k);
                var style = CompositeStyle(entity);
                style.AlternateUnits.LengthUnits = LinearUnitType.WindowsDesktop;
                style.AlternateUnits.StackUnits = stack;
                if (entity is Dimension d) d.UserText = "FIXED";
                ContainerOverrides(entity).Add(DimensionStyleOverrideType.AltUnitsLengthUnits, LinearUnitType.WindowsDesktop);
                ContainerOverrides(entity).Add(DimensionStyleOverrideType.AltUnitsStackedUnits, stack);
                doc.Entities.Add(entity);
                using var stream = new MemoryStream(); Check(doc.Save(stream, true), "Family mode save");
                Equal(stack, style.AlternateUnits.StackUnits, "Saving mutated inactive base flag");
                Equal(stack, (bool)ContainerOverrides(entity)[DimensionStyleOverrideType.AltUnitsStackedUnits].Value, "Saving mutated inactive override flag");
                stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Family mode load");
                var copy = loaded.Blocks.SelectMany(b => b.Entities).Single(e => e is Dimension || e is Leader);
                Equal(entity.GetType(), copy.GetType(), "Family type");
                Equal(LinearUnitType.WindowsDesktop, CompositeStyle(copy).AlternateUnits.LengthUnits, "Family base mode");
                Equal(false, CompositeStyle(copy).AlternateUnits.StackUnits, "Inactive stacking normalizes on load");
                Equal(LinearUnitType.WindowsDesktop, (LinearUnitType)ContainerOverrides(copy)[DimensionStyleOverrideType.AltUnitsLengthUnits].Value, "Family override mode");
                Equal(false, (bool)ContainerOverrides(copy)[DimensionStyleOverrideType.AltUnitsStackedUnits].Value, "Family loaded stacking");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            for (int code = 1; code <= 8; code++)
            {
                int c = code;
                Run($"alternate-unit-mode/table/{version}/{binary}/{c}", () =>
                {
                    var doc = new DxfDocument(version); doc.Comments.Clear();
                    var style = new DimensionStyle("ALTU_TABLE");
                    style.AlternateUnits.LengthUnits = AltuModes[c - 1]; style.AlternateUnits.StackUnits = AltuStack(c);
                    // Primary units keep their distinct on-wire code 6, not alternate-unit code 8.
                    style.DimLengthUnits = LinearUnitType.WindowsDesktop;
                    doc.DimensionStyles.Add(style); doc.DrawingVariables.DimStyle = style.Name;
                    string stem = $"alternate-unit-table-{version}-{binary}-{c}";
                    using var source = new MemoryStream(); Check(doc.Save(source, binary), "Mode table save");
                    AltuCheckTablePacket(LoadRaw(source.ToArray()), "ALTU_TABLE", c);
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                    source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Mode table load");
                    Equal(AltuModes[c-1], loaded.DimensionStyles["ALTU_TABLE"].AlternateUnits.LengthUnits, "Table loaded mode");
                    Equal(AltuStack(c), loaded.DimensionStyles["ALTU_TABLE"].AlternateUnits.StackUnits, "Table loaded stacking");
                    Equal(LinearUnitType.WindowsDesktop, loaded.DimensionStyles["ALTU_TABLE"].DimLengthUnits, "Primary mode unchanged");
                    foreach (bool output in new[] { false, true })
                    {
                        using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Mode table resave");
                        AltuCheckTablePacket(LoadRaw(stream.ToArray()), "ALTU_TABLE", c);
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    }
                });
            }
            for (int kind = 0; kind < 3; kind++) for (int placement = 0; placement < 4; placement++)
            foreach (bool rawInput in new[] { false, true })
            {
                if (kind == 1 && version < DxfVersion.AutoCad2004) continue;
                int k = kind, p = placement;
                Run($"alternate-unit-mode/wire/{version}/{binary}/{k}/{p}/{rawInput}", () =>
                {
                    var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                    var hosts = Enumerable.Range(0, 12).Select(row => AltuEntity(k, row, rawInput)).ToArray();
                    if (p == 0) foreach (var e in hosts) doc.Entities.Add(e);
                    else if (p == 1)
                    {
                        doc.Layouts.Add(new Layout("ALTU_PAPER"));
                        foreach (var e in hosts) doc.Layouts["ALTU_PAPER"].AssociatedBlock.Entities.Add(e);
                    }
                    else
                    {
                        var block = new Block("ALTU_HOLDER", hosts);
                        if (p == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block);
                    }
                    doc.DrawingVariables.DimStyle = "ALTU_STYLE_00";
                    doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                    string[] handles = hosts.Select(e => e.Handle).ToArray();
                    using var source = new MemoryStream(); Check(doc.Save(source, binary), "Mode source save");
                    byte[] bytes = source.ToArray();
                    if (rawInput) bytes = AltuInjectEight(bytes, binary);
                    else for (int row = 0; row < hosts.Length; row++) AltuCheckEntity(hosts[row], row, false);
                    AltuCheckPacket(LoadRaw(bytes));
                    string stem = $"alternate-unit-mode-{version}-{binary}-{k}-{p}-{rawInput}";
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), bytes);
                    using var input = new MemoryStream(bytes);
                    var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Mode source load");
                    AltuCheckDocument(loaded, handles);
                    foreach (bool output in new[] { false, true })
                    {
                        using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Mode output save");
                        AltuCheckPacket(LoadRaw(stream.ToArray()));
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                        stream.Position = 0;
                        var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Mode output load");
                        AltuCheckDocument(second, handles);
                    }
                    Check(input.CanRead, "Caller input stream closed");
                });
            }
        }
    }

    private static EntityObject AltuEntity(int kind, int row, bool seed)
    {
        int baseCode = AltuBaseCode(row), code = AltuOverrideCode(row);
        var style = new DimensionStyle("ALTU_STYLE_" + row.ToString("D2"));
        style.AlternateUnits.LengthUnits = AltuModes[(seed && baseCode == 8 ? 1 : baseCode) - 1];
        style.AlternateUnits.StackUnits = AltuStack(baseCode);
        style.DimLengthUnits = LinearUnitType.WindowsDesktop;
        EntityObject host = kind == 2 ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3), new Vector2(9, 3) }, style)
            : kind == 1 ? TextBlockDimension(7) : new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style);
        if (host is Dimension dim) { dim.Style = style; dim.UserText = "FIXED"; }
        host.Layer = new Layer("ALTU_" + row.ToString("D2"));
        var overrides = ContainerOverrides(host); overrides.Add(DimensionStyleOverrideType.TextHeight, 0.75);
        if (row != 0 && row != 1)
            overrides.Add(DimensionStyleOverrideType.AltUnitsLengthUnits, AltuModes[(seed && code == 8 ? 1 : code)-1]);
        if (row == 1 || row >= 3) overrides.Add(DimensionStyleOverrideType.AltUnitsStackedUnits, row is 1 or 11 || AltuStack(code));
        var xdata = new XData(new ApplicationRegistry("ALTU_KEEP"));
        xdata.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); host.XData.Add(xdata);
        return host;
    }

    private static byte[] AltuInjectEight(byte[] bytes, bool binary)
    {
        var raw = LoadRaw(bytes);
        // Start from an ordinary code-1 seed, independent of the new encoder.
        for (int row = 0; row < 12; row++)
        {
            var style = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "DIMSTYLE" &&
                r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "ALTU_STYLE_" + row.ToString("D2"))));
            raw = raw.WithRecord(style, style.Tags.Select(t => t.Code == 273 ? new DxfTag(273, (short)AltuBaseCode(row)) : t));
            if (AltuOverrideCode(row) != 8) continue;
            var host = raw.Sections.SelectMany(s => s.Records).Single(r =>
                r.Tags.Any(t => t.Code == 8 && Equals(t.Value, "ALTU_" + row.ToString("D2"))));
            var tags = host.Tags.ToArray();
            int at = Enumerable.Range(0, tags.Length-1).Single(i => tags[i].Code == 1070 && Equals(tags[i].Value, (short)273));
            tags[at+1] = new DxfTag(1070, (short)8); raw = raw.WithRecord(host, tags);
        }
        var all = raw.Tags.ToArray(); int header = Enumerable.Range(0, all.Length-1).Single(i =>
            all[i].Code == 9 && Equals(all[i].Value, "$DIMALTU"));
        all[header+1] = new DxfTag(70, (short)8);
        return SaveRaw(raw.WithTags(all), binary);
    }

    private static void AltuCheckTablePacket(DxfRawDocument raw, string name, int code)
    {
        var style = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "DIMSTYLE" &&
            r.Tags.Any(t => t.Code == 2 && Equals(t.Value, name)));
        Equal((short)code, (short)style.Tags.Single(t => t.Code == 273).Value, "Table mode packet");
        Equal((short)6, (short)style.Tags.Single(t => t.Code == 277).Value, "Primary mode packet");
        foreach (var pair in new[] { ("$DIMALTU", code), ("$DIMLUNIT", 6) })
        {
            int at = Enumerable.Range(0, raw.Tags.Count-1).Single(i => raw.Tags[i].Code == 9 && Equals(raw.Tags[i].Value, pair.Item1));
            Equal((short)70, raw.Tags[at+1].Code, "Complete header variable");
            Equal((short)pair.Item2, (short)raw.Tags[at+1].Value, "Header mode");
        }
    }

    private static void AltuCheckPacket(DxfRawDocument raw)
    {
        AltuCheckTablePacket(raw, "ALTU_STYLE_00", 8);
        for (int row = 0; row < 12; row++)
        {
            var r = raw.Sections.SelectMany(s => s.Records).Single(r =>
                r.Tags.Any(t => t.Code == 8 && Equals(t.Value, "ALTU_" + row.ToString("D2"))));
            var slots = Enumerable.Range(0, r.Tags.Count-1).Where(i => r.Tags[i].Code == 1070 && Equals(r.Tags[i].Value, (short)273)).ToArray();
            Equal(row == 0 ? 0 : 1, slots.Length, "Mode override presence");
            if (row > 0)
            {
                Equal((short)1070, r.Tags[slots[0]+1].Code, "Mode override integer type");
                Equal((short)AltuOverrideCode(row), (short)r.Tags[slots[0]+1].Value, "Complete mode override");
            }
        }
    }

    private static void AltuCheckEntity(EntityObject host, int row, bool loaded)
    {
        var style = CompositeStyle(host); int code = AltuOverrideCode(row), baseCode = AltuBaseCode(row);
        Equal(AltuModes[baseCode-1], style.AlternateUnits.LengthUnits, "Base unit preserved");
        Equal(AltuStack(baseCode), style.AlternateUnits.StackUnits, "Base stacking preserved");
        Equal(LinearUnitType.WindowsDesktop, style.DimLengthUnits, "Primary format preserved");
        var overrides = ContainerOverrides(host);
        bool units = row > 0 && (loaded || row != 1), stack = row > 0 && (loaded || row != 2);
        Equal(units, overrides.ContainsType(DimensionStyleOverrideType.AltUnitsLengthUnits), "Sparse/loaded unit presence");
        Equal(stack, overrides.ContainsType(DimensionStyleOverrideType.AltUnitsStackedUnits), "Sparse/loaded stacking presence");
        if (units) Equal(AltuModes[code-1], (LinearUnitType)overrides[DimensionStyleOverrideType.AltUnitsLengthUnits].Value, "Override mode preserved");
        if (stack) Equal(!loaded && row is 1 or 11 || AltuStack(code), (bool)overrides[DimensionStyleOverrideType.AltUnitsStackedUnits].Value, "Override stacking policy");
        Equal(1 + (units ? 1 : 0) + (stack ? 1 : 0), overrides.Count, "Override count");
        SameDoubleBits(0.75, (double)overrides[DimensionStyleOverrideType.TextHeight].Value, "Unrelated scalar");
        Equal("unchanged", (string)host.XData["ALTU_KEEP"].XDataRecord.Single().Value, "Other application XData");
    }

    private static void AltuCheckDocument(DxfDocument doc, string[] handles)
    {
        var hosts = doc.Blocks.SelectMany(b => b.Entities).Where(e => e is Dimension || e is Leader)
            .OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(12, hosts.Length, "Mode host count");
        for (int row = 0; row < hosts.Length; row++)
        {
            var host = hosts[row]; Equal(handles[row], host.Handle, "Mode host identity");
            AltuCheckEntity(host, row, true);
            var clone = (EntityObject)host.Clone(); AltuCheckEntity(clone, row, true);
            CompositeStyle(clone).AlternateUnits.LengthUnits = LinearUnitType.Decimal;
            if (row > 0) ContainerOverrides(clone).Remove(DimensionStyleOverrideType.AltUnitsLengthUnits);
            AltuCheckEntity(host, row, true);
            if (host is Dimension dim)
            {
                for (int repeat = 0; repeat < 2; repeat++) dim.Update();
                Equal("FIXED", dim.Block.Entities.OfType<MText>().Single().Value, "Stored-mode checks do not alter primary labels");
                AltuCheckEntity(host, row, true);
            }
        }
        Equal(0, doc.Objects.Validate().Count, "Mode object graph");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
