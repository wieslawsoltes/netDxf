using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Drafting;

public enum DraftingSelectionMode { Window, Crossing }
[Flags]
public enum ObjectSnapMode { None = 0, Endpoint = 1, Midpoint = 2, Center = 4, Quadrant = 8, Intersection = 16, Perpendicular = 32, Nearest = 64, Insertion = 128, Tangent = 256, Default = Endpoint | Midpoint | Center | Intersection }
public sealed record ObjectSnap(Vec3 Position, string Kind, string Occurrence, double Distance, bool Approximate = false);

/// <summary>Read-only, source-aware query services over a compiled immutable scene. The spatial
/// index is broad phase only: crossing selection tests visible geometry, not just bounding boxes.</summary>
public static class DraftingQueries
{
    private sealed class Groups(RenderScene scene)
    {
        public readonly ImmutableDictionary<ObjectId, ImmutableArray<int>> Items = scene.Items
            .Select((item, index) => (Id: Root(item), Index: index)).GroupBy(v => v.Id)
            .ToImmutableDictionary(g => g.Key, g => g.Select(v => v.Index).ToImmutableArray());
    }
    private static readonly ConditionalWeakTable<RenderScene, Groups> groups = new();
    public static ObjectId Root(RenderItem item) => item.Projection?.ViewportEntity ?? (item.InsertPath.IsEmpty ? item.Entity : item.InsertPath[0]);

    public static ImmutableArray<string> Expand(RenderScene scene, IEnumerable<ObjectId> entities)
    {
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(entities);
        var map = groups.GetValue(scene, static s => new(s)); var indices = new SortedSet<int>();
        foreach (var id in entities.Distinct()) if (map.Items.TryGetValue(id, out var items)) indices.UnionWith(items);
        return indices.Select(i => scene.Items[i].Key).ToImmutableArray();
    }

    public static ImmutableArray<string> Pick(RenderScene scene, Vec3 point, double tolerance)
    {
        Validate(point, tolerance);
        var box = new Bounds2(point.X, point.Y, point.X, point.Y).Inflate(tolerance);
        foreach (var i in scene.Index.Query(box).OrderDescending())
            if (scene.Items[i].Style.Visible && RenderHitTesting.Contains(scene.Items[i], point, tolerance))
                return Expand(scene, [Root(scene.Items[i])]);
        return [];
    }

    public static ImmutableArray<string> Select(RenderScene scene, Bounds2 box, DraftingSelectionMode mode,
        int maximumEntities = 10_000, CancellationToken token = default)
    {
        if (box.IsEmpty || !double.IsFinite(box.MinX + box.MinY + box.MaxX + box.MaxY) || !Enum.IsDefined(mode) || maximumEntities < 1)
            throw new ArgumentException("Invalid selection region, mode or budget.");
        var map = groups.GetValue(scene, static s => new(s)); var examined = new HashSet<ObjectId>(); var selected = new List<ObjectId>();
        var work = 0;
        foreach (var i in scene.Index.Query(box))
        {
            token.ThrowIfCancellationRequested(); var root = Root(scene.Items[i]); if (!examined.Add(root)) continue;
            if (examined.Count > maximumEntities * 16L) throw new InvalidOperationException("Selection candidate budget exceeded.");
            var indices = map.Items[root]; var accepted = mode == DraftingSelectionMode.Window;
            foreach (var index in indices)
            {
                var item = scene.Items[index]; if (!item.Style.Visible) continue;
                if (mode == DraftingSelectionMode.Window)
                {
                    if (!box.Contains(new(item.Bounds.MinX, item.Bounds.MinY)) || !box.Contains(new(item.Bounds.MaxX, item.Bounds.MaxY))) { accepted = false; break; }
                }
                else if (Crossing(item)) { accepted = true; break; }
            }
            if (accepted) { selected.Add(root); if (selected.Count > maximumEntities) throw new InvalidOperationException("Selection entity budget exceeded."); }
        }
        return Expand(scene, selected);

        bool Crossing(RenderItem item)
        {
            if (!item.Bounds.Intersects(box) || item.Clips.Any(c => !c.Bounds.Intersects(box))) return false;
            foreach (var point in new[] { box.Center, new Vec3(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY) })
                if (RenderHitTesting.Contains(item, point, 1e-9)) return true;
            var contours = item.Contours.Concat(item.Strokes.SelectMany(v => v.Contours)).Concat(item.Regions.SelectMany(v => v.Contours));
            foreach (var contour in contours)
            {
                var count = contour.Closed ? contour.Points.Length : contour.Points.Length - 1;
                if (contour.Points.Length == 1 && Visible(contour.Points[0])) return true;
                for (var j = 0; j < count; j++)
                {
                    token.ThrowIfCancellationRequested(); if (++work > 1_000_000) throw new InvalidOperationException("Selection segment budget exceeded.");
                    var a = contour.Points[j]; var b = contour.Points[(j + 1) % contour.Points.Length];
                    if (!PlanarGeometry.SegmentIntersectsBox(a, b, box)) continue;
                    var parameters = new List<double> { 0, 1 };
                    Edges([new(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY)]);
                    foreach (var clip in item.Clips) foreach (var boundary in clip.Contours) Edges(boundary.Points);
                    parameters.Sort();
                    foreach (var t in parameters) if (Visible(a + (b - a) * t)) return true;
                    for (var k = 1; k < parameters.Count; k++) if (Visible(a + (b - a) * ((parameters[k - 1] + parameters[k]) / 2))) return true;
                    void Edges(ImmutableArray<Vec3> vertices)
                    {
                        for (var k = 0; k < vertices.Length; k++)
                        {
                            if (++work > 1_000_000) throw new InvalidOperationException("Selection clipping budget exceeded.");
                            if (PlanarGeometry.LineIntersection(a, b, vertices[k], vertices[(k + 1) % vertices.Length], out var t, out var u) && t >= 0 && t <= 1 && u >= 0 && u <= 1) parameters.Add(t);
                        }
                    }
                }
            }
            // Prepared text/image geometry has no ordinary contour. Bounds are conservative;
            // probing the center catches objects wholly inside a window without inventing paths.
            return (item.Text is not null || item.Image is not null) && box.Contains(item.Bounds.Center) &&
                RenderHitTesting.Contains(item, item.Bounds.Center, 1e-9);
            bool Visible(Vec3 p) => box.Contains(p) && item.Clips.All(c => RenderHitTesting.ContainsClip(c, p));
        }
    }

    public static ObjectSnap? Snap(ShadowSnapshot snapshot, RenderScene scene, Vec3 point, double tolerance,
        ObjectSnapMode modes = ObjectSnapMode.Default, Vec3? anchor = null, IReadOnlySet<ObjectId>? excluded = null,
        CancellationToken token = default)
    {
        DraftingGuard.Current(snapshot, scene); Validate(point, tolerance);
        if (anchor is { IsFinite: false }) throw new ArgumentException("Invalid snap anchor.");
        var box = new Bounds2(point.X, point.Y, point.X, point.Y).Inflate(tolerance);
        var projector = new StandardEntityProjector(); ObjectSnap? best = null;
        var curves = new List<(Curve3 Curve, RenderItem Item)>(); var seen = new HashSet<(ObjectId, Affine3)>();
        var candidates = 0;
        foreach (var i in scene.Index.Query(box).Order())
        {
            token.ThrowIfCancellationRequested(); var item = scene.Items[i];
            if (!item.Style.Visible || item.IsPresentationOnly || excluded?.Contains(Root(item)) == true || !seen.Add((item.Entity, item.SourceToWorld))) continue;
            if (++candidates > 2048) throw new InvalidOperationException("Object snap candidate budget exceeded.");
            var reader = snapshot.Read(item.Entity);
            if (modes.HasFlag(ObjectSnapMode.Insertion))
                foreach (var name in new[] { "Position", "Center" }) if (reader[name].Exists) Candidate(item.SourceToWorld.Point(reader[name].Vector()), "Insertion", item);
            if (!projector.TryProject(reader, out var geometry, out _)) continue;
            foreach (var contour in geometry.Contours)
                foreach (var source in contour.Curves)
                {
                    if (curves.Count >= 4096) throw new InvalidOperationException("Object snap curve budget exceeded.");
                    var curve = source.Transform(item.SourceToWorld); curves.Add((curve, item));
                    if (modes.HasFlag(ObjectSnapMode.Endpoint) && !curve.Closed) { Candidate(curve.Start, "Endpoint", item); Candidate(curve.End, "Endpoint", item); }
                    if (modes.HasFlag(ObjectSnapMode.Midpoint) && curve is LineCurve or EllipseCurve && !curve.Closed) Candidate(curve.At(.5), "Midpoint", item);
                    if (curve is EllipseCurve ellipse)
                    {
                        if (modes.HasFlag(ObjectSnapMode.Center)) Candidate(ellipse.Center, "Center", item);
                        if (modes.HasFlag(ObjectSnapMode.Quadrant))
                            foreach (var angle in new[] { 0.0, Math.PI / 2, Math.PI, Math.PI * 1.5 })
                                if (OnArc(ellipse, angle)) Candidate(ellipse.Center + ellipse.U * Math.Cos(angle) + ellipse.V * Math.Sin(angle), "Quadrant", item);
                        if (modes.HasFlag(ObjectSnapMode.Tangent) && anchor is { } origin && Circular(ellipse))
                        {
                            var d = origin - ellipse.Center; var radius = ellipse.U.Length; var length = d.Length;
                            if (length > radius)
                            {
                                var baseAngle = Math.Atan2(Vec3.Dot(d, ellipse.V.Normalized), Vec3.Dot(d, ellipse.U.Normalized));
                                var offset = Math.Acos(radius / length);
                                foreach (var angle in new[] { baseAngle + offset, baseAngle - offset })
                                    if (OnArc(ellipse, angle)) Candidate(ellipse.Center + ellipse.U * Math.Cos(angle) + ellipse.V * Math.Sin(angle), "Tangent", item);
                            }
                        }
                    }
                    if (curve is LineCurve line && modes.HasFlag(ObjectSnapMode.Perpendicular) && anchor is { } from)
                    {
                        var direction = line.B - line.A; var length = direction.Length;
                        if (length > 0)
                        {
                            var t = Vec3.Dot(from - line.A, direction / length) / length;
                            if (t >= 0 && t <= 1) Candidate(line.At(t), "Perpendicular", item);
                        }
                    }
                    if (modes.HasFlag(ObjectSnapMode.Nearest))
                    {
                        if (curve is LineCurve edge) Candidate(PlanarGeometry.NearestOnSegment(point, edge.A, edge.B), "Nearest", item);
                        else if (curve is EllipseCurve circle && Circular(circle))
                        {
                            var d = point - circle.Center; var angle = Math.Atan2(Vec3.Dot(d, circle.V.Normalized), Vec3.Dot(d, circle.U.Normalized));
                            if (OnArc(circle, angle)) Candidate(circle.Center + circle.U * Math.Cos(angle) + circle.V * Math.Sin(angle), "Nearest", item);
                        }
                        else
                        {
                            // Bounded numerical minimization: approximation is explicitly exposed to callers.
                            var t = 0.0; var distance = double.PositiveInfinity;
                            for (var k = 0; k <= 64; k++) { var d = Vec3.Distance(point, curve.At(k / 64.0)); if (d < distance) { t = k / 64.0; distance = d; } }
                            var low = Math.Max(0, t - 1.0 / 64); var high = Math.Min(1, t + 1.0 / 64);
                            for (var k = 0; k < 24; k++)
                            { var l = low + (high - low) / 3; var r = high - (high - low) / 3; if (Vec3.Distance(point, curve.At(l)) < Vec3.Distance(point, curve.At(r))) high = r; else low = l; }
                            Candidate(curve.At((low + high) / 2), "Nearest", item, true);
                        }
                    }
                }
        }
        if (modes.HasFlag(ObjectSnapMode.Intersection))
        {
            if (curves.Count > 256) throw new InvalidOperationException("Intersection snap is limited to 256 nearby curves.");
            for (var a = 0; a < curves.Count; a++) for (var b = a + 1; b < curves.Count; b++)
            {
                token.ThrowIfCancellationRequested(); var first = curves[a]; var second = curves[b];
                if (first.Curve is LineCurve line && second.Curve is LineCurve other)
                {
                    if (PlanarGeometry.LineIntersection(line.A, line.B, other.A, other.B, out var t, out var u) && t >= 0 && t <= 1 && u >= 0 && u <= 1)
                    { var p = line.At(t); if (Vec3.Distance(p, other.At(u)) <= tolerance * 1e-6) Intersection(p); }
                }
                else if (first.Curve is LineCurve l && second.Curve is EllipseCurve e) LineEllipse(l, e);
                else if (first.Curve is EllipseCurve e2 && second.Curve is LineCurve l2) LineEllipse(l2, e2);
                void Intersection(Vec3 p)
                { if (second.Item.Clips.All(c => RenderHitTesting.ContainsClip(c, p))) Candidate(p, "Intersection", first.Item); }
                void LineEllipse(LineCurve line, EllipseCurve ellipse)
                {
                    var frame = new Affine3(ellipse.U, ellipse.V, Vec3.Cross(ellipse.U, ellipse.V).Normalized, ellipse.Center);
                    if (!frame.TryInverse(out var inverse)) return;
                    var start = inverse.Point(line.A); var end = inverse.Point(line.B);
                    if (Math.Abs(start.Z) + Math.Abs(end.Z) > 1e-7) return;
                    foreach (var t in PlanarGeometry.LineCircle(start, end, Vec3.Zero, 1))
                        if (t >= 0 && t <= 1)
                        { var local = start + (end - start) * t; if (OnArc(ellipse, Math.Atan2(local.Y, local.X))) Intersection(line.At(t)); }
                }
            }
        }
        return best;

        void Candidate(Vec3 position, string kind, RenderItem item, bool approximate = false)
        {
            if (!position.IsFinite || item.Clips.Any(c => !RenderHitTesting.ContainsClip(c, position))) return;
            var distance = Vec3.Distance(point with { Z = 0 }, position with { Z = 0 });
            if (distance <= tolerance && (best is null || distance < best.Distance - 1e-9 || Math.Abs(distance - best.Distance) <= 1e-9 && string.CompareOrdinal(kind, best.Kind) < 0))
                best = new(position, kind, item.Key, distance, approximate);
        }
    }
    private static bool Circular(EllipseCurve curve) => Math.Abs(curve.U.Length - curve.V.Length) <= 1e-9 * curve.U.Length && Math.Abs(Vec3.Dot(curve.U, curve.V)) <= 1e-9 * curve.U.Length * curve.V.Length;
    private static bool OnArc(EllipseCurve curve, double angle)
    {
        if (curve.Closed) return true;
        var delta = curve.Sweep >= 0 ? angle - curve.StartAngle : curve.StartAngle - angle;
        delta = (delta % Math.Tau + Math.Tau) % Math.Tau;
        return delta <= Math.Abs(curve.Sweep) + 1e-10;
    }
    private static void Validate(Vec3 point, double tolerance)
    { if (!point.IsFinite || !double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 1e9) throw new ArgumentOutOfRangeException(nameof(tolerance)); }
}
