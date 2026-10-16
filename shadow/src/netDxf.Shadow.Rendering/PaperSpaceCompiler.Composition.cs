using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

internal sealed partial class PaperSpaceCompiler
{
    internal RenderScene Compose(ShadowSnapshot snapshot, RenderScene paper, RenderCompileOptions options,
        Func<ShadowSnapshot, double, double, RenderScene> compileModel, CancellationToken cancellationToken, ITextOutlineProvider? outlines = null)
    {
        var limits = options.PaperSpace ?? throw new ArgumentException("Paper-space budgets must be provided.", nameof(options));
        if (limits.MaximumViewports < 1 || limits.MaximumItems < 1 || limits.MaximumCandidates < 1 ||
            limits.MaximumClipPoints < 4 || limits.MaximumPatternScales < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Paper-space budgets must be positive.");
        if (paper.Items.Length > limits.MaximumItems) throw new InvalidOperationException("Paper-space item budget exceeded.");
        var overrides = options.ViewportOverrides;
        overrides?.Validate(snapshot, cancellationToken);
        limits.Perspective?.Validate();
        var projectiveBudget = limits.Perspective is { } policy ? new PerspectiveWorkBudget(policy) : null;
        var diagnostics = paper.Diagnostics.ToBuilder();
        var active = ReadViews(snapshot, options, limits, diagnostics, cancellationToken);
        if (active.Count == 0) return paper with { Diagnostics = diagnostics.ToImmutable() };
        // Conservative visible-frustum sampling amplification; the source sampler keeps its own quotas.
        var scale = active.Max(v => v.Projection.Perspective is { } camera
            ? v.Projection.Scale / camera.NearDistanceFraction * (1 +
                Math.Max(Math.Max(Math.Abs(v.Clip.Bounds.MinX - camera.Center.X), Math.Abs(v.Clip.Bounds.MaxX - camera.Center.X)),
                    Math.Max(Math.Abs(v.Clip.Bounds.MinY - camera.Center.Y), Math.Abs(v.Clip.Bounds.MaxY - camera.Center.Y))) /
                    (v.Projection.Scale * camera.EyeDistance)) : v.Projection.Scale);
        var tolerance = options.Tolerance / scale;
        if (!(tolerance > 0) || !double.IsFinite(tolerance)) throw new InvalidOperationException("Viewport sampling tolerance is outside the numeric range.");
        var paperLinetypes = snapshot.Document["DrawingVariables"]["PsLtScale"].Number(1) != 0;
        var variants = new HashSet<(double Scale, ViewportLayerProfile Profile)>();
        foreach (var view in active)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var factor = paperLinetypes ? 1 / view.Projection.Scale : 1;
            if (!(factor > 0) || !double.IsFinite(factor)) throw new InvalidOperationException("Viewport linetype scale is outside the numeric range.");
            var profile = overrides?.For(view.Projection.ViewportEntity) ?? ViewportLayerProfile.Empty;
            if (variants.Add((factor, profile)) && variants.Count > limits.MaximumPatternScales)
                throw new InvalidOperationException("Paper-space distinct pattern scale/override profile budget exceeded.");
        }
        // Override layers before inherited style resolution and glyph generation. Compiling equivalent
        // scale/profile pairs once preserves both native/SHX decorations and ByBlock inheritance.
        var models = new Dictionary<(double Scale, ViewportLayerProfile Profile), RenderScene>();
        var seenDiagnostics = diagnostics.ToHashSet();
        var result = ImmutableArray.CreateBuilder<RenderItem>(); var candidates = 0;
        foreach (var originalView in active.OrderByDescending(v => v.Stacking))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var factor = paperLinetypes ? 1 / originalView.Projection.Scale : 1;
            var profile = overrides?.For(originalView.Projection.ViewportEntity) ?? ViewportLayerProfile.Empty;
            var view = overrides?.Thaw(snapshot, originalView, profile) ?? originalView;
            if (!models.TryGetValue((factor, profile), out var model))
            {
                var modelSnapshot = overrides?.CreateView(snapshot, profile, cancellationToken) ?? snapshot;
                model = compileModel(modelSnapshot, tolerance, factor);
                models.Add((factor, profile), model);
                foreach (var diagnostic in model.Diagnostics)
                    if (seenDiagnostics.Add(diagnostic)) diagnostics.Add(diagnostic);
            }
            foreach (var index in Candidates(model, view, options.Tolerance))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++candidates > limits.MaximumCandidates) throw new InvalidOperationException("Paper-space candidate budget exceeded.");
                var source = model.Items[index];
                if (!source.Style.Visible || Frozen(snapshot, source, view.FrozenLayers)) continue;
                var key = (snapshot.DocumentId, view.Projection.ViewportEntity, source.Key);
                if (!items.TryGetValue(key, out var cached) || !ReferenceEquals(source, cached.Source) || !ReferenceEquals(view, cached.View))
                {
                    RenderItem? projected;
                    try
                    {
                        projected = view.Projection.Perspective is not null
                            ? PerspectiveItemProjection.Project(source, view, projectiveBudget!, outlines, tolerance, cancellationToken)
                            : PaperItemProjection.Project(source, view);
                    }
                    catch (NotSupportedException error)
                    { diagnostics.Add(new("viewport.projective-paint", error.Message, source.Entity, DiagnosticSeverity.Error)); continue; }
                    if (items.Count >= 262_144) items.Clear();
                    items[key] = cached = new(source, view, projected);
                }
                else if (view.Projection.Perspective is not null) projectiveBudget!.Charge(PerspectiveItemProjection.PointCount(cached.Result));
                if (cached.Result is { } item)
                {
                    if (result.Count >= limits.MaximumItems - paper.Items.Length) throw new InvalidOperationException("Paper-space item budget exceeded.");
                    result.Add(item);
                }
            }
        }
        result.AddRange(paper.Items);
        var immutable = result.ToImmutable(); var bounds = Bounds2.Empty;
        foreach (var item in immutable) bounds = bounds.Union(item.Bounds);
        return paper with { Items = immutable, Bounds = bounds, Index = new(immutable.Select(i => i.Bounds)), Diagnostics = diagnostics.ToImmutable() };
    }
}
