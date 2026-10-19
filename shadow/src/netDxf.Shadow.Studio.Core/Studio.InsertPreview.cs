using System.Collections.Immutable;
using System.Text;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed record CadInsertPreviewRequest(long Revision, CadInsertOperation Insert, int Width = 512, int Height = 512);
public sealed record CadInsertPreviewResult(long Revision, Vec3 Anchor, Bounds2 ImageBounds, string Svg,
    int Items, ImmutableArray<ShadowDiagnostic> Diagnostics);

public sealed partial class Studio
{
    /// <summary>Compiles only a detached proposed INSERT. No retained proposal, source edit,
    /// selection, history entry or renderer switch is created by this read operation.</summary>
    public CadInsertPreviewResult CadInsertPreview(CadInsertPreviewRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (request.Width is < 32 or > 2048 || request.Height is < 32 or > 2048 || (long)request.Width * request.Height > 4_000_000)
                throw new ArgumentException("Insertion preview dimensions require 32–2048 pixels per axis and at most four megapixels.");
            ArgumentNullException.ThrowIfNull(request.Insert);
            var plan = PlanInsert(Current, request.Insert, token);
            var proposed = plan.Patch.Apply(Current);
            var services = FrozenServices(CurrentFonts);
            var scene = services.Compiler.Compile(proposed, ViewOptions() with { RootEntities = plan.Created }, token);
            var viewport = Viewport.Fit(scene.Bounds, request.Width, request.Height, .05);
            using var backend = new SvgRenderBackend();
            var image = backend.Render(new(scene, viewport, Background: 0), token);
            if (image.Bytes.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Insertion preview exceeds its eight megabyte image budget.");
            return new(Current.Revision, request.Insert.Command.Points.Single(), viewport.WorldBounds,
                Encoding.UTF8.GetString(image.Bytes), scene.Items.Length, scene.Diagnostics);
        }
    }
}
