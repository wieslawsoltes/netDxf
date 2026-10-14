using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Drafting;

public sealed partial class DraftingPlanner
{
    private DraftingPlan JoinExplode(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner)
    {
        Selected(command, command.Name == "JOIN" ? 2 : 1);
        if (command.Name == "JOIN") return Join(snapshot, command, owner);
        var edit = new ShadowGraphEdit(snapshot); var created = ImmutableArray.CreateBuilder<ObjectId>();
        foreach (var id in command.Entities)
        {
            var entity = DraftingGuard.Entity(snapshot, id); DraftingGuard.Planar(entity);
            if (entity.Type != "Polyline2D") throw new NotSupportedException("EXPLODE currently decomposes 2D polylines into exact lines/arcs.");
            if (entity["XData"]["Values"].Items.Any()) throw new NotSupportedException("Polyline has XDATA; choose an application-specific metadata distribution policy before exploding it.");
            var vertices = entity["Vertexes"].Items.ToArray();
            if (vertices.Any(v => v["StartWidth"].Number() != 0 || v["EndWidth"].Number() != 0))
                throw new NotSupportedException("Wide polylines need a region conversion, not centerline EXPLODE.");
            var count = entity["IsClosed"].Boolean() ? vertices.Length : vertices.Length - 1;
            if (count < 1 || created.Count + count > limits.MaximumEntities) throw new InvalidOperationException("Explode entity budget exceeded.");
            for (var i = 0; i < count; i++)
            {
                var a = vertices[i]["Position"].Vector(); var b = vertices[(i + 1) % vertices.Length]["Position"].Vector();
                Length(b - a); var bulge = vertices[i]["Bulge"].Number(); var values = Appearance(entity); string type;
                if (Math.Abs(bulge) < 1e-12)
                { type = "Line"; values["StartPoint"] = V(a); values["EndPoint"] = V(b); }
                else
                {
                    var curve = (EllipseCurve)StandardEntityProjector.Bulge(a, b, bulge);
                    type = "Arc"; values["Center"] = V(curve.Center); values["Radius"] = N(curve.U.Length);
                    var start = PlanarGeometry.Degrees(curve.Start - curve.Center); var end = PlanarGeometry.Degrees(curve.End - curve.Center);
                    if (curve.Sweep < 0) (start, end) = (end, start);
                    values["StartAngle"] = N(start); values["EndAngle"] = N(end);
                }
                created.Add(edit.AddEntity(owner, "netDxf.Entities." + type, values));
            }
        }
        edit.RemoveEntities(command.Entities);
        return new("EXPLODE", edit.Build(), created.ToImmutable());
    }

    private DraftingPlan Join(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner)
    {
        // Build an endpoint adjacency graph rather than repeatedly scanning every remaining edge.
        var vertices = new List<Vec3>(); var adjacency = new List<List<int>>();
        var cells = new Dictionary<(double X, double Y), List<int>>(); var edges = new List<(int A, int B)>();
        var tolerance = Math.Max(limits.MinimumLength, command.Value);
        if (!(tolerance > 0) || !double.IsFinite(tolerance)) throw new ArgumentException("Invalid join tolerance.");
        var source = DraftingGuard.Entity(snapshot, command.Entities[0]);
        foreach (var id in command.Entities)
        {
            var entity = DraftingGuard.Entity(snapshot, id); DraftingGuard.Planar(entity);
            if (entity.Type != "Line") throw new NotSupportedException("JOIN currently assembles an unbranched chain of LINE entities.");
            if (entity["XData"]["Values"].Items.Any()) throw new NotSupportedException("JOIN cannot implicitly combine entity XDATA.");
            foreach (var name in Appearance(source).Keys)
                if (!ShadowValue.Equivalent(source[name].Value, entity[name].Value) && name is not ("Color" or "Transparency"))
                    throw new NotSupportedException("JOIN requires consistent appearance; assign one layer/linetype first.");
            var a = Vertex(entity["StartPoint"].Vector()); var b = Vertex(entity["EndPoint"].Vector());
            if (a == b) throw new ArgumentException("JOIN encountered a zero-length edge.");
            var index = edges.Count; edges.Add((a, b)); adjacency[a].Add(index); adjacency[b].Add(index);
            if (adjacency[a].Count > 2 || adjacency[b].Count > 2) throw new ArgumentException("JOIN selection branches at an endpoint.");
        }
        var ends = Enumerable.Range(0, vertices.Count).Where(i => adjacency[i].Count == 1).ToArray();
        if (ends.Length is not (0 or 2)) throw new ArgumentException("JOIN selection is disconnected.");
        var closed = ends.Length == 0; var current = closed ? 0 : ends[0];
        var visited = new HashSet<int>(); var points = ImmutableArray.CreateBuilder<Vec3>(); points.Add(vertices[current]);
        while (true)
        {
            var next = adjacency[current].Where(i => !visited.Contains(i)).DefaultIfEmpty(-1).First();
            if (next < 0) break;
            visited.Add(next); var edge = edges[next]; current = edge.A == current ? edge.B : edge.A;
            points.Add(vertices[current]);
        }
        if (visited.Count != edges.Count) throw new ArgumentException("JOIN selection contains disconnected components.");
        if (closed) points.RemoveAt(points.Count - 1);
        var create = Create(snapshot, new("PLINE", points.ToImmutable(), Layer: source["Layer"]["Name"].Text, Closed: closed), owner, default);
        var staged = create.Patch.Apply(snapshot); var edit = new ShadowGraphEdit(staged);
        foreach (var (name, value) in Appearance(source)) edit.Set(create.Created[0], name, value);
        edit.RemoveEntities(command.Entities);
        return new("JOIN", ShadowGraphPatch.Between(snapshot, edit.Build().Apply(staged)), create.Created);

        int Vertex(Vec3 point)
        {
            CheckPoint(point);
            if (Math.Abs(point.Z) > limits.MinimumLength) throw new NotSupportedException("JOIN requires XY line geometry.");
            var cell = (X: Math.Floor(point.X / tolerance), Y: Math.Floor(point.Y / tolerance));
            for (var x = -1; x <= 1; x++) for (var y = -1; y <= 1; y++)
                if (cells.TryGetValue((cell.X + x, cell.Y + y), out var candidates))
                    foreach (var index in candidates) if (Vec3.Distance(point, vertices[index]) <= tolerance) return index;
            var id = vertices.Count; vertices.Add(point); adjacency.Add(new());
            if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = new();
            bucket.Add(id); return id;
        }
    }
}
