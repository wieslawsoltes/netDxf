// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
using System.Text.Json;
using netDxf.GTE;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static string[] ExtremesBits(IEnumerable<double> values) => values.Select(v =>
        unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("x16", CultureInfo.InvariantCulture)).ToArray();

    private static void RegisterGVectorExtremesTests()
    {
        var specs = new List<(string Name, double[][] Values, bool Shared)>();
        foreach (int size in new[] { 0, 1, 2, 3, 7, 16 })
        foreach (int count in new[] { 1, 2, 5 })
        for (int seed = 0; seed < 3; seed++)
        {
            double[][] values = Enumerable.Range(0, count).Select(r => Enumerable.Range(0, size)
                .Select(c => (double)(((r + 3) * (c + 5) * 17 + seed * 11) % 43 - 21)
                    + ((r + c) % 4) * 0.125).ToArray()).ToArray();
            specs.Add(($"finite/{size}/{count}/{seed}", values, false));
        }
        double[][] points = { new[] { 3.0, 5.0 }, new[] { -2.0, 7.0 }, new[] { 9.0, -4.0 } };
        int[][] orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        for (int i = 0; i < orders.Length; i++)
            specs.Add(($"permutation/{i}", orders[i].Select(j => points[j]).ToArray(), false));
        specs.Add(("shared", new[] { new[] { 2.0, -3.0 }, new[] { 2.0, -3.0 }, new[] { 2.0, -3.0 } }, true));
        ulong[] patterns = { 0xfff0000000000000UL, 0xffefffffffffffffUL, 0x8000000000000001UL,
            0x8000000000000000UL, 0UL, 1UL, 0x7fefffffffffffffUL, 0x7ff0000000000000UL,
            0x7ff8000000000001UL, 0xfff8000000000042UL };
        for (int a = 0; a < patterns.Length; a++) for (int b = 0; b < patterns.Length; b++)
        {
            double x = BitConverter.Int64BitsToDouble(unchecked((long)patterns[a]));
            double y = BitConverter.Int64BitsToDouble(unchecked((long)patterns[b]));
            specs.Add(($"binary64/{a}/{b}", new[] { new[] { x, y }, new[] { y, x } }, false));
        }
        var observations = new List<object>();
        foreach (var spec in specs)
            Run("gvector-extremes/" + spec.Name, () =>
            {
                int count = spec.Values.Length, size = spec.Values[0].Length;
                // Only the requested prefix participates; the tail is deliberately unsuitable.
                var input = spec.Values.Select(row => new GVector(row))
                    .Concat(new GVector[] { new GVector(new[] { double.MaxValue }), null! }).ToArray();
                if (spec.Shared) for (int i = 1; i < count; i++) input[i] = input[0];
                var references = (GVector[])input.Clone();
                var before = input.Take(count + 1).Select(v => ExtremesBits(v.Vector)).ToArray();
                bool SourceUnchanged() => Enumerable.Range(0, count + 1).All(i =>
                    ReferenceEquals(input[i], references[i]) && before[i].SequenceEqual(ExtremesBits(input[i].Vector)))
                    && ReferenceEquals(input[count + 1], null);

                Check(GVector.ComputeExtremes(count, input, out var minimum, out var maximum), "Valid prefix rejected");
                bool sourceUnchanged = SourceUnchanged();
                bool objectsDetached = !ReferenceEquals(minimum, maximum)
                    && input.All(v => !ReferenceEquals(v, minimum) && !ReferenceEquals(v, maximum));
                bool buffersDetached = !ReferenceEquals(minimum.Vector, maximum.Vector)
                    && input.Take(count + 1).All(v => !ReferenceEquals(v.Vector, minimum.Vector)
                        && !ReferenceEquals(v.Vector, maximum.Vector));
                Check(sourceUnchanged, "Computing bounds mutated input");
                Check(objectsDetached && buffersDetached, "Bounds must own independent objects and buffers");
                Equal(size, minimum.Size, "Minimum dimension"); Equal(size, maximum.Size, "Maximum dimension");
                // Stable ordering supplies the first extremal value, retaining zero-sign ties.
                // A NaN in the first row remains the seed; later NaNs remain ignored.
                for (int c = 0; c < size; c++)
                {
                    double first = spec.Values[0][c];
                    var column = spec.Values.Select(row => row[c]).Where(v => !double.IsNaN(v)).ToArray();
                    double low = double.IsNaN(first) ? first : column.OrderBy(v => v).First();
                    double high = double.IsNaN(first) ? first : column.OrderByDescending(v => v).First();
                    SameDoubleBits(low, minimum[c], "Minimum coordinate");
                    SameDoubleBits(high, maximum[c], "Maximum coordinate");
                }
                if (spec.Name.StartsWith("permutation/", StringComparison.Ordinal))
                {
                    Check(ExtremesBits(minimum.Vector).SequenceEqual(ExtremesBits(new[] { -2.0, -4.0 })), "Golden minimum");
                    Check(ExtremesBits(maximum.Vector).SequenceEqual(ExtremesBits(new[] { 9.0, 7.0 })), "Golden maximum");
                }
                string[] minimumBits = ExtremesBits(minimum.Vector), maximumBits = ExtremesBits(maximum.Vector);
                Check(GVector.ComputeExtremes(count, input, out var againMin, out var againMax), "Repeated bounds failed");
                bool repeat = minimumBits.SequenceEqual(ExtremesBits(againMin.Vector))
                    && maximumBits.SequenceEqual(ExtremesBits(againMax.Vector)) && SourceUnchanged()
                    && !ReferenceEquals(minimum, againMin) && !ReferenceEquals(maximum, againMax);
                Check(repeat, "Repeated bounds changed values or reused mutable outputs");
                bool isolation = true;
                if (size > 0)
                {
                    minimum[0] = 12345;
                    isolation &= maximumBits.SequenceEqual(ExtremesBits(maximum.Vector)) && SourceUnchanged();
                    maximum[0] = -12345;
                    isolation &= minimum[0] == 12345 && SourceUnchanged();
                    input[0][0] = 87654;
                    isolation &= minimum[0] == 12345 && maximum[0] == -12345
                        && minimumBits.SequenceEqual(ExtremesBits(againMin.Vector))
                        && maximumBits.SequenceEqual(ExtremesBits(againMax.Vector));
                }
                Check(isolation, "Mutating an input or output changed another bound");
                observations.Add(new { name = spec.Name, count,
                    input = before.Take(count).ToArray(), minimum = minimumBits, maximum = maximumBits,
                    sourceUnchanged, objectsDetached, buffersDetached, repeat, isolation });
            });
        if (string.IsNullOrEmpty(TestFilter) || TestFilter.Contains("gvector-extremes", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(ArtifactDirectory, "gvector-extremes.json"),
                JsonSerializer.Serialize(new { schema = 1, observations }));
    }
}
