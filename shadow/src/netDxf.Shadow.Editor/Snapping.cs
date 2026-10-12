using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Editor;

[Flags]
public enum SnapMode { None = 0, Vertex = 1, Midpoint = 2, Nearest = 4, Grid = 8, All = Vertex | Midpoint | Nearest | Grid }
public sealed record SnapResult(Vec3 Position, double Distance, SnapMode Mode, string? Occurrence);

public static class Snapping
{
    /// <summary>Snaps to the compiled scene. Curve vertices/midpoints are sampled, not exact analytic extrema.</summary>
    public static SnapResult? Find(RenderScene scene, Vec3 point, double tolerance, SnapMode mode = SnapMode.All,
        double gridSpacing = 10, IReadOnlySet<string>? excluded = null)
    {
        if (!point.IsFinite || !double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        SnapResult? best = null;
        void Candidate(Vec3 candidate, SnapMode kind, string? key)
        {
            var distance = Vec3.Distance(point with { Z = 0 }, candidate with { Z = 0 });
            if (distance <= tolerance && (best is null || distance < best.Distance)) best = new(candidate, distance, kind, key);
        }
        if (mode.HasFlag(SnapMode.Grid) && double.IsFinite(gridSpacing) && gridSpacing > 0)
            Candidate(new(Math.Round(point.X / gridSpacing) * gridSpacing, Math.Round(point.Y / gridSpacing) * gridSpacing, point.Z), SnapMode.Grid, null);
        var box = new Bounds2(point.X, point.Y, point.X, point.Y).Inflate(tolerance);
        foreach (var index in scene.Index.Query(box))
        {
            var item = scene.Items[index];
            if (!item.Style.Visible || excluded?.Contains(item.Key) == true) continue;
            foreach (var contour in item.Contours)
            {
                if (mode.HasFlag(SnapMode.Vertex)) foreach (var vertex in contour.Points) Candidate(vertex, SnapMode.Vertex, item.Key);
                for (var i = 1; i < contour.Points.Length; i++)
                {
                    var a = contour.Points[i - 1]; var b = contour.Points[i];
                    if (mode.HasFlag(SnapMode.Midpoint)) Candidate((a + b) / 2, SnapMode.Midpoint, item.Key);
                    if (mode.HasFlag(SnapMode.Nearest))
                    {
                        var delta = b - a; var length2 = delta.X * delta.X + delta.Y * delta.Y;
                        if (length2 > 0) Candidate(a + delta * Math.Clamp(((point.X - a.X) * delta.X + (point.Y - a.Y) * delta.Y) / length2, 0, 1), SnapMode.Nearest, item.Key);
                    }
                }
            }
        }
        return best;
    }
}
