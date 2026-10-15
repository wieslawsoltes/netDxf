using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public readonly record struct RenderCompilerStatistics(long GeometryCompilations, long ItemHits);

/// <summary>Consumer-owned ephemeron caches reuse analytic and sampled geometry across revisions.</summary>
public sealed partial class RenderCompiler
{
    private sealed class Cache { public readonly Dictionary<(RenderCompileOptions Options, long Projection, long Text, long Linetype), RenderScene> Scenes = new(); }
    private sealed class GeometryCache { public readonly Dictionary<(SamplingOptions Sampling, long Text), RenderGeometryData> Values = new(); }
    private sealed class ItemCache { public readonly Dictionary<(SamplingOptions Sampling, long Text), RenderItem> Values = new(); }
    private readonly ConditionalWeakTable<ShadowSnapshot, Cache> snapshots = new();
    private readonly ConditionalWeakTable<CadGeometry, GeometryCache> geometries = new();
    private readonly ConditionalWeakTable<CadOccurrence, ItemCache> occurrences = new();
    private readonly CadSceneBuilder builder;
    private readonly PaperSpaceCompiler paperSpace = new();
    private readonly ITextLayoutEngine textEngine;
    private readonly LinetypeRenderCompiler linetypes;
    private readonly object gate = new();
    private readonly Dictionary<(Guid Document, RenderCompileOptions Options), RenderScene> previousScenes = new();
    private long geometryCompilations, itemHits;

    public RenderCompiler(CadSceneBuilder? builder, ITextLayoutEngine? textLayout)
        : this(builder, textLayout, null, null) { }

    public RenderCompiler(CadSceneBuilder? builder = null, ITextLayoutEngine? textLayout = null,
        ILinetypeDecorationProvider? linetypeResources = null, LinetypePlanOptions? linetypeOptions = null)
    {
        this.builder = builder ?? new CadSceneBuilder();
        textEngine = textLayout ?? ApproximateTextLayoutEngine.Instance;
        linetypes = new(linetypeResources ?? this.builder.Projectors.OfType<ILinetypeDecorationProvider>().FirstOrDefault(), linetypeOptions);
    }
    public RenderCompilerStatistics CacheStatistics => new(Interlocked.Read(ref geometryCompilations), Interlocked.Read(ref itemHits));

    public RenderScene Compile(ShadowSnapshot snapshot, RenderCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new();
        if (!(options.Tolerance > 0) || !double.IsFinite(options.Tolerance) ||
            !(options.LinetypeScale > 0) || !double.IsFinite(options.LinetypeScale) || !Enum.IsDefined(options.LinetypeAlignment)) throw new ArgumentOutOfRangeException(nameof(options));
        lock (gate)
        {
            var cache = snapshots.GetValue(snapshot, _ => new Cache());
            var textRevision = textEngine.Revision;
            var projectionVersion = builder.ProjectionVersion;
            var linetypeRevision = linetypes.Revision;
            var key = (options, projectionVersion, textRevision, linetypeRevision);
            if (builder.IsSnapshotCacheable && cache.Scenes.TryGetValue(key, out var cached)) return cached;
            var annotation = string.Equals(options.Layout, "Model", StringComparison.OrdinalIgnoreCase)
                ? options.ModelAnnotation?.Resolve(snapshot) : null;
            var effectiveScale = options.LinetypeScale * (annotation?.Multiplier ?? 1);
            if (!(effectiveScale > 0) || !double.IsFinite(effectiveScale))
                throw new ArgumentOutOfRangeException(nameof(options), "Combined annotation and linetype scale is outside the numeric range.");
            var scene = builder.Build(snapshot, new SceneOptions(options.Layout) { LinetypeScale = effectiveScale, LinetypeAlignment = options.LinetypeAlignment }, cancellationToken);
            if (annotation?.Diagnostic is { } message)
                scene = scene with { Diagnostics = scene.Diagnostics.Add(new("linetype.annotation", message)) };
            var previousKey = (snapshot.DocumentId, options);
            previousScenes.TryGetValue(previousKey, out var previous);
            var result = CompileCore(scene, new(options.Tolerance), (o, s) => CompileCached(o, s, textRevision), previous, linetypes, cancellationToken);
            if (options.IncludePaperViewports && !string.Equals(options.Layout, "Model", StringComparison.OrdinalIgnoreCase))
                result = paperSpace.Compose(snapshot, result, options, (modelSnapshot, tolerance, scale) => Compile(modelSnapshot,
                    options with { Layout = "Model", Tolerance = tolerance, IncludePaperViewports = false,
                        LinetypeScale = options.LinetypeScale * scale, ModelAnnotation = null, ViewportOverrides = null }, cancellationToken), cancellationToken);
            if (builder.ProjectionVersion != projectionVersion) throw new InvalidOperationException("Projection resources changed while compiling. Retry with the current resource revision.");
            if (textEngine.Revision != textRevision) throw new InvalidOperationException("Font services changed while compiling. Retry with the current font revision.");
            if (linetypes.Revision != linetypeRevision) throw new InvalidOperationException("Linetype resources changed while compiling. Retry with the current resource revision.");
            if (cache.Scenes.Count >= 8) cache.Scenes.Clear();
            if (builder.IsSnapshotCacheable) cache.Scenes[key] = result;
            if (previousScenes.Count >= 8) previousScenes.Clear();
            previousScenes[previousKey] = result;
            return result;
        }
    }

    private RenderItem CompileCached(CadOccurrence occurrence, SamplingOptions sampling, long textRevision)
    {
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
        item = CreateItem(occurrence, data, linetypes.Options.MetricSpace);
        if (cache.Values.Count >= 8) cache.Values.Clear();
        cache.Values[key] = item;
        return item;
    }
    private static RenderItem CreateItem(CadOccurrence occurrence, RenderGeometryData data, LinetypeMetricSpace metricSpace = LinetypeMetricSpace.World)
    {
        var item = new RenderItem(occurrence.Key, occurrence.Entity, occurrence.InsertPath, occurrence.Style,
            data.Contours, occurrence.Geometry.Filled, occurrence.Geometry.Text, occurrence.Geometry.Image, data.Bounds)
            { Regions = data.Regions, Strokes = data.Strokes, TextLayout = data.TextLayout, SourceToWorld = occurrence.SourceToWorld,
                IsPresentationOnly = occurrence.IsPresentationOnly, PatternToWorld = metricSpace == LinetypeMetricSpace.Definition ? occurrence.SourceToWorld : Affine3.Identity };
        _ = StrokePattern.GetContours(item);
        return item;
    }

}
