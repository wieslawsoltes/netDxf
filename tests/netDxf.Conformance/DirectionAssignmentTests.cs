// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using DxfAttribute = netDxf.Entities.Attribute;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] DirectionSlotKinds =
    {
        "line-normal", "circle-normal", "arc-normal", "ellipse-normal", "solid-normal", "trace-normal",
        "point-normal", "text-normal", "mtext-normal", "polyline-normal", "ray-normal", "xline-normal",
        "attrib-normal", "attdef-normal", "ray-direction", "xline-direction"
    };
    private static readonly int[] DirectionExponents = { -1074, -1022, -1000, -600, -500, -50, 0, 50, 500, 600, 1000, 1021 };

    private sealed class DirectionSlot
    {
        internal readonly object Owner;
        internal readonly Action<Vector3> Set;
        internal readonly Func<Vector3> Get;
        internal DirectionSlot(object owner, Action<Vector3> set, Func<Vector3> get)
        { Owner = owner; Set = set; Get = get; }
    }

    private static DirectionSlot MakeDirectionSlot(string kind)
    {
        if (kind == "attrib-normal")
        {
            var a = new DxfAttribute(new AttributeDefinition("DIR"));
            return new(a, v => a.Normal = v, () => a.Normal);
        }
        if (kind == "attdef-normal")
        {
            var a = new AttributeDefinition("DIR");
            return new(a, v => a.Normal = v, () => a.Normal);
        }
        if (kind == "ray-direction")
        {
            var a = new Ray(); return new(a, v => a.Direction = v, () => a.Direction);
        }
        if (kind == "xline-direction")
        {
            var a = new XLine(); return new(a, v => a.Direction = v, () => a.Direction);
        }
        EntityObject entity = kind switch
        {
            "line-normal" => new Line(), "circle-normal" => new Circle(), "arc-normal" => new Arc(),
            "ellipse-normal" => new Ellipse(), "solid-normal" => new Solid(), "trace-normal" => new Trace(),
            "point-normal" => new Point(), "text-normal" => new Text(), "mtext-normal" => new MText(),
            "polyline-normal" => new Polyline3D(), "ray-normal" => new Ray(), "xline-normal" => new XLine(),
            _ => throw new ArgumentException(kind)
        };
        return new(entity, v => entity.Normal = v, () => entity.Normal);
    }

    private static List<Vector3> ValidDirections()
    {
        var result = new List<Vector3>();
        foreach (int exponent in DirectionExponents)
        {
            double s = Math.ScaleB(1.0, exponent);
            result.Add(new(s, -2 * s, 3 * s));
            result.Add(new(3 * s, s, -2 * s));
            result.Add(new(-2 * s, 3 * s, s));
        }
        result.Add(new(double.MaxValue, -double.Epsilon, 0));
        result.Add(new(0, -double.MaxValue, double.MaxValue / 2));
        result.Add(new(-0.0, double.Epsilon, -0.0));
        result.Add(new(-double.Epsilon, 0, -0.0));
        result.Add(new(0, -0.0, -double.MaxValue));
        result.Add(new(double.MaxValue, double.MaxValue, double.MaxValue));
        return result;
    }

    private static List<Vector3> InvalidDirections()
    {
        var result = new List<Vector3>();
        for (int mask = 0; mask < 8; mask++)
            result.Add(new((mask & 1) == 0 ? 0.0 : -0.0, (mask & 2) == 0 ? 0.0 : -0.0, (mask & 4) == 0 ? 0.0 : -0.0));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            result.Add(new(bad, 2, -3)); result.Add(new(2, bad, -3)); result.Add(new(2, -3, bad));
        }
        result.Add(new(double.NaN, double.PositiveInfinity, double.NegativeInfinity));
        result.Add(Vector3.Normalize(new Vector3(double.MaxValue, double.MaxValue, double.MaxValue)));
        return result;
    }

    private static string[] DirectionBits(Vector3 value) => new[] { value.X, value.Y, value.Z }
        .Select(v => unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("X16")).ToArray();

    private static void CheckUnitDirection(Vector3 input, Vector3 result)
    {
        Check(double.IsFinite(result.X) && double.IsFinite(result.Y) && double.IsFinite(result.Z), "Nonfinite direction");
        double scale = Math.Max(Math.Abs(input.X), Math.Max(Math.Abs(input.Y), Math.Abs(input.Z)));
        var source = new Vector3(input.X / scale, input.Y / scale, input.Z / scale);
        Check(Math.Abs(Vector3.DotProduct(result, result) - 1) <= 2e-15, "Direction is not unit length");
        Check(Vector3.DotProduct(source, result) > 0, "Direction reversed");
        Check(Vector3.CrossProduct(source, result).Modulus() <= 3e-15, "Direction changed");
    }

    private static void DirectionAccept(string kind, Vector3 input)
    {
        var slot = MakeDirectionSlot(kind);
        slot.Set(input); CheckUnitDirection(input, slot.Get());
        var bits = DirectionBits(slot.Get());
        for (int i = 0; i < 16; i++) slot.Set(slot.Get());
        Check(bits.SequenceEqual(DirectionBits(slot.Get())), "Repeated assignment drifted");
        object copy = ((ICloneable)slot.Owner).Clone();
        string property = kind.EndsWith("-direction", StringComparison.Ordinal) ? "Direction" : "Normal";
        var copied = (Vector3)(copy.GetType().GetProperty(property)!.GetValue(copy) ?? throw new InvalidOperationException());
        Check(bits.SequenceEqual(DirectionBits(copied)), "Clone direction drifted");
    }

    private static void DirectionReject(string kind, Vector3 input)
    {
        var slot = MakeDirectionSlot(kind); slot.Set(new(2, -3, 6));
        var bits = DirectionBits(slot.Get());
        byte[] proxy = { 1, 7, 19, 33 };
        if (slot.Owner is EntityObject entity) entity.ProxyGraphics = proxy;
        if (slot.Owner is DxfAttribute attribute) attribute.ProxyGraphics = proxy;
        if (slot.Owner is AttributeDefinition definition) definition.ProxyGraphics = proxy;
        ArgumentException? failure = null;
        try { slot.Set(input); } catch (ArgumentException ex) { failure = ex; }
        Check(failure != null, "Invalid direction was accepted");
        Equal("value", failure!.ParamName, "Setter argument name");
        Check(bits.SequenceEqual(DirectionBits(slot.Get())), "Rejected assignment changed direction bits");
        byte[]? after = slot.Owner switch
        {
            EntityObject e => e.ProxyGraphics, DxfAttribute a => a.ProxyGraphics,
            AttributeDefinition a => a.ProxyGraphics, _ => throw new InvalidOperationException()
        };
        Check(after != null && proxy.SequenceEqual(after), "Rejected assignment changed proxy graphics");
    }

    private static void DirectionConstructor(bool ray, bool two, Vector3 input, bool valid)
    {
        object? item = null; ArgumentException? failure = null;
        try
        {
            item = ray
                ? two ? new Ray(Vector2.Zero, new Vector2(input.X, input.Y)) : new Ray(Vector3.Zero, input)
                : two ? new XLine(Vector2.Zero, new Vector2(input.X, input.Y)) : new XLine(Vector3.Zero, input);
        }
        catch (ArgumentException ex) { failure = ex; }
        if (valid)
        {
            Check(failure == null, "Valid constructor direction rejected: " + failure);
            Vector3 direction = item is Ray r ? r.Direction : ((XLine)item!).Direction;
            CheckUnitDirection(two ? new(input.X, input.Y, 0) : input, direction);
        }
        else
        {
            Check(failure != null, "Invalid constructor direction accepted");
            Equal("direction", failure!.ParamName, "Constructor argument name");
        }
    }

    private static void DirectionEpsilon(string kind, double epsilon)
    {
        var slot = MakeDirectionSlot(kind); double old = MathHelper.Epsilon;
        try
        {
            MathHelper.Epsilon = epsilon;
            foreach (Vector3 value in ValidDirections())
            { slot.Set(value); CheckUnitDirection(value, slot.Get()); }
        }
        finally { MathHelper.Epsilon = old; }
    }

    private static void DirectionLegacy(string kind)
    {
        var slot = MakeDirectionSlot(kind); var random = new Random(624713);
        for (int i = 0; i < 512; i++)
        {
            var input = new Vector3(random.NextDouble() - .5, random.NextDouble() - .5, random.NextDouble() - .5);
            var expected = Vector3.Normalize(input);
            slot.Set(input);
            Check(DirectionBits(expected).SequenceEqual(DirectionBits(slot.Get())), "Ordinary legacy output bits changed");
        }
    }

    private static void DirectionWire(DxfVersion version, bool binary, int index)
    {
        Vector3 value = ValidDirections()[index];
        var document = new DxfDocument(version);
        var line = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)) { Normal = value, Thickness = 2 };
        var ray = new Ray(new Vector3(7, 8, 9), value);
        var xline = new XLine(new Vector3(-1, -2, -3), value);
        var definition = new AttributeDefinition("DIR") { Value = "definition", Normal = value };
        var block = new Block("DIRECTION_BLOCK"); block.AttributeDefinitions.Add(definition);
        var insert = new Insert(block); document.Entities.Add(new EntityObject[] { line, ray, xline, insert });
        var attribute = insert.Attributes.Single(); attribute.Normal = value; attribute.Value = "attribute";
        using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Direction wire save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"direction-assignment-{version}-{binary}-{index}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Direction wire load failed");
        foreach (Vector3 actual in new[] { loaded.Entities.Lines.Single().Normal, loaded.Entities.Rays.Single().Direction,
            loaded.Entities.XLines.Single().Direction, loaded.Entities.Inserts.Single().Attributes.Single().Normal,
            loaded.Blocks["DIRECTION_BLOCK"].AttributeDefinitions["DIR"].Normal }) CheckUnitDirection(value, actual);
        CheckUnitDirection(value, line.Normal); CheckUnitDirection(value, attribute.Normal);
    }

    private static void DirectionNumericalOracle()
    {
        ulong state = 0x6e65744478664E31;
        ulong Next()
        { unchecked { state ^= state << 13; state ^= state >> 7; state ^= state << 17; return state; } }
        double NextDouble()
        {
            ulong bits = Next();
            if ((bits & 0x7ff0000000000000) == 0x7ff0000000000000) bits ^= 0x0010000000000000;
            return BitConverter.Int64BitsToDouble(unchecked((long)bits));
        }
        var rows = new List<object>();
        for (int i = 0; i < 512; i++)
        {
            Vector3 input = new(NextDouble(), NextDouble(), NextDouble());
            var slot = MakeDirectionSlot("line-normal"); slot.Set(input); CheckUnitDirection(input, slot.Get());
            rows.Add(new { input = DirectionBits(input), result = DirectionBits(slot.Get()) });
        }
        File.WriteAllText(Path.Combine(ArtifactDirectory, "direction-assignment-numerics.json"), JsonSerializer.Serialize(rows));
    }

    private static void RegisterDirectionAssignmentTests()
    {
        var valid = ValidDirections(); var invalid = InvalidDirections();
        foreach (string kind in DirectionSlotKinds)
        {
            for (int i = 0; i < valid.Count; i++)
            { int n = i; Run($"direction-assignment/{kind}/valid/{n}", () => DirectionAccept(kind, valid[n])); }
            for (int i = 0; i < invalid.Count; i++)
            { int n = i; Run($"direction-assignment/{kind}/reject/{n}", () => DirectionReject(kind, invalid[n])); }
            foreach (double epsilon in new[] { 1e-12, 1.0, 100.0 })
                Run($"direction-assignment/{kind}/epsilon/{epsilon:R}", () => DirectionEpsilon(kind, epsilon));
            Run($"direction-assignment/{kind}/legacy-bits", () => DirectionLegacy(kind));
        }
        foreach (bool ray in new[] { false, true }) foreach (bool two in new[] { false, true })
        {
            for (int i = 0; i < valid.Count; i++)
            {
                int n = i; Vector3 v = valid[n];
                bool accepted = !two || v.X != 0 || v.Y != 0;
                Run($"direction-assignment/constructor/{ray}/{two}/valid/{n}", () => DirectionConstructor(ray, two, v, accepted));
            }
            for (int i = 0; i < invalid.Count; i++)
            {
                int n = i; Vector3 v = invalid[n];
                bool accepted = two && double.IsFinite(v.X) && double.IsFinite(v.Y) && (v.X != 0 || v.Y != 0);
                Run($"direction-assignment/constructor/{ray}/{two}/reject/{n}", () => DirectionConstructor(ray, two, v, accepted));
            }
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            for (int i = 0; i < valid.Count; i++)
            { int n = i; Run($"direction-assignment/wire/{version}/{binary}/{n}", () => DirectionWire(version, binary, n)); }
        Run("direction-assignment/numerical-oracle", DirectionNumericalOracle);
    }
}
