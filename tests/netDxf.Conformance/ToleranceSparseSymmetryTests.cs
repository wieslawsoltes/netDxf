// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterToleranceSparseSymmetryTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int variant = 0; variant < 4; variant++)
        {
            int v = variant;
            Run($"tolerance-label/sparse-symmetry/{version}/{binary}/{v}", () =>
            {
                double minusZero = BitConverter.Int64BitsToDouble(long.MinValue);
                var style = new DimensionStyle("SPARSE_SYMMETRY") { LengthPrecision = 2, TextFractionHeightScale = .5 };
                style.Tolerances.DisplayMethod = v is 1 or 2 ? DimensionStyleTolerancesDisplayMethod.Symmetrical : DimensionStyleTolerancesDisplayMethod.Deviation;
                style.Tolerances.UpperLimit = .25; style.Tolerances.LowerLimit = v is 1 or 2 ? 999 : .125;
                style.Tolerances.Precision = 3;
                var dim = new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = "<>" };
                if (v == 0) dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, DimensionStyleTolerancesDisplayMethod.Symmetrical);
                else dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesUpperLimit, v == 2 ? 0.0 : .5);
                if (v == 2) dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesLowerLimit, minusZero);
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Entities.Add(dim);
                string expected = TolText(dim); int originalCount = dim.StyleOverrides.Count;
                short[] identifiers = v == 0 ? new short[] { 48, 71, 72 } : v == 3 ? new short[] { 47 } : new short[] { 47, 48 };
                for (int pass = 0; pass < 3; pass++)
                {
                    var target = doc.Entities.Dimensions.Single();
                    Equal(expected, TolText(target), "Sparse generated label changed");
                    using var output = new MemoryStream(); Check(doc.Save(output, pass == 1 ? !binary : binary), "Sparse symmetry save");
                    var record = LoadRaw(output.ToArray()).Sections.SelectMany(s => s.Records).Single(r => r.Name == "DIMENSION");
                    var values = new Dictionary<short, object>();
                    for (int i = 0; i < record.Tags.Count - 1; i++)
                        if (record.Tags[i].Code == 1070 && identifiers.Contains((short)record.Tags[i].Value))
                        { Check(!values.ContainsKey((short)record.Tags[i].Value), "Duplicate native tolerance field"); values.Add((short)record.Tags[i].Value, record.Tags[i + 1].Value); }
                    Check(identifiers.OrderBy(x => x).SequenceEqual(values.Keys.OrderBy(x => x)), "Exact sparse field inventory");
                    // Also reject an extra unselected member of the four-field native group.
                    foreach (short id in new short[] { 47, 48, 71, 72 })
                        Equal(identifiers.Contains(id), record.Tags.Any(t => t.Code == 1070 && Equals(t.Value, id)), "Unselected native field appeared");
                    if (v != 3) SameDoubleBits(v == 2 ? minusZero : v == 0 ? .25 : .5, (double)values[48], "Required symmetric lower bits");
                    output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Sparse symmetry reload");
                    var loaded = doc.Entities.Dimensions.Single().StyleOverrides;
                    Equal(v == 0, loaded.ContainsType(DimensionStyleOverrideType.TolerancesDisplayMethod), "Method inheritance");
                    Equal(v != 0, loaded.ContainsType(DimensionStyleOverrideType.TolerancesUpperLimit), "Upper inheritance");
                    Equal(v != 3, loaded.ContainsType(DimensionStyleOverrideType.TolerancesLowerLimit), "Lower projection");
                    Equal(v == 3 ? 1 : 2, loaded.Count, "Loaded sparse override count");
                    Equal(0, doc.Objects.Validate().Count, "Sparse symmetry graph");
                }
                Equal(originalCount, dim.StyleOverrides.Count, "Saving expanded source dictionary");
                SameDoubleBits(v is 1 or 2 ? 999 : .125, style.Tolerances.LowerLimit, "Saving changed inactive source bound");
            });
        }
    }
}
