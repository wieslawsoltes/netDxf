// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace netDxf.Objects
{
    /// <summary>Public cell-property override bits. These identify properties, not their values.</summary>
    [Flags]
    public enum DxfCellProperty
    {
        /// <summary>No properties.</summary>
        None = 0,
        /// <summary>Data and unit type.</summary>
        DataType = 1,
        /// <summary>Data format expression.</summary>
        DataFormat = 2,
        /// <summary>Content rotation.</summary>
        Rotation = 4,
        /// <summary>Block scale.</summary>
        Scale = 8,
        /// <summary>Content alignment.</summary>
        Alignment = 16,
        /// <summary>Content color.</summary>
        ContentColor = 32,
        /// <summary>Text style identity.</summary>
        TextStyle = 64,
        /// <summary>Text height.</summary>
        TextHeight = 128,
        /// <summary>Automatic content scaling.</summary>
        AutoScale = 256,
        /// <summary>Background color.</summary>
        BackgroundColor = 512,
        /// <summary>Left margin.</summary>
        MarginLeft = 1024,
        /// <summary>Top margin.</summary>
        MarginTop = 2048,
        /// <summary>Right margin.</summary>
        MarginRight = 4096,
        /// <summary>Bottom margin.</summary>
        MarginBottom = 8192,
        /// <summary>Content layout.</summary>
        ContentLayout = 16384,
        /// <summary>Merge-all flag.</summary>
        MergeAll = 32768,
        /// <summary>Bottom-to-top flow flag.</summary>
        FlowDirection = 65536,
        /// <summary>Horizontal content spacing.</summary>
        HorizontalSpacing = 131072,
        /// <summary>Vertical content spacing.</summary>
        VerticalSpacing = 262144,
        /// <summary>All public properties recognized by this resolver.</summary>
        All = 524287
    }

    /// <summary>A resolved complete definition and the layer supplying each cell property.</summary>
    public sealed class DxfResolvedCellStyle
    {
        internal DxfResolvedCellStyle(DxfCellStyleFormatDefinition format, IDictionary<DxfCellProperty, int> sources)
        { this.Format = format; this.PropertySources = new ReadOnlyDictionary<DxfCellProperty, int>(new Dictionary<DxfCellProperty, int>(sources)); }
        /// <summary>Gets a new immutable flattened definition with zero override masks and explicit resolved values.</summary>
        public DxfCellStyleFormatDefinition Format { get; }
        /// <summary>Gets the winning layer per property: zero is the complete base, positive indices follow caller override order.</summary>
        public IReadOnlyDictionary<DxfCellProperty, int> PropertySources { get; }
    }

    /// <summary>Resolves public property and grid override masks in an explicit caller-selected cascade.</summary>
    /// <remarks>
    /// The last selected override wins. This does not guess row-versus-column precedence, a zero
    /// style-ID's meaning, native FIELD formatting, private bits, or classic TABLESTYLE duplication.
    /// Applications select the base and ordered scopes; grid snapshots expose the original scopes.
    /// STYLE/LTYPE objects retain identity. No resource is imported or changed and no source packet is edited.
    /// </remarks>
    public static class DxfCellStyleResolver
    {
        /// <summary>Resolves a complete base and zero to 64 ordered override definitions.</summary>
        /// <remarks>Unknown flag bits, overlapping grid masks within a layer and missing selected margins reject. Empty override frames select nothing.</remarks>
        public static DxfResolvedCellStyle Resolve(DxfCellStyleFormatDefinition baseFormat, IEnumerable<DxfCellStyleFormatDefinition> overrides)
        {
            if (baseFormat == null) throw new ArgumentNullException(nameof(baseFormat));
            if (overrides == null) throw new ArgumentNullException(nameof(overrides));
            var layers = new List<DxfCellStyleFormatDefinition> { baseFormat };
            foreach (var layer in overrides)
            {
                if (layer == null || layers.Count > 64) throw new ArgumentException("A cascade accepts at most 64 non-null overrides.", nameof(overrides));
                layers.Add(layer);
            }
            if (baseFormat.Content == null || baseFormat.Values == null || baseFormat.Margins == null)
                throw new NotSupportedException("Resolution requires a complete base with content, table values and margins.");
            var provenance = new Dictionary<DxfCellProperty, int>();
            var winners = new Dictionary<DxfCellProperty, DxfCellStyleFormatDefinition>();
            var grids = new SortedDictionary<int, DxfCellGridFormatDefinition>();
            int known = (int)DxfCellProperty.All;
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                if (layer.StoredDataFlags == 0) continue;
                if (layer.StoredDataFlags != 1 || layer.StoredMarginFlags < 0 || layer.StoredMarginFlags > 1)
                    throw new NotSupportedException("Private format presence flags are not qualified for resolution.");
                int mask = layer.Values.StoredPropertyOverrides | layer.Content.StoredPropertyOverrides;
                if ((mask & ~known) != 0 || (layer.Content.StoredPropertyFlags & ~256) != 0 || (layer.Values.StoredMergeFlags & ~98304) != 0)
                    throw new NotSupportedException("Unknown cell-property or value flags cannot be resolved.");
                if (layerIndex == 0) mask = known;
                for (int bit = 1; bit <= known; bit <<= 1)
                {
                    if ((mask & bit) == 0) continue;
                    if ((bit & 408576) != 0 && layer.Margins == null)
                        throw new NotSupportedException("An overridden margin requires an explicit margin frame.");
                    var property = (DxfCellProperty)bit;
                    winners[property] = layer; provenance[property] = layerIndex;
                }
                int used = 0;
                foreach (var grid in layer.Borders)
                {
                    int edges = grid.StoredIndexMask, properties = grid.Values.StoredPropertyOverrides;
                    if (edges <= 0 || (edges & ~63) != 0 || (used & edges) != 0 || (properties & ~63) != 0)
                        throw new NotSupportedException("Unknown or overlapping grid masks cannot be resolved.");
                    used |= edges;
                    for (int edge = 1; edge <= 32; edge <<= 1)
                    {
                        if ((edges & edge) == 0) continue;
                        if (layerIndex == 0) grids[edge] = FlattenGrid(edge, grid);
                        else if (properties != 0)
                        {
                            if (!grids.TryGetValue(edge, out DxfCellGridFormatDefinition previous))
                                throw new NotSupportedException("A partial grid override requires an explicit base for its edge.");
                            var p = previous.Values; var n = grid.Values;
                            grids[edge] = new DxfCellGridFormatDefinition(edge, new DxfCellGridFormatValues(0,
                                (properties & 1) != 0 ? n.StoredBorderType : p.StoredBorderType,
                                (properties & 8) != 0 ? n.StoredColor : p.StoredColor,
                                (properties & 2) != 0 ? n.StoredLineweight : p.StoredLineweight,
                                (properties & 16) != 0 ? n.StoredVisibility : p.StoredVisibility,
                                (properties & 32) != 0 ? n.DoubleLineSpacing : p.DoubleLineSpacing),
                                (properties & 4) != 0 ? grid.Linetype : previous.Linetype);
                        }
                    }
                }
            }
            DxfCellContentFormatValues Content(DxfCellProperty property) { return winners[property].Content; }
            DxfCellStyleFormatValues Table(DxfCellProperty property) { return winners[property].Values; }
            DxfCellMargins Margin(DxfCellProperty property) { return winners[property].Margins; }
            var content = new DxfCellContentFormatValues(0, Content(DxfCellProperty.AutoScale).StoredPropertyFlags & 256,
                Content(DxfCellProperty.DataType).StoredDataType, Content(DxfCellProperty.DataType).StoredUnitType,
                Content(DxfCellProperty.DataFormat).FormatString, Content(DxfCellProperty.Rotation).Rotation,
                Content(DxfCellProperty.Scale).BlockScale, Content(DxfCellProperty.Alignment).StoredAlignment,
                Content(DxfCellProperty.ContentColor).StoredColor, Content(DxfCellProperty.TextHeight).TextHeight);
            var table = new DxfCellStyleFormatValues(0,
                (Table(DxfCellProperty.MergeAll).StoredMergeFlags & 32768) | (Table(DxfCellProperty.FlowDirection).StoredMergeFlags & 65536),
                Table(DxfCellProperty.BackgroundColor).StoredBackgroundColor, Table(DxfCellProperty.ContentLayout).StoredContentLayout);
            var margins = new DxfCellMargins(Margin(DxfCellProperty.MarginTop).VerticalMargin, Margin(DxfCellProperty.MarginLeft).HorizontalMargin,
                Margin(DxfCellProperty.MarginBottom).BottomMargin, Margin(DxfCellProperty.MarginRight).RightMargin,
                Margin(DxfCellProperty.HorizontalSpacing).HorizontalSpacing, Margin(DxfCellProperty.VerticalSpacing).VerticalSpacing);
            return new DxfResolvedCellStyle(new DxfCellStyleFormatDefinition(baseFormat.StoredType, 1, table, content,
                winners[DxfCellProperty.TextStyle].TextStyle, 1, margins, grids.Values), provenance);
        }
        private static DxfCellGridFormatDefinition FlattenGrid(int edge, DxfCellGridFormatDefinition grid)
        {
            var v = grid.Values;
            return new DxfCellGridFormatDefinition(edge, new DxfCellGridFormatValues(0, v.StoredBorderType,
                v.StoredColor, v.StoredLineweight, v.StoredVisibility, v.DoubleLineSpacing), grid.Linetype);
        }
    }
}
