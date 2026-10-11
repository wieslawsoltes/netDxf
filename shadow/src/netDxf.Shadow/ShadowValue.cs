using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow;

/// <summary>Session-stable identity. DXF handles use H:, other source objects use O:.</summary>
public readonly record struct ObjectId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>A closed, detached value algebra. No mutable source or graphics object can occur here.</summary>
public abstract record ShadowValue
{
    private ShadowValue() { }
    public sealed record NullValue : ShadowValue;
    // Invariant text retains UInt64/Decimal precision and floating-point round-trip representations.
    public sealed record Atom(string TypeName, string Text) : ShadowValue;
    public sealed record Reference(ObjectId Id) : ShadowValue;
    public sealed record Sequence(ImmutableArray<ShadowValue> Items) : ShadowValue;
    public sealed record Structure(string TypeName, ImmutableDictionary<string, ShadowValue> Fields) : ShadowValue;
    public sealed record Fault(string Message) : ShadowValue;

    public static ShadowValue Null { get; } = new NullValue();
    public static ShadowValue From(string value) => new Atom(typeof(string).FullName!, value);
    public static ShadowValue From(double value) => new Atom(typeof(double).FullName!, value.ToString("R", CultureInfo.InvariantCulture));
    public static ShadowValue From(bool value) => new Atom(typeof(bool).FullName!, value ? "True" : "False");
    public static ShadowValue From(Vec3 value, bool twoDimensional = false)
    {
        var fields = ImmutableDictionary<string, ShadowValue>.Empty.Add("X", From(value.X)).Add("Y", From(value.Y));
        if (!twoDimensional) fields = fields.Add("Z", From(value.Z));
        return new Structure(twoDimensional ? "netDxf.Vector2" : "netDxf.Vector3", fields);
    }

    public static bool Equivalent(ShadowValue? left, ShadowValue? right)
    {
        if (ReferenceEquals(left, right)) return true;
        return (left, right) switch
        {
            (NullValue, NullValue) => true,
            (Atom a, Atom b) => a.TypeName == b.TypeName && a.Text == b.Text,
            (Reference a, Reference b) => a.Id == b.Id,
            (Fault a, Fault b) => a.Message == b.Message,
            (Sequence a, Sequence b) => a.Items.Length == b.Items.Length && a.Items.Zip(b.Items).All(p => Equivalent(p.First, p.Second)),
            (Structure a, Structure b) => a.TypeName == b.TypeName && Equivalent(a.Fields, b.Fields),
            _ => false
        };
    }

    public static bool Equivalent(ImmutableDictionary<string, ShadowValue> left, ImmutableDictionary<string, ShadowValue> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        foreach (var (key, value) in left)
            if (!right.TryGetValue(key, out var other) || !Equivalent(value, other)) return false;
        return true;
    }

    public IEnumerable<ObjectId> References()
    {
        var stack = new Stack<ShadowValue>();
        stack.Push(this);
        while (stack.TryPop(out var value))
        {
            switch (value)
            {
                case Reference reference: yield return reference.Id; break;
                case Sequence sequence:
                    foreach (var item in sequence.Items) stack.Push(item);
                    break;
                case Structure structure:
                    foreach (var item in structure.Fields.Values) stack.Push(item);
                    break;
            }
        }
    }
}

[Flags]
public enum ShadowObjectKind { Object = 0, Entity = 1, Table = 2, Block = 4, Layout = 8, Document = 16 }
public enum DiagnosticSeverity { Information, Warning, Error }
public sealed record ShadowDiagnostic(string Code, string Message, ObjectId? Object = null,
    DiagnosticSeverity Severity = DiagnosticSeverity.Warning);

public sealed record ShadowObject(ObjectId Id, string TypeName, ShadowObjectKind Kind,
    ImmutableDictionary<string, ShadowValue> Properties, ImmutableHashSet<string> WritableProperties)
{
    public string Type => TypeName[(TypeName.LastIndexOf('.') + 1)..];
    public ShadowValue this[string property] => Properties.GetValueOrDefault(property, ShadowValue.Null);
}
