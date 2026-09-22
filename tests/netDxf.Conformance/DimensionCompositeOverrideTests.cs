// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly int[] CompositeCodes = { 78, 79, 285, 284, 286, 273 };
    private static readonly string[][] CompositeProperties = {
        new[] { "SuppressLinearLeadingZeros", "SuppressLinearTrailingZeros", "SuppressZeroFeet", "SuppressZeroInches" },
        new[] { "SuppressAngularLeadingZeros", "SuppressAngularTrailingZeros" },
        new[] { "SuppressLinearLeadingZeros", "SuppressLinearTrailingZeros", "SuppressZeroFeet", "SuppressZeroInches" },
        new[] { "SuppressLinearLeadingZeros", "SuppressLinearTrailingZeros", "SuppressZeroFeet", "SuppressZeroInches" },
        new[] { "AlternateSuppressLinearLeadingZeros", "AlternateSuppressLinearTrailingZeros", "AlternateSuppressZeroFeet", "AlternateSuppressZeroInches" },
        new[] { "LengthUnits", "StackUnits" }
    };
    private static DimensionStyleOverrideType CompositeType(int group, int component)
    {
        if (group == 5) return component == 0 ? DimensionStyleOverrideType.AltUnitsLengthUnits : DimensionStyleOverrideType.AltUnitsStackedUnits;
        string name = CompositeProperties[group][component];
        if (group == 2) name = "AltUnits" + name;
        else if (group == 3) name = "Tolerances" + name;
        else if (group == 4) name = "TolerancesAlt" + name.Substring("Alternate".Length);
        return Enum.Parse<DimensionStyleOverrideType>(name);
    }
    private static object CompositeTarget(DimensionStyle style, int group) => group is 2 or 5 ? style.AlternateUnits : group is 3 or 4 ? style.Tolerances : style;
    private static DimensionStyle CompositeStyle(EntityObject entity) => entity is Dimension dim ? dim.Style : ((Leader)entity).Style;
    private static object[] CompositeBase(int group, int seed)
    {
        if (group == 5) return new object[] { seed / 4 == 0 ? LinearUnitType.Architectural : LinearUnitType.Fractional, (seed / 2 & 1) != 0 };
        return Enumerable.Range(0, CompositeProperties[group].Length).Select(i => (object)((seed & 1 << i) != 0)).ToArray();
    }
    private static object[] CompositeEdited(int group, int seed, int component, bool equal)
    {
        object[] result = CompositeBase(group, seed);
        if (!equal) result[component] = group == 5 && component == 0
            ? (LinearUnitType)result[0] == LinearUnitType.Architectural ? LinearUnitType.Fractional : LinearUnitType.Architectural
            : !(bool)result[component];
        return result;
    }
    private static int CompositeWire(int group, object[] values)
    {
        if (group == 5) return (LinearUnitType)values[0] == LinearUnitType.Architectural ? (bool)values[1] ? 4 : 6 : (bool)values[1] ? 5 : 7;
        if (group == 1) return ((bool)values[0] ? 1 : 0) | ((bool)values[1] ? 2 : 0);
        int low = (bool)values[2] ? (bool)values[3] ? 0 : 3 : (bool)values[3] ? 2 : 1;
        return low | ((bool)values[0] ? 4 : 0) | ((bool)values[1] ? 8 : 0);
    }
    private static EntityObject CompositeEntity(bool leader, int group, int seed, int component, bool equal, int row)
    {
        var style = new DimensionStyle("COMPOSITE_STYLE_" + row.ToString("D2"));
        object target = CompositeTarget(style, group); object[] values = CompositeBase(group, seed);
        for (int c = 0; c < values.Length; c++) target.GetType().GetProperty(CompositeProperties[group][c])!.SetValue(target, values[c]);
        EntityObject entity = leader ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3), new Vector2(9, 3) }, style)
            : new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = "FIXED" };
        entity.Layer = new Layer("COMPOSITE_" + row.ToString("D2"));
        var dictionary = ContainerOverrides(entity); dictionary.Add(DimensionStyleOverrideType.TextHeight, 0.75);
        if (component >= 0) dictionary.Add(CompositeType(group, component), CompositeEdited(group, seed, component, equal)[component]);
        var data = new XData(new ApplicationRegistry("COMPOSITE_KEEP")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); entity.XData.Add(data);
        return entity;
    }
    private static void CheckComposite(EntityObject entity, int group, int seed, int component, bool equal, bool loaded)
    {
        var dictionary = ContainerOverrides(entity); object target = CompositeTarget(CompositeStyle(entity), group);
        object[] original = CompositeBase(group, seed), effective = component < 0 ? original : CompositeEdited(group, seed, component, equal);
        for (int c = 0; c < original.Length; c++)
        {
            Equal(original[c], target.GetType().GetProperty(CompositeProperties[group][c])!.GetValue(target)!, "Unchanged base component");
            bool exists = component >= 0 && (loaded || c == component);
            Equal(exists, dictionary.ContainsType(CompositeType(group, c)), "Composite component presence");
            if (exists) Equal(effective[c], dictionary[CompositeType(group, c)].Value, "Effective composite component");
        }
        Equal(component < 0 ? 1 : loaded ? original.Length + 1 : 2, dictionary.Count, "Composite override count");
        SameDoubleBits(0.75, (double)dictionary[DimensionStyleOverrideType.TextHeight].Value, "Unrelated scalar retained");
        Equal("unchanged", (string)entity.XData["COMPOSITE_KEEP"].XDataRecord.Single().Value, "Unrelated application retained");
    }
    private static void RegisterDimensionCompositeOverrideTests()
    {
        for (int group = 0; group < 5; group++) for (int seed = 0; seed < 1 << CompositeProperties[group].Length; seed++)
        for (int component = 0; component < CompositeProperties[group].Length; component++)
        foreach (bool equal in new[] { false, true }) foreach (bool leader in new[] { false, true })
        {
            int g = group, s = seed, c = component;
            Run($"dimension-composite/exhaustive/{g}/{s}/{c}/{equal}/{leader}", () =>
            {
                var doc = new DxfDocument(); var entity = CompositeEntity(leader, g, s, c, equal, 0); doc.Entities.Add(entity);
                using var stream = new MemoryStream(); Check(doc.Save(stream, true), "Composite save");
                CheckComposite(entity, g, s, c, equal, false);
                var raw = LoadRaw(stream.ToArray()); var r = raw.Sections.SelectMany(section => section.Records).Single(r => r.Name == (leader ? "LEADER" : "DIMENSION"));
                int at = Enumerable.Range(0, r.Tags.Count - 1).Single(i => r.Tags[i].Code == 1070 && Equals(r.Tags[i].Value, (short)CompositeCodes[g]));
                Equal((short)CompositeWire(g, CompositeEdited(g, s, c, equal)), (short)r.Tags[at + 1].Value, "Complete group bits");
                stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Composite load");
                var e = loaded.Blocks.SelectMany(b => b.Entities).Single(e => e is Dimension || e is Leader);
                CheckComposite(e, g, s, c, equal, true);
                var clone = (EntityObject)e.Clone(); CheckComposite(clone, g, s, c, equal, true);
                ContainerOverrides(clone).Remove(CompositeType(g, c)); CheckComposite(e, g, s, c, equal, true);
                Equal(0, loaded.Objects.Validate().Count, "Composite object graph");
                Check(stream.CanRead, "Caller stream ownership");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool leader in new[] { false, true }) for (int placement = 0; placement < 4; placement++) for (int group = 0; group < 6; group++)
        {
            int p = placement, g = group;
            Run($"dimension-composite/wire/{version}/{binary}/{leader}/{p}/{g}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                int count = g == 5 ? 9 : (1 << CompositeProperties[g].Length) + 1;
                var entities = Enumerable.Range(0, count).Select(row => {
                    var spec = CompositeRow(g, row);
                    return CompositeEntity(leader, g, spec.seed, spec.component, false, row);
                }).ToArray();
                if (p == 0) foreach (var e in entities) doc.Entities.Add(e);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("COMPOSITE_PAPER"));
                    foreach (var e in entities) doc.Layouts["COMPOSITE_PAPER"].AssociatedBlock.Entities.Add(e);
                }
                else
                {
                    var block = new Block("COMPOSITE_HOLDER", entities);
                    if (p == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block);
                }
                string[] handles = entities.Select(e => e.Handle).ToArray();
                string stem = $"dimension-composite-{version}-{binary}-{leader}-{p}-{g}";
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Composite wire save");
                for (int row = 0; row < count; row++) { var spec = CompositeRow(g, row); CheckComposite(entities[row], g, spec.seed, spec.component, false, false); }
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0; doc = DxfDocument.Load(source) ?? throw new InvalidOperationException("Composite wire load");
                CheckCompositeDocument(doc, g, handles);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Composite wire resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0; var again = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Composite wire reload");
                    CheckCompositeDocument(again, g, handles);
                }
            });
        }
    }
    private static (int seed, int component) CompositeRow(int group, int row) => row == 0
        ? (group == 5 ? 2 : (1 << CompositeProperties[group].Length) - 1, -1)
        : (row - 1, group == 5 ? (row - 1) % 2 : (row - 1) % CompositeProperties[group].Length);
    private static void CheckCompositeDocument(DxfDocument doc, int group, string[] handles)
    {
        var entities = doc.Blocks.SelectMany(b => b.Entities).Where(e => e is Dimension || e is Leader).OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(handles.Length, entities.Length, "Composite entity inventory");
        for (int row = 0; row < entities.Length; row++)
        {
            var spec = CompositeRow(group, row); var entity = entities[row]; Equal(handles[row], entity.Handle, "Composite identity");
            CheckComposite(entity, group, spec.seed, spec.component, false, true);
            if (entity is Dimension dim) { dim.Update(); Equal("FIXED", dim.Block.Entities.OfType<MText>().Single().Value, "Literal label changed"); }
        }
        Equal(0, doc.Objects.Validate().Count, "Composite wire graph");
    }
}
