using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public readonly record struct SceneBuildStatistics(int RootsProjected, int RootsReused);

/// <summary>Tracks top-level expansion dependencies so a local edit does not re-resolve every entity's style and transforms.</summary>
internal sealed class RootProjectionCache
{
    internal sealed record Entry(ImmutableArray<CadOccurrence> Occurrences, ImmutableArray<ShadowDiagnostic> Diagnostics,
        ImmutableArray<ShadowDependency> Dependencies, int Visits);
    private sealed class State(ShadowSnapshot snapshot, SceneOptions options, long version, ShadowValue membership)
    {
        internal ShadowSnapshot Snapshot = snapshot;
        internal ShadowValue Membership = membership;
        internal readonly SceneOptions Options = options;
        internal readonly long Version = version;
        internal readonly Dictionary<ObjectId, Entry> Roots = new();
        internal readonly Dictionary<ObjectId, HashSet<ObjectId>> Dependents = new();
        internal void Remove(ObjectId id)
        {
            if (!Roots.Remove(id, out var entry)) return;
            foreach (var dependency in entry.Dependencies)
            {
                var consumers = Dependents[dependency.Id]; consumers.Remove(id);
                if (consumers.Count == 0) Dependents.Remove(dependency.Id);
            }
        }
        internal void Set(ObjectId id, Entry entry)
        {
            Remove(id); Roots[id] = entry;
            foreach (var dependency in entry.Dependencies)
            {
                if (!Dependents.TryGetValue(dependency.Id, out var consumers)) Dependents[dependency.Id] = consumers = new();
                consumers.Add(id);
            }
        }
    }
    private State? state;
    internal SceneBuildStatistics Statistics { get; private set; }
    internal Transaction Begin(ShadowSnapshot snapshot, SceneOptions options, long version, ShadowValue membership, bool enabled)
        => new(this, snapshot, options, version, membership, enabled);

    internal sealed class Transaction
    {
        private readonly RootProjectionCache owner;
        private readonly ShadowSnapshot snapshot;
        private readonly ShadowValue membership;
        private readonly State next;
        private readonly HashSet<ObjectId> dirty = new();
        private readonly Dictionary<ObjectId, Entry> pending = new();
        private readonly HashSet<ObjectId>? seen;
        private readonly bool enabled;
        private int reused, projected;
        internal Transaction(RootProjectionCache owner, ShadowSnapshot snapshot, SceneOptions options, long version, ShadowValue membership, bool enabled)
        {
            this.owner = owner; this.snapshot = snapshot; this.membership = membership; this.enabled = enabled;
            var previous = owner.state;
            if (enabled && previous is not null && previous.Snapshot.DocumentId == snapshot.DocumentId && previous.Options == options && previous.Version == version)
            {
                next = previous;
                if (!ReferenceEquals(previous.Membership, membership)) seen = new();
                var delta = snapshot.Diff(previous.Snapshot);
                foreach (var id in delta.Added.Concat(delta.Changed).Concat(delta.Removed))
                    if (previous.Dependents.TryGetValue(id, out var consumers)) dirty.UnionWith(consumers);
            }
            else { next = new(snapshot, options, version, membership); seen = new(); }
        }
        internal bool TryGet(ObjectId id, out Entry entry)
        {
            seen?.Add(id);
            if (enabled && !dirty.Contains(id) && next.Roots.TryGetValue(id, out entry!)) { reused++; return true; }
            projected++; entry = null!; return false;
        }
        internal void Add(ObjectId id, Entry entry) { if (enabled) pending[id] = entry; }
        internal void Commit()
        {
            if (seen is not null)
                foreach (var id in next.Roots.Keys.Where(id => !seen.Contains(id)).ToArray()) next.Remove(id);
            foreach (var (id, entry) in pending) next.Set(id, entry);
            next.Snapshot = snapshot; next.Membership = membership;
            owner.state = enabled ? next : null;
            owner.Statistics = new(projected, reused);
        }
    }
}
