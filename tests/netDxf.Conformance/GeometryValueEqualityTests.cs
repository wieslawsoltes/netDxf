// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.GTE;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly ulong[] EqualityBits = {
        0x0000000000000000, 0x8000000000000000, 0x0000000000000001, 0x8000000000000001,
        0x0000000000000002, 0x3ff0000000000000, 0x3ff0000000000001, 0xbff0000000000000,
        0x7fefffffffffffff, 0xffefffffffffffff, 0x7ff0000000000000, 0xfff0000000000000,
        0x7ff8000000000000, 0x7ff8000000001234, 0xfff8000000000000, 0x7ff0000000000001
    };
    private sealed class EqualityVector : GVector { public EqualityVector(double[] values) : base(values) { } }
    private sealed class EqualityMatrix : GMatrix { public EqualityMatrix(double[] values) : base(1, values.Length, values) { } }

    private static void RegisterGeometryValueEqualityTests()
    {
        var observations = new List<object>();
        foreach (bool matrix in new[] { false, true })
        foreach (ulong left in EqualityBits) foreach (ulong right in EqualityBits)
        {
            string kind = matrix ? "matrix" : "vector";
            Run($"geometry-equality/{kind}/{left:x16}/{right:x16}", () =>
            {
                double x = BitConverter.Int64BitsToDouble(unchecked((long)left));
                double y = BitConverter.Int64BitsToDouble(unchecked((long)right));
                bool expected = x.Equals(y), eq, neq, typed, boxed, reverse, defaults;
                int lh, rh, members;
                if (matrix)
                {
                    var a = new GMatrix(1, 3, new[] { 17.0, x, -2.0 });
                    var b = new GMatrix(1, 3, new[] { 17.0, y, -2.0 });
                    eq = a == b; neq = a != b; typed = a.Equals(b); boxed = a.Equals((object)b);
                    reverse = b.Equals(a); defaults = EqualityComparer<GMatrix>.Default.Equals(a, b);
                    lh = a.GetHashCode(); rh = b.GetHashCode(); members = new HashSet<GMatrix> { a, b }.Count;
                    Equal(expected, new Dictionary<GMatrix, int> { [a] = 7 }.ContainsKey(b), "Dictionary lookup");
                    Equal(left, unchecked((ulong)BitConverter.DoubleToInt64Bits(a[1])), "Left source bits");
                    Equal(right, unchecked((ulong)BitConverter.DoubleToInt64Bits(b[1])), "Right source bits");
                }
                else
                {
                    var a = new GVector(new[] { 17.0, x, -2.0 });
                    var b = new GVector(new[] { 17.0, y, -2.0 });
                    eq = a == b; neq = a != b; typed = a.Equals(b); boxed = a.Equals((object)b);
                    reverse = b.Equals(a); defaults = EqualityComparer<GVector>.Default.Equals(a, b);
                    lh = a.GetHashCode(); rh = b.GetHashCode(); members = new HashSet<GVector> { a, b }.Count;
                    Equal(expected, new Dictionary<GVector, int> { [a] = 7 }.ContainsKey(b), "Dictionary lookup");
                    Equal(left, unchecked((ulong)BitConverter.DoubleToInt64Bits(a[1])), "Left source bits");
                    Equal(right, unchecked((ulong)BitConverter.DoubleToInt64Bits(b[1])), "Right source bits");
                }
                foreach (bool answer in new[] { eq, typed, boxed, reverse, defaults }) Equal(expected, answer, "Scalar value equality");
                Equal(!expected, neq, "Complementary inequality");
                Equal(expected ? 1 : 2, members, "HashSet distinct values");
                if (expected) Equal(lh, rh, "Equal values must have equal hashes");
                observations.Add(new { kind, left = left.ToString("x16"), right = right.ToString("x16"),
                    eq, neq, typed, boxed, reverse, defaults, leftHash = lh, rightHash = rh, members });
            });
        }
        if (string.IsNullOrEmpty(TestFilter) || TestFilter == "geometry-equality/")
            File.WriteAllText(Path.Combine(ArtifactDirectory, "geometry-equality.json"), JsonSerializer.Serialize(observations));

        Run("geometry-equality/nulls", () =>
        {
            GMatrix m = null!, n = null!; var matrix = new GMatrix(0, 0);
            Check(m == n && !(m != n), "Null matrices");
            Check(matrix != m && m != matrix && !(matrix == m) && !(m == matrix), "One-null matrices");
            Check(!matrix.Equals(m) && !matrix.Equals((object)null!) && !matrix.Equals(new object()), "Typed/object null matrix");
            GVector v = null!, w = null!; var vector = new GVector(0);
            Check(v == w && !(v != w), "Null vectors");
            Check(vector != v && v != vector && !(vector == v) && !(v == vector), "One-null vectors");
            Check(!vector.Equals(v) && !vector.Equals((object)null!) && !vector.Equals(new object()), "Typed/object null vector");
        });
        foreach (int rows in new[] { 0, 1, 2, 3 }) foreach (int columns in new[] { 0, 1, 2, 3 })
        foreach (bool rowMajor in new[] { false, true })
            Run($"geometry-equality/shape/{rows}/{columns}/{rowMajor}", () =>
            {
                bool previous = GTE.UseRowMajor;
                try
                {
                    GTE.UseRowMajor = rowMajor;
                    var a = new GMatrix(rows, columns); var b = new GMatrix(rows, columns);
                    for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++) a[r, c] = b[r, c] = r * 16 + c;
                    Check(a.Equals(b) && a == b && !(a != b), "Equal shapes and entries");
                    Equal(a.GetHashCode(), b.GetHashCode(), "Shape hash");
                    var otherRows = new GMatrix(rows + 1, columns);
                    var otherColumns = new GMatrix(rows, columns + 1);
                    Check(a != otherRows && a != otherColumns && !a.Equals(otherRows) && !a.Equals(otherColumns), "Different shape is unequal, including empty matrices");
                    if (a.NumElements > 0)
                    {
                        b[b.NumElements - 1] += 1;
                        Check(a != b && !(a == b), "Last differing entry");
                    }
                }
                finally { GTE.UseRowMajor = previous; }
            });
        foreach (int size in new[] { 0, 1, 2, 7, 64 })
            Run($"geometry-equality/vector-size/{size}", () =>
            {
                var a = new GVector(size); var b = new GVector(size);
                Equal(a.GetHashCode(), b.GetHashCode(), "Independent backing-array hashes");
                Check(a.Equals(b) && a != new GVector(size + 1), "Vector size identity");
                if (size > 0)
                {
                    b.Vector[size - 1] = double.Epsilon;
                    Check(a != b, "Subnormal mutation changes equality");
                    b.Vector[size - 1] = -0.0;
                    Check(a == b && a.GetHashCode() == b.GetHashCode(), "Signed-zero mutation restores equality/hash");
                }
            });
        Run("geometry-equality/runtime-types", () =>
        {
            GVector vector = new GVector(new[] { 1.0 });
            GVector derived = new EqualityVector(new[] { 1.0 });
            GVector another = new EqualityVector(new[] { 1.0 });
            Check(vector != derived && derived != vector && !vector.Equals(derived) && !derived.Equals((object)vector), "Runtime vector types agree across equality paths");
            Check(derived == another && derived.Equals((object)another) && derived.GetHashCode() == another.GetHashCode(), "Same derived vector type");
            GMatrix matrix = new GMatrix(1, 1, new[] { 1.0 });
            GMatrix derivedMatrix = new EqualityMatrix(new[] { 1.0 });
            GMatrix anotherMatrix = new EqualityMatrix(new[] { 1.0 });
            Check(matrix != derivedMatrix && derivedMatrix != matrix && !matrix.Equals(derivedMatrix) && !derivedMatrix.Equals((object)matrix), "Runtime matrix types agree across equality paths");
            Check(derivedMatrix == anotherMatrix && derivedMatrix.GetHashCode() == anotherMatrix.GetHashCode(), "Same derived matrix type");
        });
        foreach (double epsilon in new[] { double.Epsilon, 1e-12, 100.0 })
            Run($"geometry-equality/tolerance/{ParameterBits(epsilon)}", () =>
            {
                double previous = MathHelper.Epsilon;
                try
                {
                    MathHelper.Epsilon = epsilon;
                    var a = new GVector(new[] { 0.0 }); var b = new GVector(new[] { double.Epsilon }); var c = new GVector(new[] { 2 * double.Epsilon });
                    Check(a != b && b != c && a != c, "Exact component equivalence independent of geometric tolerance");
                    var ma = new GMatrix(1, 1, a.Vector); var mb = new GMatrix(1, 1, b.Vector);
                    Check(ma != mb, "Exact matrix equivalence");
                }
                finally { MathHelper.Epsilon = previous; }
            });
        Run("geometry-equality/collections-and-arithmetic", () =>
        {
            var a = new GMatrix(2, 2, new[] { 1.0, 2.0, 3.0, 4.0 });
            var b = new GMatrix(2, 2, new[] { 1.0, 2.0, 3.0, 4.0 });
            var copy = new GMatrix(2, 2, (double[])a.Elements.Vector.Clone());
            Check(a + b == 2 * a && b - a == GMatrix.Zero(2, 2), "Matrix arithmetic produces comparable values");
            Check(GMatrix.Transpose(GMatrix.Transpose(a)) == copy, "Transpose equality");
            var values = new Dictionary<GMatrix, string> { [a] = "first" };
            Equal("first", values[b], "Value-key lookup");
            Check(values.Remove(b), "Equal independent key removes entry");
            a[0] = 7; // Mutation is safe after removing the key, never while indexed.
            Check(a != copy && a != b, "Independent construction retained old entries");
            values[a] = "changed";
            Equal("changed", values[new GMatrix(2, 2, new[] { 7.0, 2.0, 3.0, 4.0 })], "Hash recomputed after mutation");
        });
    }
}
