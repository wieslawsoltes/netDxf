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

/// <summary>Owns per-consumer caches. Snapshots and analytic source geometry remain backend-independent.</summary>
public sealed class RenderCompiler
{
    private sealed class Cache { public readonly Dictionary<RenderCompileOptions, RenderScene> Scenes = new(); }
    private readonly ConditionalWeakTable<ShadowSnapshot, Cache> snapshots = new();
    private readonly CadSceneBuilder builder;
    public RenderCompiler(CadSceneBuilder? builder = null) => this.builder = builder ?? new();

    public RenderScene Compile(ShadowSnapshot snapshot, RenderCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (!(options.Tolerance > 0) || !double.IsFinite(options.Tolerance)) throw new ArgumentOutOfRangeException(nameof(options));
        var cache = snapshots.GetValue(snapshot, _ => new());
        lock (cache.Scenes)
        {
            if (cache.Scenes.TryGetValue(options, out var scene)) return scene;
            scene = Compile(builder.Build(snapshot, new(options.Layout), cancellationToken), new(options.Tolerance), cancellationToken);
            if (cache.Scenes.Count >= 8) cache.Scenes.Clear();
            cache.Scenes[options] = scene;
            return scene;
        }
    }

    public static RenderScene Compile(CadScene scene, SamplingOptions? sampling = null, CancellationToken cancellationToken = default)
    {
        sampling ??= new();
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
                    contours.Add(new(points.ToImmutable(), contour.Closed));
                }
                if (occurrence.Geometry.Text is { } text)
                {
                    var lines = TextLayout.PlainText(text.Content, text.Multiline).Split('\n');
                    // Conservative culling only; backend glyph measurement determines actual placement.
                    var width = Math.Max(text.Width, lines.Select(l => l.Length).DefaultIfEmpty(1).Max() * text.Height * 1.5);
                    var height = text.Height * (lines.Length + 1) * 1.5;
                    foreach (var x in new[] { -width, width }) foreach (var y in new[] { -height, height })
                        bounds = bounds.Include(text.Origin + text.XAxis * x + text.YAxis * y);
                }
                if (occurrence.Geometry.Image is { } image) foreach (var point in image.Corners) bounds = bounds.Include(point);
                total = total.Union(bounds);
                items.Add(new(occurrence.Key, occurrence.Entity, occurrence.InsertPath, occurrence.Style,
                    contours.ToImmutable(), occurrence.Geometry.Filled, occurrence.Geometry.Text, occurrence.Geometry.Image, bounds));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                diagnostics.Add(new("render.geometry", error.Message, occurrence.Entity, DiagnosticSeverity.Error));
            }
        }
        var result = items.ToImmutable();
        return new(scene.DocumentId, scene.Revision, scene.Layout, result, total, new(result.Select(i => i.Bounds)), diagnostics.ToImmutable());
    }

    public static RenderItem? HitTest(RenderScene scene, Vec3 worldPoint, double tolerance)
    {
        if (!(tolerance > 0) || !double.IsFinite(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var rectangle = new Bounds2(worldPoint.X, worldPoint.Y, worldPoint.X, worldPoint.Y).Inflate(tolerance);
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
                if (item.Filled && contour.Closed)
                {
                    var points = contour.Points;
                    for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                    {
                        var a = points[i]; var b = points[j];
                        if ((a.Y > worldPoint.Y) != (b.Y > worldPoint.Y) && worldPoint.X < (b.X - a.X) * (worldPoint.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                    }
                }
            }
            if (inside) return item;
        }
        return null;
    }
}
