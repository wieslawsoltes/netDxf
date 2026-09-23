// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static string SymmetryLabel(int variant) => variant == 1
        ? "{\\A1;10.0000{\\H0.5x;\\S+0.250^ -0.125;}}"
        : "{\\A1;10.0000{\\H0.5x;±" + (variant == 3 ? "0.500" : variant == 5 ? "0.000" : "0.250") + "}}";

    private static AlignedDimension SymmetryDimension(int variant)
    {
        var style = new DimensionStyle("SN_STYLE") { TextHeight = .75, DimScaleOverall = 2, TextFractionHeightScale = .5 };
        style.Tolerances.DisplayMethod = variant == 2 ? DimensionStyleTolerancesDisplayMethod.Deviation : DimensionStyleTolerancesDisplayMethod.Symmetrical;
        style.Tolerances.UpperLimit = variant == 5 ? 0.0 : .25;
        style.Tolerances.LowerLimit = variant == 5 ? -0.0 : .125;
        style.Tolerances.Precision = 3;
        var dim = new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = "<>" };
        dim.TextReferencePoint = new Vector2(17.25, 0);
        dim.StyleOverrides.Add(DimensionStyleOverrideType.TextColor, new AciColor(3));
        if (variant == 1) dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, DimensionStyleTolerancesDisplayMethod.Deviation);
        if (variant == 2) dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, DimensionStyleTolerancesDisplayMethod.Symmetrical);
        if (variant == 3) dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesUpperLimit, .5);
        if (variant == 4) dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesLowerLimit, .75);
        return dim;
    }

    private static void RegisterToleranceSymmetryTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            for (int variant = 0; variant < 6; variant++)
            {
                int v = variant;
                Run($"tolerance-label/symmetry/{version}/{binary}/{v}", () =>
                {
                    var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                    var dim = SymmetryDimension(v); doc.Entities.Add(dim); doc.DrawingVariables.DimStyle = dim.Style.Name;
                    Equal(SymmetryLabel(v), TolLabel(dim), "Symmetrical uses the public upper allowance");
                    var snapshot = DxSnapshot(dim);
                    double lower = dim.Style.Tolerances.LowerLimit;
                    using var source = new MemoryStream(); Check(doc.Save(source, binary), "Symmetry source save");
                    snapshot(); SameDoubleBits(lower, dim.Style.Tolerances.LowerLimit, "Inactive source allowance changed");
                    Equal(v is 0 or 5 ? 1 : 2, dim.StyleOverrides.Count, "Serialization materialized source overrides");
                    string stem = $"tolerance-symmetric-{version}-{binary}-{v}";
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                    source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Symmetric load");
                    CheckSymmetry(loaded, v, dim.Handle);
                    foreach (bool output in new[] { false, true })
                    {
                        using var result = new MemoryStream(); Check(loaded.Save(result, output), "Symmetric output");
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), result.ToArray());
                        result.Position = 0;
                        CheckSymmetry(DxfDocument.Load(result) ?? throw new InvalidOperationException("Symmetric reload"), v, dim.Handle);
                    }
                    Check(source.CanRead, "Symmetry load closed caller stream");
                });
            }
            Run($"tolerance-label/foreign-symmetry/{version}/{binary}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                doc.Entities.Add(SymmetryDimension(4));
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Foreign symmetry seed");
                var raw = LoadRaw(source.ToArray()); var host = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "DIMENSION");
                var tags = host.Tags.ToArray();
                int at = Enumerable.Range(0, tags.Length - 1).Single(i => tags[i].Code == 1070 && Equals(tags[i].Value, (short)48));
                tags[at + 1] = new DxfTag(1040, .125); raw = raw.WithRecord(host, tags);
                using var input = new MemoryStream(SaveRaw(raw, binary));
                var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Sparse foreign symmetry load");
                var dim = loaded.Entities.Dimensions.Single();
                Check(!dim.StyleOverrides.ContainsType(DimensionStyleOverrideType.TolerancesUpperLimit), "Missing foreign upper materialized");
                Equal(DimensionStyleTolerancesDisplayMethod.Deviation, (DimensionStyleTolerancesDisplayMethod)dim.StyleOverrides[DimensionStyleOverrideType.TolerancesDisplayMethod].Value, "Foreign unequal bounds keep native deviation semantics");
                Equal(SymmetryLabel(1), TolLabel(dim), "Foreign tolerance label");
            });
        }
    }

    private static void CheckSymmetry(DxfDocument doc, int variant, string handle)
    {
        var dim = doc.Entities.Dimensions.Single(); Equal(handle, dim.Handle, "Symmetry handle");
        SameDoubleBits(variant == 2 ? .125 : variant == 5 ? -0.0 : .25, dim.Style.Tolerances.LowerLimit, "Effective native base lower allowance");
        Equal(variant is 0 or 5 ? 1 : variant == 4 ? 2 : 3, dim.StyleOverrides.Count, "Only necessary native lower component materialized");
        for (int i = 0; i < 2; i++)
        {
            dim.Update(); Equal(SymmetryLabel(variant), dim.Block.Entities.OfType<MText>().Single().Value, "Symmetry regenerated label");
        }
        Equal(SymmetryLabel(variant), TolLabel((Dimension)dim.Clone()), "Symmetry clone");
        Equal(0, doc.Objects.Validate().Count, "Symmetry object graph");
    }
}
