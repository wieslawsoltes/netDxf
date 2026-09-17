// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.Entities;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void InfiniteNumericalOracle()
    {
        ulong state = 0x524159584C494E45;
        ulong Next()
        { unchecked { state ^= state << 13; state ^= state >> 7; state ^= state << 17; return state; } }
        double Value(int exponent) => BitConverter.Int64BitsToDouble(unchecked((long)
            ((Next() & 0x800fffffffffffffUL) | ((ulong)(1023 + exponent) << 52))));
        string[] Bits(IEnumerable<double> values) => values.Select(v => unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("X16")).ToArray();
        var rows = new List<object>();
        for (int i = 0; i < 512; i++)
        {
            int exponent = (int)(Next() % 1801) - 900;
            var entries = Enumerable.Range(0, 9).Select(_ => Value(exponent)).ToArray();
            var matrix = new Matrix3(entries[0], entries[1], entries[2], entries[3], entries[4], entries[5], entries[6], entries[7], entries[8]);
            var origin = new Vector3(Value((int)(Next() % 101) - 50), Value((int)(Next() % 101) - 50), Value((int)(Next() % 101) - 50));
            var translation = new Vector3(Value(exponent), Value(exponent), Value(exponent));
            var c = new InfiniteCase("seeded", matrix, translation, origin, new(2, -3, 6), Vector3.UnitZ);
            var e = InfiniteEntity((i & 1) == 0, c);
            InfiniteApply(e, matrix, translation, (i & 2) != 0);
            InfiniteUnit(InfiniteDirection(e)); InfiniteUnit(e.Normal);
            rows.Add(new { matrix = Bits(entries), origin = DirectionBits(origin), translation = DirectionBits(translation),
                resultOrigin = DirectionBits(InfiniteOrigin(e)), resultDirection = DirectionBits(InfiniteDirection(e)), resultNormal = DirectionBits(e.Normal) });
        }
        File.WriteAllText(Path.Combine(ArtifactDirectory, "infinite-affine-numerics.json"), JsonSerializer.Serialize(rows));
    }

    private static void InfiniteInvalidUnit(bool ray, bool four, int field, Vector3 value)
    {
        var e = InfiniteEntity(ray, InfiniteCases()[0]);
        (field == 1 ? e.GetType() : typeof(EntityObject))
            .GetField(field == 1 ? "direction" : "normal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(e, value);
        InfiniteReject(e, () => InfiniteApply(e, Matrix3.Identity, Vector3.Zero, four));
    }
}
