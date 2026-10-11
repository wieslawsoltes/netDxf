using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

/// <summary>Expands occurrences, never clones or mutates a netDxf entity.</summary>
public sealed partial class CadSceneBuilder
{
    private readonly ImmutableArray<IEntityProjector> projectors;
    private readonly ProjectionCache cache;
    private readonly object buildGate = new();
    private readonly RootProjectionCache rootCache = new();
    public SceneBuildStatistics LastBuildStatistics { get { lock (buildGate) return rootCache.Statistics; } }
    public ProjectionCacheStatistics CacheStatistics => cache.Statistics;
    public bool IsSnapshotCacheable => projectors.All(p => p is ICacheableEntityProjector);
    public long ProjectionVersion
    {
        get
        {
            long value = 17;
            foreach (var projector in projectors)
                value = unchecked(value * 31 + ((projector as ICacheableEntityProjector)?.Revision ?? 0));
            return value;
        }
    }
    public CadSceneBuilder(IEnumerable<IEntityProjector>? extensions = null, ProjectionCacheOptions? cacheOptions = null)
    {
        projectors = (extensions ?? []).Append(new StandardProjectionCacheAdapter()).ToImmutableArray();
        cache = new(cacheOptions ?? new());
    }

    public CadScene Build(ShadowSnapshot snapshot, SceneOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (buildGate) return BuildCore(snapshot, options ?? new(), cancellationToken);
    }

}
