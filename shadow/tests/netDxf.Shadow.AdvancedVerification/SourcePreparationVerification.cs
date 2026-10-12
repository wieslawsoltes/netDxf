using System.Collections;
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;

/// <summary>
/// White-box metadata/index tests without exposing mutable adapter internals through
/// the public API or introducing an InternalsVisibleTo assembly-wide exception.
/// </summary>
internal static class SourcePreparationVerification
{
    private static readonly Type Accessors = typeof(ShadowSession).Assembly.GetType(
        "netDxf.Shadow.NetDxf.SourceAccessors", throwOnError: true)!;
    private static readonly MethodInfo ForMethod = Accessors.GetMethod("For", BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo FindMethod = Accessors.GetMethod("Find", BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo IndexMethod = typeof(ShadowSession).GetMethod(
        "IndexOwnedReferences", BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("accessors/ordinal lookup shares the deterministic enumeration metadata", MetadataIdentity));
        tests.Add(("accessors/nearest declaration wins and indexers remain excluded", HiddenMembers));
        tests.Add(("accessors/read-only metadata rejects writes", ReadOnlyMembers));
        tests.Add(("accessors/boxed value-type fields and properties write through", BoxedValue));
        tests.Add(("accessors/concurrent first access publishes one shared metadata table", ConcurrentLookup));
        tests.Add(("accessors/drawing entity aliases remain filtered", DrawingAliases));
        tests.Add(("ownership/index agrees with the previous nested scan including candidate order", IndexMatchesLegacy));
        tests.Add(("ownership/empty additions yield no candidates", EmptyIndex));
        foreach (var count in new[] { 16, 64, 256 })
            tests.Add(($"ownership/{count} entities/index visits each input node once", () => SinglePass(count)));
    }

    private static void MetadataIdentity()
    {
        var ordered = Ordered(typeof(Line));
        var names = ordered.Select(Name).ToArray();
        Require(names.SequenceEqual(names.Order(StringComparer.Ordinal)));
        Require(names.Distinct(StringComparer.Ordinal).Count() == names.Length);
        foreach (var accessor in ordered)
            Require(ReferenceEquals(accessor, Find(typeof(Line), Name(accessor))));
        Require(Find(typeof(Line), "StartPoint") is not null);
        Require(Find(typeof(Line), "startpoint") is null);
        Require(Find(typeof(Line), "NonexistentProperty") is null);
        Require(Find(typeof(Line), string.Empty) is null);
    }

    private static void HiddenMembers()
    {
        var source = new MetadataDerived();
        var accessor = Find(typeof(MetadataDerived), "Value")!;
        Require((Type)Member(accessor, "ValueType") == typeof(string));
        Require((string)Get(accessor)(source)! == "derived");
        Set(accessor)(source, "updated");
        Require(source.Value == "updated");
        Require(((MetadataBase)source).Value == 7);
        Require(Find(typeof(MetadataDerived), "Item") is null);
        Require(Find(typeof(MetadataDerived), "WriteOnly") is null);
    }

    private static void ReadOnlyMembers()
    {
        var source = new MetadataDerived();
        foreach (var name in new[] { "ReadOnly", "ConstantField" })
        {
            var accessor = Find(typeof(MetadataDerived), name)!;
            Require(!(bool)Member(accessor, "CanWrite"));
            Expect<InvalidOperationException>(() => Set(accessor)(source, 99));
        }
        Require(source.ReadOnly == 42 && source.ConstantField == 11);
    }

    private static void BoxedValue()
    {
        object boxed = new ValueCarrier { Coordinate = 1, Field = 2 };
        var coordinate = Find(typeof(ValueCarrier), "Coordinate")!;
        var field = Find(typeof(ValueCarrier), "Field")!;
        Set(coordinate)(boxed, 7);
        Set(field)(boxed, 9);
        var value = (ValueCarrier)boxed;
        Require(value.Coordinate == 7 && value.Field == 9);
        Require((int)Get(coordinate)(boxed)! == 7);
        Require((int)Get(field)(boxed)! == 9);
    }

    private static void ConcurrentLookup()
    {
        // A distinct type keeps the first metadata access inside the parallel region.
        var arrays = new object[128];
        var entries = new object[128];
        Parallel.For(0, arrays.Length, i =>
        {
            arrays[i] = ForMethod.Invoke(null, [typeof(ConcurrentCarrier)])!;
            entries[i] = Find(typeof(ConcurrentCarrier), "Value")!;
        });
        Require(arrays.All(value => ReferenceEquals(value, arrays[0])));
        Require(entries.All(value => ReferenceEquals(value, entries[0])));
        Require(ReferenceEquals(Ordered(typeof(ConcurrentCarrier)).Single(), entries[0]));
    }

    private static void DrawingAliases()
    {
        var document = new DxfDocument();
        var type = document.Entities.GetType();
        Require(Ordered(type).Select(Name).SequenceEqual(new[] { "ActiveLayout", "All" }));
        Require(Find(type, "Lines") is null && Find(type, "Circles") is null);
    }

    private static void IndexMatchesLegacy()
    {
        var document = new DxfDocument();
        var block = new Block("OwnershipIndex");
        block.Entities.Add(new Polyline2D([Vector2.Zero, new Vector2(10, 0), new Vector2(10, 10)]));
        document.Blocks.Add(block);
        document.Entities.Add(new Insert(block, Vector2.Zero));
        document.Entities.Add(new Line(Vector2.Zero, new Vector2(20, 0)));
        var additions = new ShadowSession(document).Current.Objects.ToDictionary(pair => pair.Key, pair => pair.Value);
        var actual = BuildIndex(additions);
        var expectedKeys = 0;
        var totalCandidates = 0;
        foreach (var target in additions.Keys)
        {
            var expected = LegacyCandidates(additions, target);
            if (expected.Count == 0)
            {
                Require(!actual.Contains(target));
                continue;
            }
            expectedKeys++;
            var candidates = (IList)actual[target]!;
            Require(candidates.Count == expected.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                var candidate = candidates[i]!;
                var owner = (ObjectId)candidate.GetType().GetField("Item1")!.GetValue(candidate)!;
                var accessor = candidate.GetType().GetField("Item2")!.GetValue(candidate)!;
                Require(owner == expected[i].Owner);
                Require(ReferenceEquals(accessor, expected[i].Accessor), "Candidate priority/metadata changed.");
                totalCandidates++;
            }
        }
        Require(totalCandidates > 0, "The fixture must exercise actual owned references.");
        Require(actual.Count == expectedKeys);
    }

    // Deliberately retain the previous search strategy as the independent oracle.
    private static List<(ObjectId Owner, object Accessor)> LegacyCandidates(
        IReadOnlyDictionary<ObjectId, ShadowObject> additions, ObjectId target)
    {
        var result = new List<(ObjectId Owner, object Accessor)>();
        foreach (var parent in additions.Values)
        {
            foreach (var property in parent.Properties)
            {
                if (property.Value is not ShadowValue.Reference reference || reference.Id != target) continue;
                var type = typeof(DxfDocument).Assembly.GetType(parent.TypeName);
                if (type is null) continue;
                var accessor = Ordered(type).FirstOrDefault(value => Name(value) == property.Key && !(bool)Member(value, "CanWrite"));
                if (accessor is not null) result.Add((parent.Id, accessor));
            }
        }
        return result;
    }

    private static void EmptyIndex()
    {
        var additions = new CountedAdditions(new Dictionary<ObjectId, ShadowObject>());
        Require(BuildIndex(additions).Count == 0);
        Require(additions.ValueVisits == 0 && additions.ValueEnumerations == 1);
        Require(additions.ContainsKeyCalls == 0);
    }

    private static void SinglePass(int count)
    {
        var document = new DxfDocument();
        for (var i = 0; i < count; i++)
            document.Entities.Add(new Line(new Vector2(i, 0), new Vector2(i + 1, 0)));
        var snapshot = new ShadowSession(document).Current;
        var additions = new CountedAdditions(snapshot.Objects);
        var index = BuildIndex(additions);
        Require(additions.ValueEnumerations == 1 && additions.ValueVisits == additions.Count,
            "Ownership discovery must not rescan additions for each target ID.");
        var references = snapshot.Objects.Values.Sum(node => node.Properties.Values.Count(value => value is ShadowValue.Reference));
        Require(additions.ContainsKeyCalls <= references);
        Require(index.Values.Cast<IList>().Sum(candidates => candidates.Count) <= references);
    }

    private static IDictionary BuildIndex(IReadOnlyDictionary<ObjectId, ShadowObject> additions)
        => (IDictionary)IndexMethod.Invoke(null, [additions])!;
    private static object[] Ordered(Type type) => ((Array)ForMethod.Invoke(null, [type])!).Cast<object>().ToArray();
    private static object? Find(Type type, string name) => FindMethod.Invoke(null, [type, name]);
    private static object Member(object accessor, string name) => accessor.GetType().GetProperty(name)!.GetValue(accessor)!;
    private static string Name(object accessor) => (string)Member(accessor, "Name");
    private static Func<object, object?> Get(object accessor) => (Func<object, object?>)Member(accessor, "Get");
    private static Action<object, object?> Set(object accessor) => (Action<object, object?>)Member(accessor, "Set");
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void Require(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public class MetadataBase { public int Value { get; set; } = 7; }
    public sealed class MetadataDerived : MetadataBase
    {
        public new string Value { get; set; } = "derived";
        public int ReadOnly => 42;
        public readonly int ConstantField = 11;
        public int this[int index] => index;
        public int WriteOnly { set { } }
    }
    public struct ValueCarrier { public int Coordinate { get; set; } public int Field; }
    public sealed class ConcurrentCarrier { public int Value { get; set; } }

    private sealed class CountedAdditions(IReadOnlyDictionary<ObjectId, ShadowObject> inner)
        : IReadOnlyDictionary<ObjectId, ShadowObject>
    {
        public int ValueEnumerations { get; private set; }
        public int ValueVisits { get; private set; }
        public int ContainsKeyCalls { get; private set; }
        public int Count => inner.Count;
        public IEnumerable<ObjectId> Keys => inner.Keys;
        public IEnumerable<ShadowObject> Values => EnumerateValues();
        public ShadowObject this[ObjectId id] => inner[id];
        public bool ContainsKey(ObjectId id) { ContainsKeyCalls++; return inner.ContainsKey(id); }
        public bool TryGetValue(ObjectId id, out ShadowObject value) => inner.TryGetValue(id, out value!);
        public IEnumerator<KeyValuePair<ObjectId, ShadowObject>> GetEnumerator() => inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private IEnumerable<ShadowObject> EnumerateValues()
        {
            ValueEnumerations++;
            foreach (var value in inner.Values) { ValueVisits++; yield return value; }
        }
    }
}
