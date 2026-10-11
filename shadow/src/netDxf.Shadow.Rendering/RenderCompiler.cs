using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public readonly record struct RenderCompilerStatistics(long GeometryCompilations, long ItemHits);

/// <summary>Consumer-owned ephemeron caches reuse analytic and sampled geometry across revisions.</summary>
public sealed class RenderCompiler
{
    private sealed class Cache { public readonly Dictionary<(RenderCompileOptions Options, long Projection, long Text), RenderScene> Scenes = new(); }
    private sealed class GeometryCache { public readonly Dictionary<(SamplingOptions Sampling, long Text), RenderGeometryData> Values = new(); }
    private sealed class ItemCache { public readonly Dictionary<(SamplingOptions Sampling, long Text), RenderItem> Values = new(); }
    private readonly ConditionalWeakTable<ShadowSnapshot, Cache> snapshots = new();
    private readonly ConditionalWeakTable<CadGeometry, GeometryCache> geometries = new();
    private readonly ConditionalWeakTable<CadOccurrence, ItemCache> occurrences = new();
    private readonly CadSceneBuilder builder;
    private readonly ITextLayoutEngine textEngine;
    private readonly object gate = new();
    private readonly Dictionary<(Guid Document, RenderCompileOptions Options), RenderScene> previousScenes = new();
    private long geometryCompilations, itemHits;
    public RenderCompiler(CadSceneBuilder? builder = null, ITextLayoutEngine? textLayout = null)
    {
        this.builder = builder ?? new CadSceneBuilder();
        textEngine = textLayout ?? ApproximateTextLayoutEngine.Instance;
    }
    public RenderCompilerStatistics CacheStatistics => new(Interlocked.Read(ref geometryCompilations), Interlocked.Read(ref itemHits));

    public RenderScene Compile(ShadowSnapshot snapshot, RenderCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new();
        if (!(options.Tolerance > 0) || !double.IsFinite(options.Tolerance)) throw new ArgumentOutOfRangeException(nameof(options));
        lock (gate)
        {
            var cache = snapshots.GetValue(snapshot, _ => new Cache());
            var textRevision = textEngine.Revision;
            var key = (options, builder.ProjectionVersion, textRevision);
            if (builder.IsSnapshotCacheable && cache.Scenes.TryGetValue(key, out var cached)) return cached;
            var scene = builder.Build(snapshot, new SceneOptions(options.Layout), cancellationToken);
            var previousKey = (snapshot.DocumentId, options);
            previousScenes.TryGetValue(previousKey, out var previous);
            var result = CompileCore(scene, new(options.Tolerance), (o, s) => CompileCached(o, s, textRevision), previous, cancellationToken);
            if (textEngine.Revision != textRevision) throw new InvalidOperationException("Font services changed while compiling. Retry with the current font revision.");
            if (cache.Scenes.Count >= 8) cache.Scenes.Clear();
            if (builder.IsSnapshotCacheable) cache.Scenes[key] = result;
            if (previousScenes.Count >= 8) previousScenes.Clear();
            previousScenes[previousKey] = result;
            return result;
        }
    }

    private RenderItem CompileCached(CadOccurrence occurrence, SamplingOptions sampling, long textRevision)
    {
        // Font invalidation does not invalidate non-text geometry.
        var key = (sampling, occurrence.Geometry.Text is null ? 0 : textRevision);
        var cache = occurrences.GetValue(occurrence, _ => new ItemCache());
        if (cache.Values.TryGetValue(key, out var item)) { Interlocked.Increment(ref itemHits); return item; }
        var geometry = geometries.GetValue(occurrence.Geometry, _ => new GeometryCache());
        if (!geometry.Values.TryGetValue(key, out var data))
        {
            data = RenderGeometryCompiler.Compile(occurrence.Geometry, sampling, textEngine);
            Interlocked.Increment(ref geometryCompilations);
            if (geometry.Values.Count >= 8) geometry.Values.Clear();
            geometry.Values[key] = data;
        }
        item = CreateItem(occurrence, data);
        if (cache.Values.Count >= 8) cache.Values.Clear();
        cache.Values[key] = item;
        return item;
    }
    private static RenderItem CreateItem(CadOccurrence occurrence, RenderGeometryData data)
    {
        var item = new RenderItem(occurrence.Key, occurrence.Entity, occurrence.InsertPath, occurrence.Style,
            data.Contours, occurrence.Geometry.Filled, occurrence.Geometry.Text, occurrence.Geometry.Image, data.Bounds)
            { Regions = data.Regions, TextLayout = data.TextLayout };
        // Invalid patterns are diagnosed per entity during compilation, not as a frame-wide backend failure.
        _ = StrokePattern.GetContours(item);
        return item;
    }

    public static RenderScene Compile(CadScene scene, SamplingOptions? sampling = null, CancellationToken cancellationToken = default,
        ITextLayoutEngine? textLayout = null)
    {
        textLayout ??= ApproximateTextLayoutEngine.Instance;
        return CompileCore(scene, sampling ?? new(), (o, s) => CreateItem(o, RenderGeometryCompiler.Compile(o.Geometry, s, textLayout)), null, cancellationToken);
    }

    private static RenderScene CompileCore(CadScene scene, SamplingOptions sampling,
        Func<CadOccurrence, SamplingOptions, RenderItem> project, RenderScene? previous, CancellationToken cancellationToken)
    {
        var items = ImmutableArray.CreateBuilder<RenderItem>(scene.Occurrences.Length);
        var diagnostics = scene.Diagnostics.ToBuilder();
        var total = Bounds2.Empty;
        foreach (var occurrence in scene.Occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var item = project(occurrence, sampling);
                items.Add(item); total = total.Union(item.Bounds);
                if (item.TextLayout is { } layout)
                    foreach (var issue in layout.Diagnostics)
                        diagnostics.Add(new(issue.Code, $"At {issue.Offset}: {issue.Message}", item.Entity));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { diagnostics.Add(new("render.geometry", error.Message, occurrence.Entity, DiagnosticSeverity.Error)); }
        }
        var immutable = items.ToImmutable();
        SpatialIndex? index = null;
        if (previous is not null && previous.Items.Length == immutable.Length)
        {
            var changes = new List<KeyValuePair<int, Bounds2>>();
            var sameOrder = true;
            for (var i = 0; i < immutable.Length; i++)
            {
                if (previous.Items[i].Key != immutable[i].Key) { sameOrder = false; break; }
                if (previous.Items[i].Bounds != immutable[i].Bounds) changes.Add(new(i, immutable[i].Bounds));
            }
            if (sameOrder) index = previous.Index.Refit(changes);
        }
        return new(scene.DocumentId, scene.Revision, scene.Layout, immutable, total,
            index ?? new SpatialIndex(immutable.Select(item => item.Bounds)), diagnostics.ToImmutable());
    }

    public static RenderItem? HitTest(RenderScene scene, Vec3 worldPoint, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!worldPoint.IsFinite || !(tolerance > 0) || !double.IsFinite(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var rectangle = new Bounds2(worldPoint.X, worldPoint.Y, worldPoint.X, worldPoint.Y).Inflate(tolerance);
        foreach (var index in scene.Index.Query(rectangle).OrderDescending())
        {
            var item = scene.Items[index];
            if (item.Style.Visible && RenderHitTesting.Contains(item, worldPoint, tolerance)) return item;
        }
        return null;
    }
}
