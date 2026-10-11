using System.Collections.Immutable;

namespace netDxf.Shadow;

/// <summary>Per-operation read observer. Observers must not mutate the supplied immutable nodes.</summary>
public interface IShadowReadObserver
{
    void Read(ObjectId id, ShadowObject? node);
}

/// <summary>
/// Tracks only the nodes actually dereferenced by a consumer. Does not recursively follow
/// ownership or reverse edges, and does not retain a snapshot or a mutable source object.
/// </summary>
public sealed class ShadowReadSet : IShadowReadObserver
{
    private readonly Dictionary<ObjectId, ShadowObject?> reads = new();
    public void Read(ObjectId id, ShadowObject? node) => reads[id] = node;
    public ImmutableArray<ShadowDependency> Freeze() => reads.Select(p => new ShadowDependency(p.Key, p.Value)).ToImmutableArray();
}

public readonly record struct ShadowDependency(ObjectId Id, ShadowObject? Node)
{
    public bool IsCurrent(ShadowSnapshot snapshot) => ReferenceEquals(Node, snapshot.Objects.GetValueOrDefault(Id));
}
