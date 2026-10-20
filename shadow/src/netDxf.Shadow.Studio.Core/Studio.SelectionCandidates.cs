using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

/// <summary>A live-layout overlap query. Tolerance is in drawing units, independent of elevation.</summary>
public sealed record CadSelectionCandidatesRequest(long Revision, string Layout, Vec3 Point, double Tolerance, int Limit = 32);

/// <summary>A bounded preview of the hit occurrence, not an editable copy of source geometry.</summary>
public sealed record CadSelectionPreviewPath(ImmutableArray<Vec3> Points, bool Closed);

/// <summary>Entity is the selectable root; LeafEntity and Occurrence identify the actual hit beneath that root.</summary>
public sealed record CadSelectionCandidate(string Entity, string Occurrence, string LeafEntity, string Type,
    string LeafType, string Handle, string Layer, uint Argb, Bounds2 Bounds,
    ImmutableArray<CadSelectionPreviewPath> Preview, bool PreviewTruncated);

/// <summary>Reverse painter order, deduplicated by selectable root. Truncated means further roots were not returned.</summary>
public sealed record CadSelectionCandidatesResult(Guid DocumentId, string WorkspaceId, long ViewVersion,
    long Revision, long ChangeVersion, string Layout, ImmutableArray<CadSelectionCandidate> Candidates, bool Truncated);

public sealed partial class Studio
{
    /// <summary>Finds visible selectable roots at a point without changing selection, source, or undo history.
    /// Queries examine at most 8192 spatial candidates and 250000 geometry vertices. The response is bounded to
    /// 128 KiB; previews are omitted when necessary, and clipped/text/image occurrences use their bounds.</summary>
    public CadSelectionCandidatesResult CadSelectionCandidates(CadSelectionCandidatesRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            Check(request.Revision);
            if (!string.Equals(request.Layout, layout, StringComparison.Ordinal))
                throw new ShadowConflictException("The active layout changed. Repeat the overlap pick.");
            if (!request.Point.IsFinite || !(request.Tolerance > 0) || !double.IsFinite(request.Tolerance) ||
                request.Limit is < 1 or > 64)
                throw new ArgumentException("Supply a finite point, positive tolerance, and a limit from 1 to 64.");
            var box = new Bounds2(request.Point.X, request.Point.Y, request.Point.X, request.Point.Y).Inflate(request.Tolerance);
            if (!double.IsFinite(box.MinX + box.MinY + box.MaxX + box.MaxY))
                throw new ArgumentException("The selection aperture exceeds the coordinate range.");
            token.ThrowIfCancellationRequested();
            var scene = Scene();
            var indices = scene.Index.Query(box).Take(8193).ToArray();
            if (indices.Length > 8192) throw new InvalidOperationException("Overlap candidate budget exceeded. Zoom in or use a smaller selection aperture.");
            Array.Sort(indices);
            var roots = new HashSet<ObjectId>();
            var result = ImmutableArray.CreateBuilder<CadSelectionCandidate>();
            var vertices = 0L; var previewRemaining = 1024; var truncated = false;
            for (var index = indices.Length - 1; index >= 0; index--)
            {
                token.ThrowIfCancellationRequested();
                var item = scene.Items[indices[index]];
                var root = DraftingQueries.Root(item);
                if (!item.Style.Visible || roots.Contains(root)) continue;
                // Bound the existing exact hit tester's work rather than replacing it with bounds-only hits.
                foreach (var contour in Contours(item).Concat(item.Clips.SelectMany(c => c.Contours))
                             .Concat(item.TextClip?.Contours ?? []).Concat(item.ImageClip?.Contours ?? []))
                    vertices += Math.Max(1, contour.Points.Length);
                vertices += item.Image?.Corners.Length ?? 0;
                vertices += item.Image?.ClipPolygon.Length ?? 0;
                if (vertices > 250_000) throw new InvalidOperationException("Overlap geometry budget exceeded. Use a smaller selection aperture.");
                if (!RenderHitTesting.Contains(item, request.Point, request.Tolerance)) continue;
                roots.Add(root);
                if (result.Count == request.Limit) { truncated = true; break; }
                if (item.Key.Length > 2048 || root.Value.Length > 256 || item.Entity.Value.Length > 256)
                    throw new InvalidOperationException("The overlap occurrence identifier exceeds the response budget.");
                var reader = Current.Read(root);
                var preview = ImmutableArray.CreateBuilder<CadSelectionPreviewPath>();
                var incomplete = item.Text is not null || item.Image is not null || !item.Clips.IsEmpty;
                if (!incomplete)
                {
                    var available = Math.Min(256, previewRemaining);
                    foreach (var contour in Contours(item))
                    {
                        if (contour.Points.IsEmpty) continue;
                        // Never connect skipped vertices or close an incomplete contour.
                        if (contour.Points.Length > available) { incomplete = true; break; }
                        preview.Add(new(contour.Points, contour.Closed));
                        available -= contour.Points.Length; previewRemaining -= contour.Points.Length;
                    }
                }
                result.Add(new(root.Value, item.Key, item.Entity.Value, Label(Current.Objects[root].Type),
                    Label(Current.Objects[item.Entity].Type), Label(reader["Handle"].Text), Label(item.Style.Layer),
                    item.Style.Argb, item.Bounds, preview.ToImmutable(), incomplete));
            }
            var response = new CadSelectionCandidatesResult(Current.DocumentId, WorkspaceId, viewVersion, Current.Revision,
                changeVersion, layout, result.ToImmutable(), truncated);
            if (JsonSerializer.SerializeToUtf8Bytes(response, StudioJson.CreateOptions()).Length > 128 * 1024)
                response = response with { Candidates = response.Candidates.Select(c => c with { Preview = [], PreviewTruncated = true }).ToImmutableArray() };
            if (JsonSerializer.SerializeToUtf8Bytes(response, StudioJson.CreateOptions()).Length > 128 * 1024)
                throw new InvalidOperationException("Overlap response budget exceeded. Request fewer candidates.");
            return response;
        }
        static string Label(string? value) => value is null ? "" : value.Length <= 128 ? value : value[..125] + "...";
        static IEnumerable<RenderContour> Contours(RenderItem item) =>
            item.Contours.Concat(item.Strokes.SelectMany(s => s.Contours)).Concat(item.Regions.SelectMany(r => r.Contours));
    }
}
