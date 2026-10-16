using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

internal sealed partial class PaperSpaceCompiler
{
    private List<PaperViewport> ReadViews(ShadowSnapshot snapshot, RenderCompileOptions options,
        PaperSpaceOptions limits, ImmutableArray<ShadowDiagnostic>.Builder diagnostics, CancellationToken cancellationToken)
    {
        var active = new List<PaperViewport>(); var viewCount = 0;
        foreach (var entity in snapshot.DrawingEntities(options.Layout))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entity.Type != "Viewport" || entity.Id is not { } id) continue;
            if (++viewCount > limits.MaximumViewports) throw new InvalidOperationException("Paper-space viewport budget exceeded.");
            // Stacking 1 denotes the paper layout's own viewport; nonpositive values do not display an active model view.
            if (entity["Stacking"].Number(2) <= 1 || PaperViewport.HasFlag(entity["Status"], "ViewportOff", 131072)) continue;
            try
            {
                var key = (snapshot.DocumentId, id, options.Tolerance, limits.MaximumClipPoints, limits.Perspective);
                if (!views.TryGetValue(key, out var cached) || !cached.Dependencies.All(d => d.IsCurrent(snapshot)))
                {
                    var reads = new ShadowReadSet();
                    var view = PaperViewport.Read(entity.Observe(reads), options.Tolerance, limits.MaximumClipPoints, limits.Perspective);
                    if (views.Count >= 1024) views.Clear();
                    views[key] = cached = new(reads.Freeze(), view);
                }
                active.Add(cached.View);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            { diagnostics.Add(new("viewport.unsupported", error.Message, id, DiagnosticSeverity.Error)); }
        }
        return active;
    }

    private static IEnumerable<int> Candidates(RenderScene model, PaperViewport view, double tolerance)
    {
        if (view.Projection.Perspective is not null) return Enumerable.Range(0, model.Items.Length);
        var matrix = view.Projection.ModelToPaper;
        if (Math.Abs(matrix.Z.X) > 1e-14 || Math.Abs(matrix.Z.Y) > 1e-14) return Enumerable.Range(0, model.Items.Length);
        var clip = view.Clip.Bounds; var bounds = Bounds2.Empty;
        foreach (var x in new[] { clip.MinX, clip.MaxX }) foreach (var y in new[] { clip.MinY, clip.MaxY })
            bounds = bounds.Include(view.Projection.PaperToModel.Point(new(x, y)));
        return model.Index.Query(bounds.Inflate(tolerance / view.Projection.Scale)).Order();
    }
    private static bool Frozen(ShadowSnapshot snapshot, RenderItem item, ImmutableHashSet<string> frozen)
    {
        if (frozen.Contains(item.Style.Layer)) return true;
        var inherited = "0";
        foreach (var id in item.InsertPath)
        {
            var layer = snapshot.Read(id)["Layer"]["Name"].Text;
            if (layer == "0") layer = inherited;
            if (frozen.Contains(layer)) return true;
            inherited = layer;
        }
        return false;
    }
}
