using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow;

/// <summary>All fields are immutable; acquiring/retaining a snapshot is O(1).</summary>
public sealed record ShadowSnapshot(Guid DocumentId, long Revision, ObjectId Root,
    ImmutableDictionary<ObjectId, ShadowObject> Objects, ImmutableHashSet<ObjectId> EntityIds,
    ImmutableArray<ShadowDiagnostic> Diagnostics)
{
    public ShadowReader Read(ObjectId id) => new(this, new ShadowValue.Reference(id));
    public ShadowReader Document => Read(Root);
    public IEnumerable<ShadowReader> Layouts => Document["Layouts"].Items;
    public ShadowReader Layout(string name = "Model") => Layouts.FirstOrDefault(n => string.Equals(n["Name"].Text, name, StringComparison.OrdinalIgnoreCase));
    public IEnumerable<ShadowReader> DrawingEntities(string layout = "Model") => Layout(layout)["AssociatedBlock"]["Entities"].Items;

    public ShadowSnapshot Apply(ShadowEditBatch batch)
    {
        if (batch.DocumentId != DocumentId || batch.ExpectedRevision != Revision)
            throw new ShadowConflictException("The edit belongs to another document or a stale snapshot.");
        if (batch.Edits.IsDefaultOrEmpty) return this;
        var seen = new HashSet<(ObjectId, string)>();
        var builder = Objects.ToBuilder();
        foreach (var edit in batch.Edits)
        {
            if (!seen.Add((edit.Object, edit.Property))) throw new ArgumentException("Duplicate property edits are not allowed.", nameof(batch));
            if (!builder.TryGetValue(edit.Object, out var node)) throw new ShadowConflictException($"Object {edit.Object} was removed.");
            if (!node.WritableProperties.Contains(edit.Property)) throw new InvalidOperationException($"{node.Type}.{edit.Property} is read-only.");
            if (!ShadowValue.Equivalent(node[edit.Property], edit.Before)) throw new ShadowConflictException($"{edit.Object}.{edit.Property} has changed.");
            foreach (var reference in edit.After.References())
                if (!Objects.ContainsKey(reference)) throw new ArgumentException($"Dangling reference: {reference}.", nameof(batch));
            builder[edit.Object] = node with { Properties = node.Properties.SetItem(edit.Property, edit.After) };
        }
        return this with { Revision = checked(Revision + 1), Objects = builder.ToImmutable() };
    }

    public ShadowDelta Diff(ShadowSnapshot previous)
    {
        if (DocumentId != previous.DocumentId) throw new ArgumentException("Cannot diff unrelated documents.", nameof(previous));
        var changed = ImmutableHashSet.CreateBuilder<ObjectId>();
        var added = ImmutableHashSet.CreateBuilder<ObjectId>();
        var removed = ImmutableHashSet.CreateBuilder<ObjectId>();
        foreach (var (id, node) in Objects)
        {
            if (!previous.Objects.TryGetValue(id, out var before)) added.Add(id);
            else if (!ReferenceEquals(node, before)) changed.Add(id);
        }
        foreach (var id in previous.Objects.Keys) if (!Objects.ContainsKey(id)) removed.Add(id);
        return new(previous.Revision, Revision, added.ToImmutable(), changed.ToImmutable(), removed.ToImmutable());
    }

    /// <summary>Reverse references for resource/block invalidation. Built by consumers when needed, not per frame.</summary>
    public ImmutableDictionary<ObjectId, ImmutableHashSet<ObjectId>> BuildDependents()
    {
        var map = new Dictionary<ObjectId, HashSet<ObjectId>>();
        foreach (var node in Objects.Values)
            foreach (var target in node.Properties.Values.SelectMany(v => v.References()))
            {
                if (!map.TryGetValue(target, out var set)) map[target] = set = new();
                set.Add(node.Id);
            }
        return map.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableHashSet());
    }
}

/// <summary>A stack-only-in-spirit view over immutable values. Missing values have safe query defaults.</summary>
public readonly struct ShadowReader
{
    public ShadowSnapshot? Snapshot { get; }
    public ShadowValue Value { get; }
    public ShadowReader(ShadowSnapshot snapshot, ShadowValue value) { Snapshot = snapshot; Value = value; }
    public ObjectId? Id => Value is ShadowValue.Reference r ? r.Id : null;
    public ShadowObject? Object => Snapshot is not null && Id is { } id ? Snapshot.Objects.GetValueOrDefault(id) : null;
    public bool Exists => Value is not null && Value is not ShadowValue.NullValue;
    public string Type => Object?.Type ?? (Value is ShadowValue.Structure s ? s.TypeName[(s.TypeName.LastIndexOf('.') + 1)..] : string.Empty);
    public string Text => Value is ShadowValue.Atom a ? a.Text : string.Empty;
    public double Number(double fallback = 0) => double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : fallback;
    public bool Boolean(bool fallback = false) => bool.TryParse(Text, out var result) ? result : fallback;
    public Vec3 Vector(Vec3 fallback = default) => this["X"].Exists ? new(this["X"].Number(), this["Y"].Number(), this["Z"].Number()) : fallback;
    public ShadowReader this[string key]
    {
        get
        {
            if (Snapshot is null) return default;
            var value = Object is { } node ? node[key] : Value is ShadowValue.Structure s ? s.Fields.GetValueOrDefault(key, ShadowValue.Null) : ShadowValue.Null;
            return new(Snapshot, value);
        }
    }
    public IEnumerable<ShadowReader> Items
    {
        get
        {
            var snapshot = Snapshot;
            if (snapshot is null) yield break;
            var source = Value is ShadowValue.Sequence s ? s : this["$items"].Value as ShadowValue.Sequence;
            if (source is not null) foreach (var item in source.Items) yield return new(snapshot, item);
        }
    }
}

public sealed record ShadowPropertyEdit(ObjectId Object, string Property, ShadowValue Before, ShadowValue After);
public sealed record ShadowEditBatch(Guid DocumentId, long ExpectedRevision, ImmutableArray<ShadowPropertyEdit> Edits)
{
    public static ShadowEditBatch Create(ShadowSnapshot snapshot, params ShadowPropertyEdit[] edits) => new(snapshot.DocumentId, snapshot.Revision, edits.ToImmutableArray());
    public ShadowEditBatch Inverse(ShadowSnapshot current) => new(current.DocumentId, current.Revision,
        Edits.Reverse().Select(e => new ShadowPropertyEdit(e.Object, e.Property, e.After, e.Before)).ToImmutableArray());
}
public sealed record ShadowDelta(long FromRevision, long ToRevision, ImmutableHashSet<ObjectId> Added,
    ImmutableHashSet<ObjectId> Changed, ImmutableHashSet<ObjectId> Removed);
public sealed class ShadowConflictException(string message) : InvalidOperationException(message);

public interface IShadowStore
{
    ShadowSnapshot Current { get; }
    ShadowSnapshot Apply(ShadowEditBatch batch);
}

/// <summary>An isolated immutable editor branch; merging back still requires optimistic validation.</summary>
public sealed class ShadowMemoryStore(ShadowSnapshot initial) : IShadowGraphStore
{
    private readonly object gate = new();
    private ShadowSnapshot current = initial;
    public ShadowSnapshot Current => Volatile.Read(ref current);
    public ShadowSnapshot Apply(ShadowGraphPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        lock (gate)
        {
            var next = patch.Apply(current);
            Volatile.Write(ref current, next);
            return next;
        }
    }

    public ShadowSnapshot Apply(ShadowEditBatch batch)
    {
        lock (gate)
        {
            var next = current.Apply(batch);
            Volatile.Write(ref current, next);
            return next;
        }
    }
}
