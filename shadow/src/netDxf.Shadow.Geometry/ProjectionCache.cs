using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

/// <summary>
/// Opt-in cache contract. Read all model dependencies through the supplied ShadowReader
/// (including its derived readers), not directly through Snapshot.Objects. Increment Revision
/// whenever external configuration or external resources affect projection.
/// </summary>
public interface ICacheableEntityProjector : IEntityProjector
{
    long Revision { get; }
}

public sealed record ProjectionCacheOptions(int MaximumEntities = 65_536, int MaximumOccurrences = 262_144);
public readonly record struct ProjectionCacheStatistics(long Projections, long ProjectionHits, long OccurrenceHits);

internal sealed class ProjectionCache
{
    private sealed record Entry(long Revision, ImmutableArray<ShadowDependency> Dependencies,
        bool Handled, CadGeometry Geometry, string? Diagnostic);
    private sealed record OccurrenceEntry(CadGeometry Local, Affine3 Transform, CadOccurrence Occurrence);
    private readonly object gate = new();
    private readonly ProjectionCacheOptions options;
    private readonly Dictionary<(IEntityProjector Projector, Guid Document, ObjectId Entity), Entry> projections = new();
    private readonly Dictionary<(Guid Document, string Key), OccurrenceEntry> occurrences = new();
    private long builds, hits, instanceHits;

    public ProjectionCache(ProjectionCacheOptions options)
    {
        if (options.MaximumEntities < 1 || options.MaximumOccurrences < 1) throw new ArgumentOutOfRangeException(nameof(options));
        this.options = options;
    }
    public ProjectionCacheStatistics Statistics { get { lock (gate) return new(builds, hits, instanceHits); } }

    public bool TryProject(IEntityProjector projector, ShadowReader entity, out CadGeometry geometry, out string? diagnostic, IShadowReadObserver? observer = null)
    {
        if (projector is not ICacheableEntityProjector cacheable || entity.Snapshot is not { } snapshot || entity.Id is not { } id)
            return projector.TryProject(entity, out geometry, out diagnostic);
        lock (gate)
        {
            var key = (projector, snapshot.DocumentId, id);
            var revision = cacheable.Revision;
            if (projections.TryGetValue(key, out var previous) && previous.Revision == revision && previous.Dependencies.All(d => d.IsCurrent(snapshot)))
            {
                foreach (var dependency in previous.Dependencies) observer?.Read(dependency.Id, dependency.Node);
                hits++; geometry = previous.Geometry; diagnostic = previous.Diagnostic;
                return previous.Handled;
            }
            var reads = new ShadowReadSet();
            reads.Read(id, entity.Object);
            var handled = projector.TryProject(entity.Observe(reads), out geometry, out diagnostic);
            builds++;
            if (projections.Count >= options.MaximumEntities) projections.Clear();
            var dependencies = reads.Freeze();
            foreach (var dependency in dependencies) observer?.Read(dependency.Id, dependency.Node);
            projections[key] = new(revision, dependencies, handled, geometry, diagnostic);
            return handled;
        }
    }

    public CadOccurrence Occurrence(Guid document, string key, ObjectId entity, ImmutableArray<ObjectId> path,
        CadStyle style, CadGeometry local, Affine3 transform)
    {
        lock (gate)
        {
            var cacheKey = (document, key);
            if (occurrences.TryGetValue(cacheKey, out var previous) && ReferenceEquals(previous.Local, local) && previous.Transform == transform)
            {
                var old = previous.Occurrence;
                if (old.Entity == entity && old.InsertPath.AsSpan().SequenceEqual(path.AsSpan()) && Equivalent(old.Style, style))
                { instanceHits++; return old; }
                var changed = old with { Entity = entity, InsertPath = path, Style = style };
                occurrences[cacheKey] = previous with { Occurrence = changed };
                return changed;
            }
            var result = new CadOccurrence(key, entity, path, style, transform == Affine3.Identity ? local : local.Transform(transform)) { SourceToWorld = transform };
            if (occurrences.Count >= options.MaximumOccurrences) occurrences.Clear();
            occurrences[cacheKey] = new(local, transform, result);
            return result;
        }
    }
    private static bool Equivalent(CadStyle a, CadStyle b) => a.Argb == b.Argb && a.Lineweight == b.Lineweight &&
        a.Visible == b.Visible && a.Layer == b.Layer && a.Dashes.AsSpan().SequenceEqual(b.Dashes.AsSpan()) &&
        CadLinetype.Equivalent(a.Linetype, b.Linetype);
}

// The stock projector is pure and all its dependencies flow through ShadowReader.
internal sealed class StandardProjectionCacheAdapter : ICacheableEntityProjector
{
    private readonly StandardEntityProjector projector = new();
    private readonly ViewportEntityProjector viewports = new();
    public long Revision => 0;
    public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
        => viewports.TryProject(entity, out geometry, out diagnostic) || projector.TryProject(entity, out geometry, out diagnostic);
}
