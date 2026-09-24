// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf.GTE;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static IEnumerable<(string Name, double[] Left, double[] Right)> OrderingInputs()
    {
        foreach (int prefix in new[] { 0, 3 })
        for (int a = 0; a < EqualityBits.Length; a++) for (int b = 0; b < EqualityBits.Length; b++)
        {
            double x = BitConverter.Int64BitsToDouble(unchecked((long)EqualityBits[a]));
            double y = BitConverter.Int64BitsToDouble(unchecked((long)EqualityBits[b]));
            var before = Enumerable.Repeat(17.0, prefix).ToArray();
            // Opposing suffixes catch componentwise comparisons and equal-prefix mistakes.
            yield return ($"bits/{prefix}/{a}/{b}", before.Concat(new[] { x, 5.0 }).ToArray(),
                before.Concat(new[] { y, -5.0 }).ToArray());
        }
        foreach (int size in new[] { 0, 1, 2, 3, 7, 16 }) for (int seed = 0; seed < 4; seed++)
        {
            double[] a = Enumerable.Range(0, size).Select(i => (double)((i * 17 + seed * 13) % 29 - 14)).ToArray();
            double[] b = (double[])a.Clone();
            if (size > 0 && seed != 0) b[(seed - 1) % size] += seed % 2 == 0 ? -0.125 : 0.125;
            yield return ($"finite/{size}/{seed}", a, b);
        }
        yield return ("crossed", new[] { 1.0, 100.0 }, new[] { 2.0, -100.0 });
        yield return ("last", new[] { 1.0, 2.0, -1.0 }, new[] { 1.0, 2.0, 1.0 });
        yield return ("zero-tie", new[] { 0.0, -0.0 }, new[] { -0.0, 0.0 });
        yield return ("nan-tie", new[] { double.NaN, 0.0 },
            new[] { BitConverter.Int64BitsToDouble(0x7ff8000000001234L), -0.0 });
    }

    // Independent scalar branches, rather than calling the production comparator.
    private static int OrderingReference(double[] a, double[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (double.IsNaN(a[i])) { if (!double.IsNaN(b[i])) return -1; }
            else if (double.IsNaN(b[i])) return 1;
            else if (a[i] < b[i]) return -1;
            else if (a[i] > b[i]) return 1;
        }
        return 0;
    }

    private static bool[] OrderingVector(GVector a, GVector b) => new[] { a < b, a <= b, a > b, a >= b, a == b, a != b };
    private static bool[] OrderingMatrix(GMatrix a, GMatrix b) => new[] { a < b, a <= b, a > b, a >= b, a == b, a != b };
    private static string[] OrderingBits(double[] values) => values.Select(x => unchecked((ulong)BitConverter.DoubleToInt64Bits(x)).ToString("x16")).ToArray();
    private static bool[] OrderingFlags(int order) => new[] { order < 0, order <= 0, order > 0, order >= 0, order == 0, order != 0 };

    private static void RegisterGeometryOrderingTests()
    {
        var observations = new List<object>();
        foreach (var item in OrderingInputs())
            Run("geometry-order/" + item.Name, () =>
            {
                var a = new GVector(item.Left); var b = new GVector(item.Right);
                var left = OrderingBits(a.Vector); var right = OrderingBits(b.Vector);
                var expected = OrderingFlags(OrderingReference(item.Left, item.Right));
                bool[] vector = OrderingVector(a, b), reverse = OrderingVector(b, a);
                var rowA = new GMatrix(1, a.Size, a.Vector); var rowB = new GMatrix(1, b.Size, b.Vector);
                var colA = new GMatrix(a.Size, 1, a.Vector); var colB = new GMatrix(b.Size, 1, b.Vector);
                bool[] row = OrderingMatrix(rowA, rowB), column = OrderingMatrix(colA, colB);
                foreach (var actual in new[] { vector, row, column }) Check(expected.SequenceEqual(actual), "Lexicographic relation/equality");
                Check(OrderingFlags(-OrderingReference(item.Left, item.Right)).SequenceEqual(reverse), "Reverse relation");
                Check(OrderingFlags(0).SequenceEqual(OrderingVector(a, a)), "Same vector reflexivity");
                Check(OrderingFlags(0).SequenceEqual(OrderingMatrix(rowA, rowA)), "Same matrix reflexivity");
                bool unchanged = left.SequenceEqual(OrderingBits(a.Vector)) && right.SequenceEqual(OrderingBits(b.Vector))
                    && left.SequenceEqual(OrderingBits(rowA.Elements.Vector)) && right.SequenceEqual(OrderingBits(rowB.Elements.Vector))
                    && left.SequenceEqual(OrderingBits(colA.Elements.Vector)) && right.SequenceEqual(OrderingBits(colB.Elements.Vector));
                Check(unchanged, "Comparisons mutated exact source bits");
                observations.Add(new { name = item.Name, left, right, vector, reverse, row, column, unchanged });
            });
        if (string.IsNullOrEmpty(TestFilter) || TestFilter == "geometry-order/")
            File.WriteAllText(Path.Combine(ArtifactDirectory, "geometry-order.json"), JsonSerializer.Serialize(observations));

        for (int size = 0; size < 4; size++)
        {
            int n = size;
            Run($"geometry-order/different-sizes/{n}", () =>
            {
                var a = new GVector(n); var b = new GVector(n + 1);
                foreach (var result in new[] { OrderingVector(a, b), OrderingVector(b, a),
                    OrderingMatrix(new GMatrix(1, n), new GMatrix(1, n + 1)),
                    OrderingMatrix(new GMatrix(n, 1), new GMatrix(n + 1, 1)) })
                    Check(new[] { false, false, false, false, false, true }.SequenceEqual(result), "Different-size restriction changed");
            });
        }
        foreach (bool rowMajor in new[] { false, true })
            Run($"geometry-order/matrix-storage/{rowMajor}", () =>
            {
                bool old = GTE.UseRowMajor;
                try
                {
                    GTE.UseRowMajor = rowMajor;
                    var a = new GMatrix(2, 3); var b = new GMatrix(2, 3);
                    a[0, 1] = 1; b[1, 0] = 1;
                    Check(OrderingFlags(rowMajor ? 1 : -1).SequenceEqual(OrderingMatrix(a, b)), "Stored-entry lexicographic order");
                    var different = new GMatrix(3, 2, a.Elements.Vector);
                    Check(new[] { false, false, false, false, false, true }.SequenceEqual(OrderingMatrix(a, different)), "Same-length, different-shape restriction");
                }
                finally { GTE.UseRowMajor = old; }
            });
        foreach (bool matrix in new[] { false, true })
            Run($"geometry-order/nulls/{matrix}", () =>
            {
                if (matrix)
                {
                    var value = new GMatrix(0, 0); GMatrix absent = null!;
                    foreach (Func<bool> call in new Func<bool>[] { () => value < absent, () => absent < value,
                        () => value <= absent, () => absent <= value, () => value > absent, () => absent > value,
                        () => value >= absent, () => absent >= value }) Throws<NullReferenceException>(() => call());
                }
                else
                {
                    var value = new GVector(0); GVector absent = null!;
                    foreach (Func<bool> call in new Func<bool>[] { () => value < absent, () => absent < value,
                        () => value <= absent, () => absent <= value, () => value > absent, () => absent > value,
                        () => value >= absent, () => absent >= value }) Throws<NullReferenceException>(() => call());
                }
            });
        Run("geometry-order/order-laws-and-sorted-keys", () =>
        {
            double[] scalars = { double.NaN, double.NegativeInfinity, -double.Epsilon, -0.0, 0.0, double.Epsilon, double.PositiveInfinity };
            var values = scalars.SelectMany(x => new[] { new GVector(new[] { x, 1.0 }), new GVector(new[] { x, -1.0 }) }).ToArray();
            foreach (var a in values) foreach (var b in values) foreach (var c in values)
            {
                if (a < b && b < c) Check(a < c, "Strict transitivity");
                if (a <= b && b <= c) Check(a <= c, "Non-strict transitivity");
                Equal(a == b, a <= b && b <= a, "Antisymmetry/value equality");
            }
            var comparer = Comparer<GVector>.Create((a, b) => a < b ? -1 : a > b ? 1 : 0);
            var set = new SortedSet<GVector>(values, comparer);
            Equal(12, set.Count, "Signed-zero ties and NaN ordered keys");
            foreach (var value in values) Check(set.Contains(new GVector(value.Vector)), "Equivalent independent ordered key");
            var ordered = set.ToArray();
            for (int i = 1; i < ordered.Length; i++) Check(ordered[i - 1] < ordered[i], "Sorted iteration");
        });
    }
}
