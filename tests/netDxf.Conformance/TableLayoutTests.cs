using netDxf;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static DxfCellStyleFormatDefinition LayoutDefinition(DxfDocument doc, int mask = 0, int variant = 0,
        int alignment = 1, short fill = 257, int borderType = 1, int visibility = 0, short borderColor = 7,
        int borderMask = 63, int borderProperties = 0, double rotation = 0, int flags = 0, int mergeFlags = 0)
    {
        return new DxfCellStyleFormatDefinition(5, 1, new DxfCellStyleFormatValues(mask, mergeFlags, fill, 1),
            new DxfCellContentFormatValues(mask, flags, 2 + variant, variant, variant == 0 ? "" : "%lu2%pr3",
                rotation, 1 + variant, alignment, (short)(7 - variant), 2 + variant), doc.TextStyles["Standard"], 1,
            new DxfCellMargins(1 + variant, 2 + variant, 3 + variant, 4 + variant, 5 + variant, 6 + variant),
            new[] { new DxfCellGridFormatDefinition(borderMask,
                new DxfCellGridFormatValues(borderProperties, borderType, borderColor, -1, visibility, .2), doc.Linetypes["Continuous"]) });
    }
    private static object ResolvedProperty(DxfCellStyleFormatDefinition f, DxfCellProperty p) => p switch
    {
        DxfCellProperty.DataType => (f.Content.StoredDataType, f.Content.StoredUnitType),
        DxfCellProperty.DataFormat => f.Content.FormatString,
        DxfCellProperty.Rotation => f.Content.Rotation,
        DxfCellProperty.Scale => f.Content.BlockScale,
        DxfCellProperty.Alignment => f.Content.StoredAlignment,
        DxfCellProperty.ContentColor => f.Content.StoredColor,
        DxfCellProperty.TextStyle => f.TextStyle,
        DxfCellProperty.TextHeight => f.Content.TextHeight,
        DxfCellProperty.AutoScale => f.Content.StoredPropertyFlags & 256,
        DxfCellProperty.BackgroundColor => f.Values.StoredBackgroundColor,
        DxfCellProperty.MarginLeft => f.Margins.HorizontalMargin,
        DxfCellProperty.MarginTop => f.Margins.VerticalMargin,
        DxfCellProperty.MarginRight => f.Margins.RightMargin,
        DxfCellProperty.MarginBottom => f.Margins.BottomMargin,
        DxfCellProperty.ContentLayout => f.Values.StoredContentLayout,
        DxfCellProperty.MergeAll => f.Values.StoredMergeFlags & 32768,
        DxfCellProperty.FlowDirection => f.Values.StoredMergeFlags & 65536,
        DxfCellProperty.HorizontalSpacing => f.Margins.HorizontalSpacing,
        DxfCellProperty.VerticalSpacing => f.Margins.VerticalSpacing,
        _ => throw new ArgumentOutOfRangeException(nameof(p))
    };
    private static void RegisterTableLayoutTests()
    {
        foreach (int bit in Enumerable.Range(0, 19).Select(n => 1 << n))
            Run($"table-layout/property/{bit}", () =>
            {
                var doc = new DxfDocument(); var baseFormat = LayoutDefinition(doc);
                var other = LayoutDefinition(doc, bit, 1, 9, 3, rotation: 45, flags: 256, mergeFlags: 98304);
                var result = DxfCellStyleResolver.Resolve(baseFormat, new[] { other });
                foreach (int selected in Enumerable.Range(0, 19).Select(n => 1 << n))
                {
                    var property = (DxfCellProperty)selected;
                    Equal(ResolvedProperty(selected == bit ? other : baseFormat, property), ResolvedProperty(result.Format, property), "single-property resolution");
                    Equal(selected == bit ? 1 : 0, result.PropertySources[property], "property provenance");
                }
                Equal(0, result.Format.Content.StoredPropertyOverrides, "flattened content mask");
                Equal(0, result.Format.Values.StoredPropertyOverrides, "flattened table mask");
                Equal(6, result.Format.Borders.Count, "combined edge mask expansion");
                Throws<NotSupportedException>(() => ((IDictionary<DxfCellProperty, int>)result.PropertySources).Clear());
            });
        foreach (int properties in Enumerable.Range(0, 64))
            Run($"table-layout/grid-properties/{properties}", () =>
            {
                var doc = new DxfDocument(); var prior = LayoutDefinition(doc);
                var next = LayoutDefinition(doc, borderType: 2, visibility: 1, borderColor: 3, borderMask: 2, borderProperties: properties);
                var resolved = DxfCellStyleResolver.Resolve(prior, new[] { next }).Format;
                var target = resolved.Borders.Single(b => b.StoredIndexMask == 2).Values;
                Equal((properties & 1) == 0 ? 1 : 2, target.StoredBorderType, "border style override");
                Equal((short)((properties & 8) == 0 ? 7 : 3), target.StoredColor, "border color override");
                Equal((properties & 16) == 0 ? 0 : 1, target.StoredVisibility, "border visibility override");
                foreach (var untouched in resolved.Borders.Where(b => b.StoredIndexMask != 2)) Equal((short)7, untouched.Values.StoredColor, "unselected edge changed");
            });
        Run("table-layout/layer-order", () =>
        {
            var doc = new DxfDocument(); var prior = LayoutDefinition(doc);
            var one = LayoutDefinition(doc, (int)DxfCellProperty.TextHeight, 1); var two = LayoutDefinition(doc, (int)DxfCellProperty.TextHeight, 2);
            var result = DxfCellStyleResolver.Resolve(prior, new[] { one, DxfCellStyleFormatDefinition.Empty(), two });
            Equal(4d, result.Format.Content.TextHeight, "last selected layer wins"); Equal(3, result.PropertySources[DxfCellProperty.TextHeight], "layer provenance");
        });
        foreach (string fault in new[] { "unknown-property", "unknown-value-flag", "unknown-merge-flag", "unknown-grid-property", "unknown-edge", "negative-edge", "too-many-layers", "empty-base" })
            Run($"table-layout/resolver-rejection/{fault}", () =>
            {
                var doc = new DxfDocument(); var prior = LayoutDefinition(doc); var next = LayoutDefinition(doc);
                if (fault == "unknown-property") next = LayoutDefinition(doc, 1 << 20);
                if (fault == "unknown-value-flag") next = LayoutDefinition(doc, flags: 1);
                if (fault == "unknown-merge-flag") next = LayoutDefinition(doc, mergeFlags: 1);
                if (fault == "unknown-grid-property") next = LayoutDefinition(doc, borderProperties: 64);
                if (fault == "unknown-edge") next = LayoutDefinition(doc, borderMask: 64);
                if (fault == "negative-edge") next = LayoutDefinition(doc, borderMask: -1);
                if (fault == "empty-base") prior = DxfCellStyleFormatDefinition.Empty();
                bool rejected = false;
                try { DxfCellStyleResolver.Resolve(prior, Enumerable.Repeat(next, fault == "too-many-layers" ? 65 : 1)); }
                catch (Exception e) when (e is NotSupportedException || e is ArgumentException) { rejected = true; }
                Check(rejected, "unqualified resolution was accepted");
            });
        foreach (int alignment in Enumerable.Range(1, 9))
            Run($"table-layout/alignment/{alignment}", () =>
            {
                var doc = ConsumerLoad("acad_table_simple.dxf", false); var grid = doc.Objects.Items.OfType<DxfStoredTableContent>().First().GetGrid();
                var style = DxfCellStyleResolver.Resolve(LayoutDefinition(doc, alignment: alignment), Array.Empty<DxfCellStyleFormatDefinition>());
                var layout = DxfTableLayout.Create(grid, _ => style, cell => cell.Address.ToString(), (_, _, _) => 2, false);
                var block = layout.BuildDisplayBlock("ALIGNMENT"); var mtext = block.Entities.OfType<MText>().First();
                Equal((MTextAttachmentPoint)alignment, mtext.AttachmentPoint, "attachment");
                Equal(new Vector3(2 + ((alignment - 1) % 3) * 450 / 2d, -(1 + ((alignment - 1) / 3) * 7 / 2d), 0), mtext.Position, "alignment anchor");
            });
        foreach (bool binary in new[] { false, true })
        foreach (string mode in new[] { "fixed", "grow", "fill", "double", "hidden", "literal" })
            Run($"table-layout/display/{binary}/{mode}", () => TableLayoutDisplay(binary, mode));
        foreach (string fault in new[] { "overflow", "nan-metrics", "negative-metrics", "null-text", "surrogate-text", "rotation", "autoscale", "bottom-flow", "border-color", "border-type", "border-visibility", "late-provider" })
            Run($"table-layout/layout-rejection/{fault}", () =>
            {
                var doc = ConsumerLoad("acad_table_simple.dxf", false); var content = doc.Objects.Items.OfType<DxfStoredTableContent>().First(); var before = content.Payload;
                var grid = content.GetGrid(); var definition = LayoutDefinition(doc, rotation: fault == "rotation" ? 30 : 0, flags: fault == "autoscale" ? 256 : 0,
                    mergeFlags: fault == "bottom-flow" ? 65536 : 0, borderColor: (short)(fault == "border-color" ? 500 : 7),
                    borderType: fault == "border-type" ? 4 : 1, visibility: fault == "border-visibility" ? 3 : 0);
                var style = DxfCellStyleResolver.Resolve(definition, Array.Empty<DxfCellStyleFormatDefinition>());
                int calls = 0; bool rejected = false;
                try
                {
                    DxfTableLayout.Create(grid, _ => style, cell => {
                        if (++calls == 7 && fault == "late-provider") throw new InvalidOperationException("provider failed");
                        return fault == "null-text" ? null! : fault == "surrogate-text" ? "\ud800" : "Cell";
                    }, (_, _, _) => fault == "overflow" ? 1000 : fault == "nan-metrics" ? double.NaN : fault == "negative-metrics" ? -1 : 2, false);
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is NotSupportedException) { rejected = true; }
                Check(rejected, "invalid layout was accepted"); Check(ReferenceEquals(before, content.Payload), "failed detached layout mutated source");
            });
        Run("table-layout/shared-border-conflict", () =>
        {
            var doc = ConsumerLoad("acad_table_simple.dxf", false); var grid = doc.Objects.Items.OfType<DxfStoredTableContent>().First().GetGrid();
            var one = DxfCellStyleResolver.Resolve(LayoutDefinition(doc, borderColor: 1), Array.Empty<DxfCellStyleFormatDefinition>());
            var two = DxfCellStyleResolver.Resolve(LayoutDefinition(doc, borderColor: 2), Array.Empty<DxfCellStyleFormatDefinition>());
            var layout = DxfTableLayout.Create(grid, cell => cell.Address.Column == 1 ? two : one, _ => "", (_, _, _) => 0, false);
            Throws<InvalidOperationException>(() => layout.BuildDisplayBlock("CONFLICT"));
            Equal(22, layout.BuildDisplayBlock("RESOLVED", DxfTableBorderConflictPolicy.LastCell).Entities.Count, "explicit shared-border policy");
        });
        foreach (string file in TableContentFiles)
        foreach (bool binary in new[] { false, true })
            Run($"table-layout/native-format/{file}/{binary}", () =>
            {
                var doc = ConsumerLoad(file, binary); var map = CellStyleMapObject(doc);
                foreach (var entry in map.Entries)
                {
                    var definition = DxfCellStyleFormatDefinition.FromFormat(entry.Format);
                    var resolved = DxfCellStyleResolver.Resolve(definition, Array.Empty<DxfCellStyleFormatDefinition>());
                    Equal(definition.Content.TextHeight, resolved.Format.Content.TextHeight, "native base text height");
                    Check(ReferenceEquals(definition.TextStyle, resolved.Format.TextStyle), "native STYLE identity");
                }
            });
    }
    private static void TableLayoutDisplay(bool binary, string mode)
    {
        var source = ConsumerLoad("acad_table_simple.dxf", binary); var content = source.Objects.Items.OfType<DxfStoredTableContent>().First();
        var before = content.Payload; var grid = content.GetGrid(); long seed = OwnershipSeed(source);
        var style = DxfCellStyleResolver.Resolve(LayoutDefinition(source, fill: (short)(mode == "fill" ? 3 : 257),
            borderType: mode == "double" ? 2 : 1, visibility: mode == "hidden" ? 1 : 0), Array.Empty<DxfCellStyleFormatDefinition>());
        var layout = DxfTableLayout.Create(grid, _ => style, cell => mode == "literal" ? "{\\C1;literal}\n" + cell.Address : cell.Address.ToString(),
            (_, _, _) => mode == "grow" ? 20 : 2, mode == "grow");
        Equal(7, layout.Cells.Count, "merged continuation cells excluded"); Equal(456d, layout.Width, "fixed column sum");
        Equal(mode == "grow" ? 72d : 29d, layout.Height, "measured row growth");
        var block = layout.BuildDisplayBlock("GENERATED_TABLE");
        Equal(mode == "hidden" ? 0 : mode == "double" ? 44 : 22, block.Entities.OfType<Line>().Count(), "shared grid edge count");
        Equal(mode == "fill" ? 7 : 0, block.Entities.OfType<Solid>().Count(), "background fills");
        Equal(7, block.Entities.OfType<MText>().Count(), "literal cell text count");
        Check(ReferenceEquals(before, content.Payload), "detached layout changed original payload"); Equal(seed, OwnershipSeed(source), "detached layout allocated source handles");
        var doc = new DxfDocument(); doc.Entities.Add(new Insert(block));
        Check(doc.Save(Path.Combine(ArtifactDirectory, $"table-layout-{mode}-{binary}.dxf"), binary), "save generated display");
    }
}
