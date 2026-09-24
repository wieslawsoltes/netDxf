// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
using System.Text.Json;
using netDxf.GTE;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterGVectorSafetyTests()
    {
        RegisterGVectorExtremesTests();
        Run("gvector-safety/null-equality", () =>
        {
            GVector absent = null!;
            var value = new GVector(new[] { 3.0, 4.0 });
            Check(absent == (GVector)null!, "Two null references must compare equal");
            Check(!(absent != (GVector)null!), "Null inequality must complement equality");
            Check(!(value == absent) && !(absent == value), "Exactly one null must differ");
            Check(value != absent && absent != value, "One-null inequality");
            Check(!value.Equals(absent) && !value.Equals((object)null!), "Typed/object null equality");
        });
        foreach (int size in new[] { 0, 1, 2, 3, 16 })
        {
            int n = size;
            Run($"gvector-safety/arithmetic/{n}", () =>
            {
                var a = new GVector(Enumerable.Range(1, n).Select(i => (double)i).ToArray());
                var b = new GVector(Enumerable.Range(1, n).Select(i => (double)(2 * i)).ToArray());
                var copy = new GVector((double[])a.Vector.Clone());
                Check(a == copy && !(a != copy) && a.Equals(copy) && a.Equals((object)copy), "Equal vector values");
                Check(!a.Equals(new GVector(n + 1)), "Different sizes");
                Check(a != (GVector)null!, "Non-null reference");
                var sum = a + b; var difference = b - a; var scaled = 2.0 * a; var divided = b / 2;
                for (int i = 0; i < n; i++)
                {
                    SameDoubleBits(3 * (i + 1), sum[i], "Component sum");
                    SameDoubleBits(i + 1, difference[i], "Component difference");
                    SameDoubleBits(2 * (i + 1), scaled[i], "Scalar product");
                    SameDoubleBits(i + 1, divided[i], "Scalar quotient");
                    SameDoubleBits(i + 1, a![i], "Source mutation");
                }
                SameDoubleBits(2 * Enumerable.Range(1, n).Sum(i => (double)i * i), GVector.Dot(a, b), "Dot product");
                Check(!ReferenceEquals(a, sum) && !ReferenceEquals(a, divided), "New vector results");
                if (n > 0) { copy[n - 1] += 1; Check(a != copy && !(a == copy), "Unequal values"); }
            });
        }
        Run("gvector-safety/null-arithmetic", () =>
        {
            GVector absent = null!; var value = new GVector(new[] { 3.0, 4.0 });
            Check(ReferenceEquals(absent + value, null) && ReferenceEquals(value - absent, null), "Null binary sentinel");
            Check(ReferenceEquals(2 * absent, null) && ReferenceEquals(absent / 2, null), "Null scalar sentinel");
            Check(double.IsNaN(GVector.Dot(absent, value)) && double.IsNaN(GVector.Dot(value, absent)), "Null dot sentinel");
            foreach (bool robust in new[] { false, true })
            {
                Check(double.IsNaN(GVector.Length(absent, robust)), "Null length sentinel");
                Check(double.IsNaN(GVector.Normalize(ref absent, robust)) && ReferenceEquals(absent, null), "Null normalize sentinel");
            }
        });
        var cases = new List<(string Name, double[] Input)>();
        foreach (int exponent in new[] { -1074, -1073, -1068, -1023, -1022, -1000, -500, 0, 100, 500, 1000, 1020 })
        for (int signs = 0; signs < 4; signs++)
        {
            double scale = Math.ScaleB(1.0, exponent);
            cases.Add(($"scaled/{exponent}/{signs}", new[] { (signs % 2 == 0 ? 3.0 : -3.0) * scale,
                (signs < 2 ? 4.0 : -4.0) * scale, 0.0 }));
        }
        cases.Add(("empty", Array.Empty<double>()));
        cases.Add(("zero", new[] { 0.0, BitConverter.Int64BitsToDouble(long.MinValue) }));
        cases.Add(("overflow-length", new[] { double.MaxValue, double.MaxValue }));
        cases.Add(("mixed", new[] { 1e300, -1e-300, 1e299, 0.0 }));
        var observations = new List<object>();
        foreach (var item in cases)
            Run("gvector-safety/norm/" + item.Name, () =>
            {
                var source = new GVector(item.Input); var normalized = source;
                double length = GVector.Length(source, true);
                double original = GVector.Normalize(ref normalized, true);
                SameDoubleBits(length, original, "Length/normalization agreement");
                bool nonzero = item.Input.Any(x => x != 0.0);
                if (nonzero)
                {
                    Check(!ReferenceEquals(source, normalized), "Nonzero normalize must retain its ref-replacement behavior");
                    Check(Math.Abs(GVector.Dot(normalized, normalized) - 1) <= 2e-15, "Normalized vector is not unit length");
                    for (int i = 0; i < source.Size; i++) SameDoubleBits(item.Input[i], source[i], "Input alias was mutated");
                }
                else
                {
                    SameDoubleBits(0, original, "Empty/zero norm");
                    Check(ReferenceEquals(source, normalized), "Zero branch identity");
                    Check(normalized.Vector.All(x => x == 0), "Zero normalized vector");
                }
                string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
                observations.Add(new { name = item.Name, input = item.Input.Select(Number).ToArray(),
                    length = Number(length), normalized = normalized.Vector.Select(Number).ToArray() });
            });
        // Each independent-checker record comes from the real GVector operations above.
        if (string.IsNullOrEmpty(TestFilter) || TestFilter.Contains("gvector-safety", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(ArtifactDirectory, "gvector-norms.json"), JsonSerializer.Serialize(observations));
        Run("gvector-safety/orthonormalize", () =>
        {
            var vectors = new[] { new GVector(new[] { 3.0, 4.0 }), new GVector(new[] { -4.0, 3.0 }) };
            double shortest = GVector.Orthonormalize(2, ref vectors, true);
            Check(Math.Abs(shortest - 5) <= 2e-15, "Gram-Schmidt lengths");
            foreach (var vector in vectors) Check(Math.Abs(GVector.Dot(vector, vector) - 1) <= 2e-15, "Orthonormal basis length");
            Check(Math.Abs(GVector.Dot(vectors[0], vectors[1])) <= 2e-15, "Orthonormal basis dot product");
        });
    }
}
