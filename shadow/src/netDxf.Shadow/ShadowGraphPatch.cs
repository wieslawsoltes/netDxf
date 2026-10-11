using System.Collections.Immutable;

namespace netDxf.Shadow;

/// <summary>An optimistic, immutable replacement of one public object-graph node.</summary>
public sealed record ShadowObjectChange(ObjectId Id, ShadowObject? Before, ShadowObject? After);

/// <summary>
/// A structural transaction. Unlike property batches it may change collection membership,
/// create nodes and delete nodes. Every precondition and reference is validated before publication.
/// HistoricalTarget is immutable and lets a source adapter recover its own historical identities.
/// </summary>
public sealed record ShadowGraphPatch(Guid DocumentId, long ExpectedRevision,
    ImmutableArray<ShadowObjectChange> Changes)
{
    public ShadowSnapshot? HistoricalTarget { get; init; }

    public static ShadowGraphPatch Between(ShadowSnapshot before, ShadowSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.DocumentId != after.DocumentId || before.Root != after.Root)
            throw new ArgumentException("Cannot reconcile unrelated document graphs.");
        var edits = ImmutableArray.CreateBuilder<ShadowObjectChange>();
        foreach (var (id, node) in before.Objects)
        {
            if (!after.Objects.TryGetValue(id, out var target)) edits.Add(new(id, node, null));
            else if (!Equivalent(node, target)) edits.Add(new(id, node, target));
        }
        foreach (var (id, node) in after.Objects)
            if (!before.Objects.ContainsKey(id)) edits.Add(new(id, null, node));
        return new(before.DocumentId, before.Revision, edits.ToImmutable()) { HistoricalTarget = after };
    }

    public ShadowSnapshot Apply(ShadowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.DocumentId != DocumentId || snapshot.Revision != ExpectedRevision)
            throw new ShadowConflictException("Structural edit belongs to a stale snapshot or another document.");
        if (Changes.IsDefaultOrEmpty) return snapshot;
        var seen = new HashSet<ObjectId>();
        var nodes = snapshot.Objects.ToBuilder();
        var entities = snapshot.EntityIds.ToBuilder();
        var changed = false;
        var hasRemoval = false;
        foreach (var edit in Changes)
        {
            if (string.IsNullOrEmpty(edit.Id.Value) || !seen.Add(edit.Id))
                throw new ArgumentException("Structural edits require unique, nonempty object identities.");
            snapshot.Objects.TryGetValue(edit.Id, out var actual);
            if (!Equivalent(actual, edit.Before))
                throw new ShadowConflictException($"Structural precondition failed for {edit.Id}.");
            if (edit.After is { } replacement && replacement.Id != edit.Id || edit.Before is { } original && original.Id != edit.Id)
                throw new ArgumentException("Object identity and patch identity must agree.");
            if (edit.Id == snapshot.Root && (edit.After is null || edit.After.TypeName != actual?.TypeName))
                throw new ArgumentException("The document root cannot be removed or replaced with another type.");
            if (edit.Before is not null && edit.After is not null && edit.Before.TypeName != edit.After.TypeName)
                throw new ArgumentException("Changing the runtime type of an existing identity is not supported.");
            if (Equivalent(edit.Before, edit.After)) continue;
            changed = true;
            if (edit.After is null)
            {
                nodes.Remove(edit.Id); entities.Remove(edit.Id); hasRemoval = true;
            }
            else
            {
                nodes[edit.Id] = edit.After;
                if ((edit.After.Kind & ShadowObjectKind.Entity) != 0) entities.Add(edit.Id);
                else entities.Remove(edit.Id);
            }
        }
        if (!changed) return snapshot;
        // Add/change-only transactions need check only their changed outgoing edges.
        // Deletions also need check incoming edges from otherwise unchanged objects.
        IEnumerable<ShadowObject> validate = hasRemoval ? nodes.Values : Changes.Where(e => e.After is not null).Select(e => e.After!);
        foreach (var node in validate)
            foreach (var reference in node.Properties.Values.SelectMany(v => v.References()))
                if (!nodes.ContainsKey(reference)) throw new ArgumentException($"{node.Id} contains a dangling reference to {reference}.");
        return snapshot with
        {
            Revision = checked(snapshot.Revision + 1), Objects = nodes.ToImmutable(), EntityIds = entities.ToImmutable(),
            Diagnostics = snapshot.Diagnostics.Where(d => d.Object is not { } id || nodes.ContainsKey(id)).ToImmutableArray()
        };
    }

    public static bool Equivalent(ShadowObject? left, ShadowObject? right) => ReferenceEquals(left, right) ||
        left is not null && right is not null && left.Id == right.Id && left.TypeName == right.TypeName &&
        left.Kind == right.Kind && left.WritableProperties.SetEquals(right.WritableProperties) &&
        ShadowValue.Equivalent(left.Properties, right.Properties);
}

public interface IShadowGraphStore : IShadowStore
{
    ShadowSnapshot Apply(ShadowGraphPatch patch);
}

/// <summary>
/// Mutable transaction builder over persistent data. Build returns a detached immutable patch;
/// the builder never mutates its input snapshot. Identifiers for new nodes are caller-visible.
/// </summary>
public sealed class ShadowGraphEdit
{
    private readonly ShadowSnapshot baseline;
    private readonly ImmutableDictionary<ObjectId, ShadowObject>.Builder nodes;

    public ShadowGraphEdit(ShadowSnapshot snapshot)
    {
        baseline = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        nodes = snapshot.Objects.ToBuilder();
    }

    public ObjectId Create(string typeName, ShadowObjectKind kind, IEnumerable<KeyValuePair<string, ShadowValue>> properties,
        ObjectId? identity = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(properties);
        var id = identity ?? new ObjectId("N:" + Guid.NewGuid().ToString("N"));
        if (string.IsNullOrEmpty(id.Value) || nodes.ContainsKey(id)) throw new ArgumentException("Object identity is already in use.");
        var values = properties.ToImmutableDictionary(StringComparer.Ordinal);
        nodes.Add(id, new(id, typeName, kind, values, values.Keys.ToImmutableHashSet(StringComparer.Ordinal)));
        return id;
    }

    public void Set(ObjectId id, string property, ShadowValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(property);
        ArgumentNullException.ThrowIfNull(value);
        var node = nodes.TryGetValue(id, out var existing) ? existing : throw new ArgumentException("Unknown object.", nameof(id));
        nodes[id] = node with { Properties = node.Properties.SetItem(property, value) };
    }

    public void ReplaceSequence(ObjectId id, string property, IEnumerable<ShadowValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var node = nodes[id];
        if (node[property] is ShadowValue.Reference reference)
        {
            id = reference.Id; property = "$items"; node = nodes[id];
        }
        if (node[property] is not ShadowValue.Sequence) throw new ArgumentException("The selected property is not a sequence.");
        var sequence = new ShadowValue.Sequence(items.ToImmutableArray());
        Set(id, property, sequence);
        if (property == "$items" && node["Count"] is ShadowValue.Atom count)
            Set(id, "Count", count with { Text = sequence.Items.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    public ObjectId AddEntity(ObjectId ownerBlock, string typeName,
        IEnumerable<KeyValuePair<string, ShadowValue>> properties, ObjectId? identity = null)
    {
        var owner = nodes[ownerBlock];
        if ((owner.Kind & ShadowObjectKind.Block) == 0) throw new ArgumentException("An entity must be owned by a block.");
        var values = properties.ToImmutableDictionary(StringComparer.Ordinal).SetItem("Owner", new ShadowValue.Reference(ownerBlock));
        var id = Create(typeName, ShadowObjectKind.Entity, values, identity);
        var collection = owner["Entities"] is ShadowValue.Reference reference ? nodes[reference.Id]["$items"] : owner["Entities"];
        if (collection is not ShadowValue.Sequence sequence) throw new ArgumentException("Block entity collection is unavailable.");
        ReplaceSequence(ownerBlock, "Entities", sequence.Items.Add(new ShadowValue.Reference(id)));
        return id;
    }

    /// <summary>
    /// Detaches entities from every public membership view and removes unreachable owned data.
    /// Scalar relationships are nulled; a source adapter may reject relationships that its API
    /// cannot legally detach. Such rejections never publish an invalid shadow graph.
    /// </summary>
    public void RemoveEntities(IEnumerable<ObjectId> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var removed = identities.ToImmutableHashSet();
        foreach (var id in removed)
            if (!nodes.TryGetValue(id, out var node) || (node.Kind & ShadowObjectKind.Entity) == 0)
                throw new ArgumentException($"{id} is not an entity.");
        foreach (var (id, node) in nodes.ToArray())
        {
            if (removed.Contains(id)) { nodes.Remove(id); continue; }
            var fields = node.Properties;
            foreach (var (name, value) in fields)
            {
                var next = Without(value);
                if (!ReferenceEquals(value, next)) fields = fields.SetItem(name, next);
            }
            if (fields.GetValueOrDefault("$items") is ShadowValue.Sequence members && fields.GetValueOrDefault("Count") is ShadowValue.Atom count)
                fields = fields.SetItem("Count", count with { Text = members.Items.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            if (!ReferenceEquals(fields, node.Properties)) nodes[id] = node with { Properties = fields };
        }
        PruneUnreachable();

        ShadowValue Without(ShadowValue value)
        {
            switch (value)
            {
                case ShadowValue.Reference r: return removed.Contains(r.Id) ? ShadowValue.Null : value;
                case ShadowValue.Sequence s:
                    var result = ImmutableArray.CreateBuilder<ShadowValue>(s.Items.Length);
                    var changed = false;
                    foreach (var item in s.Items)
                    {
                        if (item is ShadowValue.Reference reference && removed.Contains(reference.Id)) { changed = true; continue; }
                        var next = Without(item); changed |= !ReferenceEquals(next, item); result.Add(next);
                    }
                    return changed ? new ShadowValue.Sequence(result.ToImmutable()) : value;
                case ShadowValue.Structure s:
                    var fields = s.Fields;
                    foreach (var (name, field) in s.Fields)
                    {
                        var next = Without(field);
                        if (!ReferenceEquals(next, field)) fields = fields.SetItem(name, next);
                    }
                    return ReferenceEquals(fields, s.Fields) ? value : s with { Fields = fields };
                default: return value;
            }
        }
    }

    public void PruneUnreachable()
    {
        var reached = new HashSet<ObjectId>();
        var pending = new Stack<ObjectId>(); pending.Push(baseline.Root);
        while (pending.TryPop(out var id))
        {
            if (!reached.Add(id) || !nodes.TryGetValue(id, out var node)) continue;
            foreach (var reference in node.Properties.Values.SelectMany(v => v.References())) pending.Push(reference);
        }
        foreach (var id in nodes.Keys.ToArray()) if (!reached.Contains(id)) nodes.Remove(id);
    }

    public ShadowGraphPatch Build()
    {
        var target = baseline with { Objects = nodes.ToImmutable() };
        return ShadowGraphPatch.Between(baseline, target);
    }
}
