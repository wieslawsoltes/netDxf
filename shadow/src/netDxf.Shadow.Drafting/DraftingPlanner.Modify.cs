using System.Collections.Immutable;

namespace netDxf.Shadow.Drafting;

public sealed partial class DraftingPlanner
{
    private DraftingPlan Offset(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner, CancellationToken token)
    {
        if (command.Entities.Length != 1) throw new ArgumentException("OFFSET requires one entity.");
        Exact(command.Points, 1); var distance = Positive(command.Value);
        var entity = DraftingGuard.Entity(snapshot, command.Entities[0]); DraftingGuard.Planar(entity);
        if (entity.Type is not ("Line" or "Circle" or "Arc" or "Polyline2D"))
            throw new NotSupportedException("OFFSET currently supports planar lines, circles, arcs and straight polylines.");
        var edit = new ShadowGraphEdit(snapshot); var budget = 0;
        var clone = Clone(snapshot, edit, command.Entities, owner, token, ref budget).Single();
        var staged = edit.Build().Apply(snapshot); edit = new(staged); var copy = staged.Read(clone);
        var pick = command.Points[0];
        switch (entity.Type)
        {
            case "Line":
                var a = entity["StartPoint"].Vector(); var b = entity["EndPoint"].Vector();
                var normal = PlanarGeometry.Left((b - a) / Length(b - a));
                var shift = normal * (Vec3.Dot(pick - a, normal) < 0 ? -distance : distance);
                edit.Set(clone, "StartPoint", V(a + shift)); edit.Set(clone, "EndPoint", V(b + shift)); break;
            case "Circle": case "Arc":
                var radius = entity["Radius"].Number(); var center = entity["Center"].Vector();
                var next = radius + (Vec3.Distance(pick, center) < radius ? -distance : distance);
                edit.Set(clone, "Radius", N(Positive(next))); break;
            default:
                var vertices = entity["Vertexes"].Items.ToArray(); var copies = copy["Vertexes"].Items.ToArray();
                if (vertices.Length > limits.MaximumPoints || vertices.Length < 2 || vertices.Any(v => Math.Abs(v["Bulge"].Number()) > 1e-12))
                    throw new NotSupportedException("Polyline OFFSET requires a bounded, straight-segment polyline.");
                var closed = entity["IsClosed"].Boolean(); var count = closed ? vertices.Length : vertices.Length - 1;
                var points = vertices.Select(v => v["Position"].Vector()).ToArray();
                var nearest = Enumerable.Range(0, count).OrderBy(i => Vec3.Distance(pick, PlanarGeometry.NearestOnSegment(pick, points[i], points[(i + 1) % points.Length]))).First();
                var sign = PlanarGeometry.Cross(points[(nearest + 1) % points.Length] - points[nearest], pick - points[nearest]) < 0 ? -1 : 1;
                var starts = new Vec3[count]; var ends = new Vec3[count];
                for (var i = 0; i < count; i++)
                {
                    var edge = points[(i + 1) % points.Length] - points[i];
                    var offset = PlanarGeometry.Left(edge / Length(edge)) * (distance * sign);
                    starts[i] = points[i] + offset; ends[i] = points[(i + 1) % points.Length] + offset;
                }
                var result = new Vec3[points.Length];
                for (var i = 0; i < points.Length; i++)
                {
                    if (!closed && i == 0) result[i] = starts[0];
                    else if (!closed && i == points.Length - 1) result[i] = ends[^1];
                    else
                    {
                        var previous = (i + count - 1) % count; var current = i % count;
                        if (PlanarGeometry.LineIntersection(starts[previous], ends[previous], starts[current], ends[current], out var t, out _))
                            result[i] = starts[previous] + (ends[previous] - starts[previous]) * t;
                        else if (Vec3.Distance(ends[previous], starts[current]) <= limits.MinimumLength) result[i] = starts[current];
                        else throw new NotSupportedException("Reversing/degenerate polyline offset requires a different join policy.");
                        if (Vec3.Distance(result[i], points[i]) > distance * 1000)
                            throw new InvalidOperationException("Offset miter exceeds the drafting join budget.");
                    }
                    CheckPoint(result[i]);
                    edit.Set(copies[i].Id!.Value, "Position", V2(result[i]));
                }
                // Avoid publishing invalid loops. This explicit quadratic check is bounded separately.
                if (count > 1024) throw new NotSupportedException("Polyline OFFSET is limited to 1024 edges while validating self-intersections.");
                for (var i = 0; i < count; i++)
                    for (var j = i + 2; j < count; j++)
                    {
                        if (closed && i == 0 && j == count - 1) continue;
                        if (PlanarGeometry.LineIntersection(result[i], result[(i + 1) % result.Length], result[j], result[(j + 1) % result.Length], out var t, out var u)
                            && t > 1e-10 && t < 1 - 1e-10 && u > 1e-10 && u < 1 - 1e-10)
                            throw new NotSupportedException("Offset self-intersects; no source geometry was changed.");
                    }
                break;
        }
        var after = edit.Build().Apply(staged);
        return new("OFFSET", ShadowGraphPatch.Between(snapshot, after), [clone]);
    }

    private DraftingPlan TrimExtend(ShadowSnapshot snapshot, DraftingCommand command)
    {
        if (command.Entities.Length != 2) throw new ArgumentException("Choose one target line followed by one cutting/boundary edge.");
        Exact(command.Points, 1);
        var target = DraftingGuard.Entity(snapshot, command.Entities[0]);
        var boundary = DraftingGuard.Entity(snapshot, command.Entities[1], false);
        DraftingGuard.Planar(target); DraftingGuard.Planar(boundary);
        if (target.Type != "Line") throw new NotSupportedException("TRIM/EXTEND currently edits line endpoints; select a LINE target.");
        var a = target["StartPoint"].Vector(); var b = target["EndPoint"].Vector(); var delta = b - a; var length = Length(delta);
        ImmutableArray<double> intersections;
        if (boundary.Type == "Line")
        {
            intersections = PlanarGeometry.LineIntersection(a, b, boundary["StartPoint"].Vector(), boundary["EndPoint"].Vector(), out var t, out var u)
                && u >= -1e-10 && u <= 1 + 1e-10 ? [t] : [];
        }
        else if (boundary.Type == "Circle") intersections = PlanarGeometry.LineCircle(a, b, boundary["Center"].Vector(), boundary["Radius"].Number());
        else throw new NotSupportedException("Use a line or circle boundary for this planar endpoint command.");
        var pick = Vec3.Dot(command.Points[0] - a, delta / length) / length;
        var edit = new ShadowGraphEdit(snapshot);
        if (command.Name == "TRIM")
        {
            var cuts = intersections.Where(v => v > 1e-10 && v < 1 - 1e-10).Order().ToArray();
            if (cuts.Length == 0) throw new InvalidOperationException("The boundary does not cross the interior of the target line.");
            if (cuts.Length > 1 && pick > cuts[0] && pick < cuts[^1])
                throw new NotSupportedException("Trimming a middle interval into two entities is not an endpoint trim; choose an end segment.");
            var cut = pick <= cuts[0] ? cuts[0] : cuts[^1];
            edit.Set(target.Id!.Value, pick <= cuts[0] ? "StartPoint" : "EndPoint", V(a + delta * cut));
        }
        else
        {
            var start = pick < 0.5;
            var candidates = intersections.Where(v => start ? v < -1e-10 : v > 1 + 1e-10).OrderBy(v => start ? -v : v).ToArray();
            if (candidates.Length == 0) throw new InvalidOperationException("No boundary intersection beyond the chosen endpoint.");
            var point = a + delta * candidates[0]; CheckPoint(point);
            edit.Set(target.Id!.Value, start ? "StartPoint" : "EndPoint", V(point));
        }
        return new(command.Name, edit.Build(), []);
    }

    private DraftingPlan Corner(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner)
    {
        if (command.Entities.Length != 2) throw new ArgumentException("FILLET/CHAMFER requires two lines.");
        var first = DraftingGuard.Entity(snapshot, command.Entities[0]); var second = DraftingGuard.Entity(snapshot, command.Entities[1]);
        DraftingGuard.Planar(first); DraftingGuard.Planar(second);
        if (first.Type != "Line" || second.Type != "Line") throw new NotSupportedException("This corner planner requires two planar LINE entities.");
        var a = first["StartPoint"].Vector(); var b = first["EndPoint"].Vector();
        var c = second["StartPoint"].Vector(); var d = second["EndPoint"].Vector();
        if (!PlanarGeometry.LineIntersection(a, b, c, d, out var t, out _)) throw new ArgumentException("Parallel lines do not define a finite corner.");
        var intersection = a + (b - a) * t; CheckPoint(intersection);
        var firstFar = Vec3.Distance(a, intersection) > Vec3.Distance(b, intersection) ? a : b;
        var secondFar = Vec3.Distance(c, intersection) > Vec3.Distance(d, intersection) ? c : d;
        var firstLength = Length(firstFar - intersection); var secondLength = Length(secondFar - intersection);
        var u = (firstFar - intersection) / firstLength; var v = (secondFar - intersection) / secondLength;
        var theta = Math.Acos(Math.Clamp(Vec3.Dot(u, v), -1, 1));
        if (theta < 1e-10 || Math.PI - theta < 1e-10) throw new ArgumentException("Degenerate corner angle.");
        var amount = command.Value == 0 ? 0 : Positive(command.Value);
        var along = command.Name == "FILLET" ? amount / Math.Tan(theta / 2) : amount;
        if (along >= firstLength || along >= secondLength) throw new ArgumentException("The corner distance exceeds a line segment.");
        var p = intersection + u * along; var q = intersection + v * along;
        var edit = new ShadowGraphEdit(snapshot);
        edit.Set(first.Id!.Value, firstFar == a ? "EndPoint" : "StartPoint", V(p));
        edit.Set(second.Id!.Value, secondFar == c ? "EndPoint" : "StartPoint", V(q));
        var created = ImmutableArray<ObjectId>.Empty;
        if (amount > 0)
        {
            var values = Appearance(first);
            if (command.Name == "CHAMFER")
            {
                values["StartPoint"] = V(p); values["EndPoint"] = V(q);
                created = [edit.AddEntity(owner, "netDxf.Entities.Line", values)];
            }
            else
            {
                var center = intersection + (u + v).Normalized * (amount / Math.Sin(theta / 2));
                var start = PlanarGeometry.Degrees(p - center); var end = PlanarGeometry.Degrees(q - center);
                if (PlanarGeometry.Cross(p - center, q - center) < 0) (start, end) = (end, start);
                values["Center"] = V(center); values["Radius"] = N(amount); values["StartAngle"] = N(start); values["EndAngle"] = N(end);
                created = [edit.AddEntity(owner, "netDxf.Entities.Arc", values)];
            }
        }
        return new(command.Name, edit.Build(), created);
    }

    private static Dictionary<string, ShadowValue> Appearance(ShadowReader entity)
        => new[] { "Layer", "Color", "Linetype", "LinetypeScale", "Lineweight", "Transparency", "Normal", "IsVisible", "Thickness" }
            .Where(name => entity[name].Exists).ToDictionary(name => name, name => entity[name].Value, StringComparer.Ordinal);
}
