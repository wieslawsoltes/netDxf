using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public sealed record RenderContour(ImmutableArray<Vec3> Points, bool Closed);
public sealed record RenderItem(string Key, ObjectId Entity, ImmutableArray<ObjectId> InsertPath, CadStyle Style,
    ImmutableArray<RenderContour> Contours, bool Filled, CadText? Text, CadImage? Image, Bounds2 Bounds);
public sealed record RenderScene(Guid DocumentId, long Revision, string Layout, ImmutableArray<RenderItem> Items,
    Bounds2 Bounds, SpatialIndex Index, ImmutableArray<ShadowDiagnostic> Diagnostics);
public sealed record RenderCompileOptions(string Layout = "Model", double Tolerance = 0.05);

/// <summary>Owns per-consumer caches. No backend handles are stored in the immutable document.</summary>
public sealed class RenderCompiler
{
    private sealed class Cache { public readonly Dictionary<RenderCompileOptions, RenderScene> Scenes = new(); }
    private readonly ConditionalWeakTable<ShadowSnapshot, Cache> snapshots = new();
    private readonly CadSceneBuilder builder;
    public RenderCompiler(CadSceneBuilder? builder = null) => this.builder = builder ?? new CadSceneBuilder();

    public RenderScene Compile(ShadowSnapshot snapshot, RenderCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new RenderCompileOptions();
        if (!(options.Tolerance > 0) || !double.IsFinite(options.Tolerance)) throw new ArgumentOutOfRangeException(nameof(options));
        var cache = snapshots.GetValue(snapshot, _ => new Cache());
        lock (cache.Scenes)
        {
            if (cache.Scenes.TryGetValue(options, out var result)) return result;
            result = Compile(builder.Build(snapshot, new SceneOptions(options.Layout), cancellationToken), new SamplingOptions(options.Tolerance), cancellationToken);
            if (cache.Scenes.Count >= 8) cache.Scenes.Clear();
            cache.Scenes[options] = result;
            return result;
        }
    }

    public static RenderScene Compile(CadScene scene, SamplingOptions? sampling = null, CancellationToken cancellationToken = default)
    {
        sampling ??= new SamplingOptions();
        var items = ImmutableArray.CreateBuilder<RenderItem>();
        var diagnostics = scene.Diagnostics.ToBuilder();
        var total = Bounds2.Empty;
        foreach (var occurrence in scene.Occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var contours = ImmutableArray.CreateBuilder<RenderContour>();
                var bounds = Bounds2.Empty;
                foreach (var contour in occurrence.Geometry.Contours)
                {
                    var points = ImmutableArray.CreateBuilder<Vec3>();
                    foreach (var curve in contour.Curves)
                    {
                        var sampled = CurveSampler.Sample(curve, sampling);
                        for (var i = 0; i < sampled.Length; i++)
                        {
                            if (i == 0 && points.Count > 0 && Vec3.Distance(points[^1], sampled[i]) < 1e-12) continue;
                            points.Add(sampled[i]); bounds = bounds.Include(sampled[i]);
                        }
                    }
                    contours.Add(new RenderContour(points.ToImmutable(), contour.Closed));
                }
                if (occurrence.Geometry.Text is { } text)
                {
                    var paragraphs = TextLayout.PlainText(text.Content, text.Multiline).Split('\n');
                    var width = Math.Max(text.Width, paragraphs.Select(p => p.Length).DefaultIfEmpty(1).Max() * text.Height * 1.5);
                    var height = text.Height * (paragraphs.Length + 1) * 1.5;
                    foreach (var dx in new[] { -width, width }) foreach (var dy in new[] { -height, height })
                        bounds = bounds.Include(text.Origin + text.XAxis * dx + text.YAxis * dy);
                }
                if (occurrence.Geometry.Image is { } image) foreach (var point in image.Corners) bounds = bounds.Include(point);
                total = total.Union(bounds);
                items.Add(new RenderItem(occurrence.Key, occurrence.Entity, occurrence.InsertPath, occurrence.Style,
                    contours.ToImmutable(), occurrence.Geometry.Filled, occurrence.Geometry.Text, occurrence.Geometry.Image, bounds));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                diagnostics.Add(new ShadowDiagnostic("render.geometry", error.Message, occurrence.Entity, DiagnosticSeverity.Error));
            }
        }
        var immutableItems = items.ToImmutable();
        return new RenderScene(scene.DocumentId, scene.Revision, scene.Layout, immutableItems, total,
            new SpatialIndex(immutableItems.Select(item => item.Bounds)), diagnostics.ToImmutable());
    }

    public static RenderItem? HitTest(RenderScene scene, Vec3 worldPoint, double tolerance)
    {
        if (!worldPoint.IsFinite || !(tolerance > 0) || !double.IsFinite(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance));
        Bounds2 rectangle = new Bounds2(worldPoint.X, worldPoint.Y, worldPoint.X, worldPoint.Y).Inflate(tolerance);
        foreach (var index in scene.Index.Query(rectangle).OrderDescending())
        {
            var item = scene.Items[index];
            if (!item.Style.Visible) continue;
            if (item.Text is not null || item.Image is not null) return item;
            var inside = false;
            foreach (var contour in item.Contours)
            {
                for (var i = 1; i < contour.Points.Length; i++)
                    if (CurveSampler.DistanceToSegment(worldPoint with { Z = 0 }, contour.Points[i - 1] with { Z = 0 }, contour.Points[i] with { Z = 0 }) <= tolerance) return item;
                if (!item.Filled || !contour.Closed) continue;
                var vertices = contour.Points;
                for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
                {
                    var a = vertices[i]; var b = vertices[j];
                    if ((a.Y > worldPoint.Y) != (b.Y > worldPoint.Y) && worldPoint.X < (b.X - a.X) * (worldPoint.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                }
            }
            if (inside) return item;
        }
        return null;
    }
}
