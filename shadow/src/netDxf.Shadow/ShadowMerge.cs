using System.Collections.Immutable;

namespace netDxf.Shadow;

public enum ShadowMergeChoice { Unresolved, Target, Incoming }
public enum ShadowMergeConflictKind { Value, ConcurrentAddition, DeleteModify, Metadata, DanglingReference }

/// <summary>Null values mean absent fields, not the explicit ShadowValue.Null atom.</summary>
public sealed record ShadowMergeConflict(ObjectId Object, ImmutableArray<string> Path,
    ShadowMergeConflictKind Kind, ShadowMergeChoice Resolution,
    ShadowValue? BaseValue = null, ShadowValue? TargetValue = null, ShadowValue? IncomingValue = null,
    ObjectId? ReferencedObject = null);

/// <summary>Limits cover all three inputs, comparisons and final reference validation.</summary>
public sealed record ShadowMergeOptions(ShadowMergeChoice ConflictChoice = ShadowMergeChoice.Unresolved,
    int MaximumObjects = 1_000_000, long MaximumWork = 50_000_000,
    int MaximumValueDepth = 64, int MaximumConflicts = 10_000);

/// <summary>
/// Pure planning result. No patch or preview is exposed while an unresolved conflict exists.
/// Inputs are retained by reference. The caller supplies a genuine common ancestor; matching
/// document IDs and revision numbers alone cannot prove ancestry between isolated branches.
/// </summary>
public sealed class ShadowMergePlan
{
    public ShadowSnapshot Ancestor { get; }
    public ShadowSnapshot Target { get; }
    public ShadowSnapshot Incoming { get; }
    public ImmutableArray<ShadowMergeConflict> Conflicts { get; }
    public ShadowGraphPatch? Patch { get; }
    public ShadowSnapshot? Preview { get; }
    public long Work { get; }
    public bool CanApply => Patch is not null;

    internal ShadowMergePlan(ShadowSnapshot ancestor, ShadowSnapshot target, ShadowSnapshot incoming,
        ImmutableArray<ShadowMergeConflict> conflicts, ShadowGraphPatch? patch, ShadowSnapshot? preview, long work)
    {
        Ancestor = ancestor; Target = target; Incoming = incoming; Conflicts = conflicts;
        Patch = patch; Preview = preview; Work = work;
    }

    /// <summary>
    /// Applies to the exact target branch, not just a branch with the same revision number.
    /// The store must serialize writes and validate optimistic patch preconditions.
    /// Use EditorSession.ApplyMerge instead to record the merge in editor history.
    /// </summary>
    public ShadowSnapshot ApplyTo(IShadowGraphStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (Patch is null) throw new ShadowConflictException("Resolve merge conflicts before applying the plan.");
        if (!ReferenceEquals(store.Current, Target)) throw new ShadowConflictException("Merge target changed or belongs to another branch. Replan the merge.");
        return store.Apply(Patch);
    }
}

/// <summary>
/// Renderer/source-independent three-way object-graph merge. Properties and matching typed
/// structures merge by ordinal field name. Ordered sequences are atomic: competing order,
/// insertion or removal edits are never guessed. References are identities, not traversal edges.
/// </summary>
public static class ShadowMerge
{
    public static ShadowMergePlan Plan(ShadowSnapshot ancestor, ShadowSnapshot target, ShadowSnapshot incoming,
        ShadowMergeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(incoming);
        options ??= new();
        if (!Enum.IsDefined(options.ConflictChoice) || options.MaximumObjects < 1 || options.MaximumWork < 1
            || options.MaximumValueDepth is < 1 or > 256 || options.MaximumConflicts < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (ancestor.DocumentId != target.DocumentId || ancestor.DocumentId != incoming.DocumentId
            || ancestor.Root != target.Root || ancestor.Root != incoming.Root)
            throw new ArgumentException("Merging requires the same document and root identity.");
        if (ancestor.Revision < 0 || target.Revision < ancestor.Revision || incoming.Revision < ancestor.Revision)
            throw new ArgumentException("Branch revisions cannot precede the supplied ancestor.");
        return new Planner(ancestor, target, incoming, options, cancellationToken).Run();
    }

    private sealed class Planner(ShadowSnapshot ancestor, ShadowSnapshot target, ShadowSnapshot incoming,
        ShadowMergeOptions options, CancellationToken token)
    {
        private long work;
        private readonly ImmutableArray<ShadowMergeConflict>.Builder conflicts = ImmutableArray.CreateBuilder<ShadowMergeConflict>();

        public ShadowMergePlan Run()
        {
            Tick();
            Validate(ancestor);
            if (!ReferenceEquals(target, ancestor)) Validate(target);
            if (!ReferenceEquals(incoming, ancestor) && !ReferenceEquals(incoming, target)) Validate(incoming);
            var ids = new HashSet<ObjectId>(target.Objects.Keys);
            ids.UnionWith(incoming.Objects.Keys); ids.UnionWith(ancestor.Objects.Keys);
            if (ids.Count > options.MaximumObjects) throw new InvalidOperationException("Merge object-union budget exceeded.");
            var edits = ImmutableArray.CreateBuilder<ShadowObjectChange>();
            var nodes = target.Objects.ToBuilder();
            foreach (var id in ids.OrderBy(id => id.Value, StringComparer.Ordinal))
            {
                Tick();
                var b = ancestor.Objects.GetValueOrDefault(id);
                var t = target.Objects.GetValueOrDefault(id);
                var i = incoming.Objects.GetValueOrDefault(id);
                if (b is not null && (t is not null && b.TypeName != t.TypeName || i is not null && b.TypeName != i.TypeName))
                    throw new ArgumentException($"Runtime type changed for existing identity {id}.");
                var merged = MergeObject(id, b, t, i);
                if (SameObject(t, merged)) continue;
                edits.Add(new(id, t, merged));
                if (merged is null) nodes.Remove(id); else nodes[id] = merged;
            }
            if (conflicts.Any(c => c.Resolution == ShadowMergeChoice.Unresolved)) return Result(null, null);

            // Deletion on one branch can invalidate a reference independently added on the
            // other branch. Neither branch-local value comparison detects that combination.
            IEnumerable<ShadowObject> validate = edits.Any(e => e.After is null) ? nodes.Values
                : edits.Where(e => e.After is not null).Select(e => e.After!);
            foreach (var node in validate.OrderBy(n => n.Id.Value, StringComparer.Ordinal))
                foreach (var property in node.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                    CheckReferences(node.Id, [property.Key], property.Value, nodes);
            if (conflicts.Any(c => c.Resolution == ShadowMergeChoice.Unresolved)) return Result(null, null);
            var patch = new ShadowGraphPatch(target.DocumentId, target.Revision, edits.ToImmutable());
            // Reuse the exact existing transaction validator and its entity/diagnostic rules.
            // Deliberately no HistoricalTarget: a speculative graph is not source history.
            var preview = patch.Apply(target);
            return Result(patch, preview);
        }

        private ShadowMergePlan Result(ShadowGraphPatch? patch, ShadowSnapshot? preview)
            => new(ancestor, target, incoming, conflicts.ToImmutable(), patch, preview, work);

        private ShadowObject? MergeObject(ObjectId id, ShadowObject? b, ShadowObject? t, ShadowObject? i)
        {
            if (SameObject(t, i) || SameObject(b, i)) return t;
            if (SameObject(b, t)) return i;
            if (b is null || t is null || i is null)
            {
                Conflict(new(id, [], b is null ? ShadowMergeConflictKind.ConcurrentAddition : ShadowMergeConflictKind.DeleteModify,
                    options.ConflictChoice));
                return options.ConflictChoice == ShadowMergeChoice.Incoming ? i : t;
            }
            var metadata = t;
            if (!SameMetadata(t, i) && !SameMetadata(b, i))
            {
                if (SameMetadata(b, t)) metadata = i;
                else
                {
                    Conflict(new(id, [], ShadowMergeConflictKind.Metadata, options.ConflictChoice));
                    if (options.ConflictChoice == ShadowMergeChoice.Incoming) metadata = i;
                }
            }
            var properties = MergeFields(id, [], b.Properties, t.Properties, i.Properties);
            return ReferenceEquals(properties, t.Properties) && SameMetadata(metadata, t) ? t
                : t with { Properties = properties, Kind = metadata.Kind, WritableProperties = metadata.WritableProperties };
        }

        private ImmutableDictionary<string, ShadowValue> MergeFields(ObjectId id, ImmutableArray<string> path,
            ImmutableDictionary<string, ShadowValue> b, ImmutableDictionary<string, ShadowValue> t,
            ImmutableDictionary<string, ShadowValue> i)
        {
            var keys = new HashSet<string>(b.Keys, StringComparer.Ordinal);
            keys.UnionWith(t.Keys); keys.UnionWith(i.Keys);
            var result = t.ToBuilder();
            foreach (var key in keys.Order(StringComparer.Ordinal))
            {
                Tick();
                var old = b.GetValueOrDefault(key); var current = t.GetValueOrDefault(key); var branch = i.GetValueOrDefault(key);
                var value = MergeValue(id, path.Add(key), old, current, branch);
                if (ReferenceEquals(value, current)) continue;
                if (value is null) result.Remove(key); else result[key] = value;
            }
            return result.ToImmutable();
        }

        private ShadowValue? MergeValue(ObjectId id, ImmutableArray<string> path, ShadowValue? b, ShadowValue? t, ShadowValue? i)
        {
            if (Same(t, i) || Same(b, i)) return t;
            if (Same(b, t)) return i;
            if (b is ShadowValue.Structure bs && t is ShadowValue.Structure ts && i is ShadowValue.Structure ins
                && bs.TypeName == ts.TypeName && bs.TypeName == ins.TypeName)
                return ts with { Fields = MergeFields(id, path, bs.Fields, ts.Fields, ins.Fields) };
            Conflict(new(id, path, ShadowMergeConflictKind.Value, options.ConflictChoice, b, t, i));
            return options.ConflictChoice == ShadowMergeChoice.Incoming ? i : t;
        }

        private void CheckReferences(ObjectId owner, ImmutableArray<string> path, ShadowValue value,
            ImmutableDictionary<ObjectId, ShadowObject>.Builder nodes)
        {
            Tick();
            switch (value)
            {
                case ShadowValue.Reference r when !nodes.ContainsKey(r.Id):
                    // A preference cannot authorize an invalid graph or implicit resurrection.
                    Conflict(new(owner, path, ShadowMergeConflictKind.DanglingReference,
                        ShadowMergeChoice.Unresolved, ReferencedObject: r.Id));
                    break;
                case ShadowValue.Structure structure:
                    foreach (var pair in structure.Fields.OrderBy(p => p.Key, StringComparer.Ordinal))
                        CheckReferences(owner, path.Add(pair.Key), pair.Value, nodes);
                    break;
                case ShadowValue.Sequence sequence:
                    for (var index = 0; index < sequence.Items.Length; index++)
                        CheckReferences(owner, path.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture)), sequence.Items[index], nodes);
                    break;
            }
        }

        private void Validate(ShadowSnapshot snapshot)
        {
            if (snapshot.Objects is null || snapshot.EntityIds is null || snapshot.Diagnostics.IsDefault
                || string.IsNullOrEmpty(snapshot.Root.Value) || !snapshot.Objects.ContainsKey(snapshot.Root))
                throw new ArgumentException("Merge inputs require initialized snapshots with a present root.");
            if (snapshot.Objects.KeyComparer != EqualityComparer<ObjectId>.Default
                || snapshot.EntityIds.KeyComparer != EqualityComparer<ObjectId>.Default)
                throw new ArgumentException("Merge identities require default ordinal equality.");
            if (snapshot.Objects.Count > options.MaximumObjects) throw new InvalidOperationException("Merge object budget exceeded.");
            foreach (var pair in snapshot.Objects)
            {
                Tick();
                var node = pair.Value;
                if (node is null || node.Id != pair.Key || string.IsNullOrEmpty(pair.Key.Value) || string.IsNullOrEmpty(node.TypeName)
                    || node.Properties is null || node.WritableProperties is null)
                    throw new ArgumentException("Merge input contains malformed object metadata.");
                RequireOrdinal(node.Properties);
                if (node.WritableProperties.KeyComparer != StringComparer.Ordinal
                    && node.WritableProperties.KeyComparer != EqualityComparer<string>.Default)
                    throw new ArgumentException("Merge writable names require ordinal equality.");
                if (snapshot.EntityIds.Contains(pair.Key) != ((node.Kind & ShadowObjectKind.Entity) != 0))
                    throw new ArgumentException("Merge input has an inconsistent entity index.");
                foreach (var property in node.Properties) ValidateValue(property.Value, snapshot, 1);
                foreach (var property in node.WritableProperties)
                {
                    Tick();
                    if (property is null) throw new ArgumentException("Writable property names cannot be null.");
                }
            }
            foreach (var id in snapshot.EntityIds) { Tick(); if (!snapshot.Objects.ContainsKey(id)) throw new ArgumentException("Merge entity index contains an absent object."); }
        }

        private void ValidateValue(ShadowValue value, ShadowSnapshot snapshot, int depth)
        {
            Tick();
            if (depth > options.MaximumValueDepth) throw new InvalidOperationException("Merge value-depth budget exceeded.");
            switch (value)
            {
                case ShadowValue.NullValue: break;
                case ShadowValue.Atom a when a.TypeName is not null && a.Text is not null: break;
                case ShadowValue.Fault f when f.Message is not null: break;
                case ShadowValue.Reference r when snapshot.Objects.ContainsKey(r.Id): break;
                case ShadowValue.Sequence s when !s.Items.IsDefault:
                    foreach (var item in s.Items) ValidateValue(item, snapshot, depth + 1);
                    break;
                case ShadowValue.Structure s when s.TypeName is not null && s.Fields is not null:
                    RequireOrdinal(s.Fields);
                    foreach (var field in s.Fields.Values) ValidateValue(field, snapshot, depth + 1);
                    break;
                default: throw new ArgumentException("Merge input has a malformed value or dangling reference.");
            }
        }

        private static void RequireOrdinal(ImmutableDictionary<string, ShadowValue> map)
        {
            // Case-folded maps cannot safely represent case-sensitive DXF/public field names.
            if (map.KeyComparer != StringComparer.Ordinal && map.KeyComparer != EqualityComparer<string>.Default)
                throw new ArgumentException("Merge fields require ordinal, case-sensitive key equality.");
        }

        private bool SameMetadata(ShadowObject a, ShadowObject b)
        {
            Tick();
            if (a.TypeName != b.TypeName || a.Kind != b.Kind || a.WritableProperties.Count != b.WritableProperties.Count) return false;
            if (ReferenceEquals(a.WritableProperties, b.WritableProperties)) return true;
            foreach (var name in a.WritableProperties) { Tick(); if (!b.WritableProperties.Contains(name)) return false; }
            return true;
        }
        private bool SameObject(ShadowObject? a, ShadowObject? b)
        {
            Tick();
            return ReferenceEquals(a, b) || a is not null && b is not null && a.Id == b.Id && SameMetadata(a, b) && SameFields(a.Properties, b.Properties);
        }
        private bool SameFields(ImmutableDictionary<string, ShadowValue> a, ImmutableDictionary<string, ShadowValue> b)
        {
            Tick();
            if (ReferenceEquals(a, b)) return true;
            if (a.Count != b.Count) return false;
            foreach (var pair in a) { Tick(); if (!b.TryGetValue(pair.Key, out var value) || !Same(pair.Value, value)) return false; }
            return true;
        }
        private bool Same(ShadowValue? a, ShadowValue? b)
        {
            Tick();
            if (ReferenceEquals(a, b)) return true;
            return (a, b) switch
            {
                (ShadowValue.Structure x, ShadowValue.Structure y) => x.TypeName == y.TypeName && SameFields(x.Fields, y.Fields),
                (ShadowValue.Sequence x, ShadowValue.Sequence y) => SameSequence(x.Items, y.Items),
                (ShadowValue.Atom x, ShadowValue.Atom y) => x.TypeName == y.TypeName && x.Text == y.Text,
                (ShadowValue.Reference x, ShadowValue.Reference y) => x.Id == y.Id,
                (ShadowValue.Fault x, ShadowValue.Fault y) => x.Message == y.Message,
                (ShadowValue.NullValue, ShadowValue.NullValue) => true,
                _ => false
            };
        }
        private bool SameSequence(ImmutableArray<ShadowValue> a, ImmutableArray<ShadowValue> b)
        {
            if (a == b) return true;
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++) if (!Same(a[i], b[i])) return false;
            return true;
        }
        private void Conflict(ShadowMergeConflict conflict)
        {
            Tick();
            if (conflicts.Count == options.MaximumConflicts) throw new InvalidOperationException("Merge conflict budget exceeded.");
            conflicts.Add(conflict);
        }
        private void Tick()
        {
            token.ThrowIfCancellationRequested();
            if (++work > options.MaximumWork) throw new InvalidOperationException("Merge work budget exceeded.");
        }
    }
}
