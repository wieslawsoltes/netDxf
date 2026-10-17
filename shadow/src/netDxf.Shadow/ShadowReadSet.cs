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
    // Projectors commonly read one entity repeatedly. Keep that first dependency
    // inline so those accesses need neither a dictionary allocation nor a hash lookup.
    private ShadowDependency first;
    private bool hasFirst;
    private Dictionary<ObjectId, ShadowObject?>? others;

    public void Read(ObjectId id, ShadowObject? node)
    {
        if (!hasFirst) { first = new(id, node); hasFirst = true; }
        else if (first.Id == id)
        {
            if (!ReferenceEquals(first.Node, node)) first = new(first.Id, node);
        }
        else (others ??= new())[id] = node;
    }
    public ImmutableArray<ShadowDependency> Freeze()
    {
        if (!hasFirst) return [];
        if (others is null) return [first];
        var result = ImmutableArray.CreateBuilder<ShadowDependency>(others.Count + 1);
        result.Add(first);
        foreach (var (id, node) in others) result.Add(new(id, node));
        return result.MoveToImmutable();
    }
}

public readonly record struct ShadowDependency(ObjectId Id, ShadowObject? Node)
{
    public bool IsCurrent(ShadowSnapshot snapshot) => ReferenceEquals(Node, snapshot.Objects.GetValueOrDefault(Id));
}
