using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

internal sealed partial class PaperSpaceCompiler
{
    internal RenderScene Compose(ShadowSnapshot snapshot, RenderScene paper, RenderCompileOptions options,
        Func<double, RenderScene> compileModel, CancellationToken cancellationToken)
    {
        var limits = options.PaperSpace ?? throw new ArgumentException("Paper-space budgets must be provided.", nameof(options));
        if (limits.MaximumViewports < 1 || limits.MaximumItems < 1 || limits.MaximumCandidates < 1 || limits.MaximumClipPoints < 4)
            throw new ArgumentOutOfRangeException(nameof(options), "Paper-space budgets must be positive.");
        if (paper.Items.Length > limits.MaximumItems) throw new InvalidOperationException("Paper-space item budget exceeded.");
        var diagnostics = paper.Diagnostics.ToBuilder();
        var active = ReadViews(snapshot, options, limits, diagnostics, cancellationToken);
        if (active.Count == 0) return paper with { Diagnostics = diagnostics.ToImmutable() };
        var scale = active.Max(v => v.Projection.Scale);
        var tolerance = options.Tolerance / scale;
        if (!(tolerance > 0) || !double.IsFinite(tolerance)) throw new InvalidOperationException("Viewport sampling tolerance is outside the numeric range.");
        var model = compileModel(tolerance);
        var seenDiagnostics = diagnostics.ToHashSet();
        foreach (var diagnostic in model.Diagnostics)
            if (seenDiagnostics.Add(diagnostic)) diagnostics.Add(diagnostic);
        var paperLinetypes = snapshot.Document["DrawingVariables"]["PsLtScale"].Number(1) != 0;
        var result = ImmutableArray.CreateBuilder<RenderItem>(); var candidates = 0;
        foreach (var view in active.OrderByDescending(v => v.Stacking))
        {
            foreach (var index in Candidates(model, view, options.Tolerance))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++candidates > limits.MaximumCandidates) throw new InvalidOperationException("Paper-space candidate budget exceeded.");
                var source = model.Items[index];
                if (!source.Style.Visible || Frozen(snapshot, source, view.FrozenLayers)) continue;
                var key = (snapshot.DocumentId, view.Projection.ViewportEntity, source.Key);
                if (!items.TryGetValue(key, out var cached) || !ReferenceEquals(source, cached.Source) ||
                    !ReferenceEquals(view, cached.View) || paperLinetypes != cached.PaperLinetypes)
                {
                    var projected = PaperItemProjection.Project(source, view, paperLinetypes);
                    if (items.Count >= 262_144) items.Clear();
                    items[key] = cached = new(source, view, paperLinetypes, projected);
                }
                if (cached.Result is { } item)
                {
                    if (result.Count >= limits.MaximumItems - paper.Items.Length) throw new InvalidOperationException("Paper-space item budget exceeded.");
                    result.Add(item);
                }
            }
        }
        // Paper annotations and viewport frames overlay the projected model views.
        result.AddRange(paper.Items);
        var immutable = result.ToImmutable(); var bounds = Bounds2.Empty;
        foreach (var item in immutable) bounds = bounds.Union(item.Bounds);
        return paper with { Items = immutable, Bounds = bounds, Index = new(immutable.Select(i => i.Bounds)), Diagnostics = diagnostics.ToImmutable() };
    }

}
