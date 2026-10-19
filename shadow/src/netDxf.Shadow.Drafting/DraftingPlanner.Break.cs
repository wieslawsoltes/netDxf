using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Drafting;

public sealed partial class DraftingPlanner
{
    private sealed record BreakSegment(Curve3 Curve, double Length, double Bulge = 0, double StartWidth = 0, double EndWidth = 0);
    private sealed record BreakPart(BreakSegment Segment, double Start, double End);

    private DraftingPlan Break(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner, CancellationToken token)
    {
        if (command.Entities.Length != 1) throw new ArgumentException("BREAK requires exactly one source entity.");
        var atPoint = command.Name == "BREAKATPOINT"; Exact(command.Points, atPoint ? 1 : 2); PlanarPoints(command.Points);
        var entity = DraftingGuard.Entity(snapshot, command.Entities[0]); DraftingGuard.Planar(entity);
        if (entity["Owner"].Id != owner) throw new ArgumentException("BREAK selection must belong to the active editing space.");
        if (entity["XData"].Items.Any() || entity["XData"]["Values"].Items.Any() || entity["Reactors"].Items.Any() ||
            entity["PersistentReactors"].Items.Any() || entity["ExtensionDictionary"].Exists || entity["ProxyGraphics"].Items.Any())
            throw new NotSupportedException("BREAK cannot redistribute XDATA, extension dictionaries, reactors or stored proxy graphics. No source data was changed.");
        var segments = new List<BreakSegment>(); var closed = entity.Type == "Circle" || entity["IsClosed"].Boolean();
        if (atPoint && closed) throw new ArgumentException("BREAKATPOINT requires an open line, arc or polyline.");
        switch (entity.Type)
        {
            case "Line":
                AddLine(entity["StartPoint"].Vector(), entity["EndPoint"].Vector()); break;
            case "Arc": case "Circle":
            {
                var center = entity["Center"].Vector(); PlanarPoints([center]); CheckPoint(center);
                var radius = Positive(entity["Radius"].Number());
                var start = entity.Type == "Circle" ? 0 : entity["StartAngle"].Number() * Math.PI / 180;
                var sweep = entity.Type == "Circle" ? Math.Tau : PositiveAngle((entity["EndAngle"].Number() - entity["StartAngle"].Number()) * Math.PI / 180);
                if (sweep == 0) throw new ArgumentException("A zero-sweep arc cannot be broken.");
                segments.Add(new(new EllipseCurve(center, Vec3.UnitX * radius, Vec3.UnitY * radius, start, sweep), radius * sweep)); break;
            }
            case "Polyline2D":
            {
                var vertices = entity["Vertexes"].Items.Take(limits.MaximumPoints + 1).ToArray();
                if (vertices.Length is < 2 || vertices.Length > limits.MaximumPoints) throw new ArgumentException("BREAK polyline vertex budget exceeded or empty geometry.");
                var count = closed ? vertices.Length : vertices.Length - 1;
                for (var i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested(); var vertex = vertices[i];
                    var a = vertex["Position"].Vector(); var b = vertices[(i + 1) % vertices.Length]["Position"].Vector();
                    CheckPoint(a); CheckPoint(b); Length(b - a);
                    var bulge = vertex["Bulge"].Number(); var w0 = vertex["StartWidth"].Number(); var w1 = vertex["EndWidth"].Number();
                    if (!double.IsFinite(bulge) || !double.IsFinite(w0) || !double.IsFinite(w1) || w0 < 0 || w1 < 0) throw new ArgumentException("Invalid polyline bulge or widths.");
                    var curve = StandardEntityProjector.Bulge(a, b, bulge);
                    var length = curve is EllipseCurve arc ? arc.U.Length * Math.Abs(arc.Sweep) : (b - a).Length;
                    if (!double.IsFinite(length) || length < limits.MinimumLength) throw new ArgumentException("Polyline segment cannot be represented at this precision.");
                    segments.Add(new(curve, length, bulge, w0, w1));
                }
                break;
            }
            default: throw new NotSupportedException("BREAK supports analytic LINE, ARC, CIRCLE and Polyline2D geometry.");
        }
        var total = segments.Sum(s => s.Length);
        if (!double.IsFinite(total) || total < limits.MinimumLength) throw new ArgumentException("Empty or unrepresentable BREAK path.");
        var first = Project(command.Points[0]); var second = atPoint ? first : Project(command.Points[1]);
        var ranges = new List<List<(double Start, double End)>>();
        if (closed)
        {
            if (Math.Min(Math.Abs(first - second), total - Math.Abs(first - second)) < limits.MinimumLength)
                throw new ArgumentException("Closed curves require two distinct projected break points.");
            ranges.Add(second < first ? [(second, first)] : [(second, total), (0, first)]);
        }
        else
        {
            var low = Math.Min(first, second); var high = Math.Max(first, second);
            if (high - low < limits.MinimumLength && (low < limits.MinimumLength || total - high < limits.MinimumLength))
                throw new ArgumentException("A no-gap break requires an interior point.");
            if (low >= limits.MinimumLength) ranges.Add([(0, low)]);
            if (total - high >= limits.MinimumLength) ranges.Add([(high, total)]);
        }
        if (ranges.Count > limits.MaximumEntities) throw new ArgumentException("BREAK exceeds the configured result entity budget.");
        var edit = new ShadowGraphEdit(snapshot); var created = ImmutableArray.CreateBuilder<ObjectId>(); var cloneBudget = 0;
        for (var piece = 0; piece < ranges.Count; piece++)
        {
            token.ThrowIfCancellationRequested(); var parts = Slice(ranges[piece]);
            if (parts.Count == 0) throw new ArgumentException("Break would create empty geometry.");
            ObjectId id;
            if (entity.Type == "Circle")
            {
                CheckPoint(parts[0].Segment.Curve.At(parts[0].Start)); CheckPoint(parts[^1].Segment.Curve.At(parts[^1].End));
                var values = Appearance(entity); values["Center"] = entity["Center"].Value; values["Radius"] = entity["Radius"].Value;
                foreach (var property in new[] { "ColorName", "ShadowMode" })
                    if (entity[property].Exists) values[property] = entity[property].Value;
                values["StartAngle"] = N(PlanarGeometry.Degrees(parts[0].Segment.Curve.At(parts[0].Start) - entity["Center"].Vector()));
                values["EndAngle"] = N(PlanarGeometry.Degrees(parts[^1].Segment.Curve.At(parts[^1].End) - entity["Center"].Vector()));
                id = edit.AddEntity(owner, "netDxf.Entities.Arc", values); created.Add(id);
            }
            else
            {
                id = piece == 0 ? entity.Id!.Value : Clone(snapshot, edit, [entity.Id!.Value], owner, token, ref cloneBudget).Single();
                if (piece > 0) created.Add(id);
                SetGeometry(id, parts);
            }
        }
        if (entity.Type == "Circle" || ranges.Count == 0) edit.RemoveEntities([entity.Id!.Value]);
        edit.PruneUnreachable(); token.ThrowIfCancellationRequested();
        return new(command.Name, edit.Build(), created.ToImmutable());

        void AddLine(Vec3 a, Vec3 b)
        { CheckPoint(a); CheckPoint(b); PlanarPoints([a, b]); segments.Add(new(new LineCurve(a, b), Length(b - a))); }
        double Project(Vec3 point)
        {
            var nearest = double.PositiveInfinity; var distance = 0d; var offset = 0d;
            foreach (var segment in segments)
            {
                token.ThrowIfCancellationRequested(); var t = NearestParameter(segment.Curve, point);
                var candidate = Vec3.Distance(point, segment.Curve.At(t));
                if (candidate < nearest) { nearest = candidate; distance = offset + segment.Length * t; }
                offset += segment.Length;
            }
            return distance;
        }
        List<BreakPart> Slice(List<(double Start, double End)> sections)
        {
            var parts = new List<BreakPart>();
            foreach (var (start, end) in sections)
            {
                var offset = 0d;
                foreach (var segment in segments)
                {
                    token.ThrowIfCancellationRequested(); var a = Math.Max(start, offset); var b = Math.Min(end, offset + segment.Length);
                    if (b - a >= limits.MinimumLength) parts.Add(new(segment, (a - offset) / segment.Length, (b - offset) / segment.Length));
                    offset += segment.Length;
                }
            }
            return parts;
        }
        void SetGeometry(ObjectId id, List<BreakPart> parts)
        {
            var start = parts[0].Segment.Curve.At(parts[0].Start); var end = parts[^1].Segment.Curve.At(parts[^1].End);
            CheckPoint(start); CheckPoint(end);
            if (entity.Type == "Line") { edit.Set(id, "StartPoint", V(start)); edit.Set(id, "EndPoint", V(end)); }
            else if (entity.Type == "Arc")
            { edit.Set(id, "StartAngle", N(PlanarGeometry.Degrees(start - entity["Center"].Vector()))); edit.Set(id, "EndAngle", N(PlanarGeometry.Degrees(end - entity["Center"].Vector()))); }
            else
            {
                var vertices = new List<ShadowValue>();
                foreach (var part in parts)
                {
                    var segment = part.Segment; var point = segment.Curve.At(part.Start); CheckPoint(point);
                    Vertex(point, segment.Curve is EllipseCurve arc ? Math.Tan(arc.Sweep * (part.End - part.Start) / 4) : 0,
                        segment.StartWidth + (segment.EndWidth - segment.StartWidth) * part.Start,
                        segment.StartWidth + (segment.EndWidth - segment.StartWidth) * part.End);
                }
                Vertex(end, 0, 0, 0); edit.ReplaceSequence(id, "Vertexes", vertices); edit.Set(id, "IsClosed", ShadowValue.From(false));
                void Vertex(Vec3 point, double bulge, double w0, double w1)
                {
                    if (++cloneBudget > limits.MaximumClonedObjects) throw new InvalidOperationException("BREAK source-object budget exceeded.");
                    vertices.Add(new ShadowValue.Reference(edit.Create("netDxf.Entities.Polyline2DVertex", ShadowObjectKind.Object,
                        new Dictionary<string, ShadowValue> { ["Position"] = V2(point), ["Bulge"] = N(bulge), ["StartWidth"] = N(w0), ["EndWidth"] = N(w1) })));
                }
            }
        }
    }
    private static double PositiveAngle(double angle) => (angle % Math.Tau + Math.Tau) % Math.Tau;
    private static double NearestParameter(Curve3 curve, Vec3 point)
    {
        if (curve is LineCurve line)
        { var delta = line.B - line.A; return Math.Clamp(Vec3.Dot(point - line.A, delta / delta.Length) / delta.Length, 0, 1); }
        var arc = (EllipseCurve)curve; var direction = point - arc.Center;
        var angle = Math.Atan2(Vec3.Dot(direction, arc.V.Normalized), Vec3.Dot(direction, arc.U.Normalized));
        var distance = PositiveAngle(arc.Sweep > 0 ? angle - arc.StartAngle : arc.StartAngle - angle);
        if (distance <= Math.Abs(arc.Sweep)) return distance / Math.Abs(arc.Sweep);
        return Vec3.Distance(point, arc.Start) <= Vec3.Distance(point, arc.End) ? 0 : 1;
    }
}
