using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public sealed partial class RenderCompiler
{
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
