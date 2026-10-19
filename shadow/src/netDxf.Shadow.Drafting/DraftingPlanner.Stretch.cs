using System.Collections.Immutable;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Drafting;

public sealed partial class DraftingPlanner
{
    // Points are crossing corners, base and target. Explicit entity IDs retain the
    // host's crossing selection/removals; the rectangle selects source vertices.
    private DraftingPlan Stretch(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner, CancellationToken token)
    {
        Selected(command, 1); Exact(command.Points, 4); PlanarPoints(command.Points);
        var a = command.Points[0]; var b = command.Points[1];
        Positive(Math.Abs(a.X - b.X)); Positive(Math.Abs(a.Y - b.Y));
        var delta = command.Points[3] - command.Points[2]; Length(delta);
        var box = Bounds2.Empty.Include(a).Include(b);
        var edit = new ShadowGraphEdit(snapshot); var moving = new List<ObjectId>(); var fullyEnclosed = new List<ObjectId>(); var affected = 0; var coordinates = 0;
        bool Inside(Vec3 point) => point.X >= box.MinX && point.X <= box.MaxX && point.Y >= box.MinY && point.Y <= box.MaxY;
        Vec3 CheckCoordinate(Vec3 point)
        {
            if (++coordinates > limits.MaximumClonedObjects) throw new InvalidOperationException("STRETCH coordinate budget exceeded.");
            CheckPoint(point); return point;
        }
        void Vertex(ShadowReader value, string property)
        {
            var before = CheckCoordinate(value[property].Vector());
            if (!Inside(before)) return;
            var after = CheckCoordinate(before + delta);
            if (value.Id is not { } id) throw new NotSupportedException("STRETCH requires source coordinate identities.");
            edit.Set(id, property, property == "Position" && value.Type == "Polyline2DVertex" ? V2(after) : V(after)); affected++;
        }
        foreach (var id in command.Entities)
        {
            token.ThrowIfCancellationRequested(); var entity = DraftingGuard.Entity(snapshot, id);
            if (entity["Owner"].Id != owner) throw new ArgumentException("STRETCH selection must belong to the active editing space.");
            if (entity["Reactors"].Items.Any() || entity["PersistentReactors"].Items.Any())
                throw new NotSupportedException("STRETCH of associative or constrained geometry requires its owning constraint planner.");
            var previousAffected = affected;
            switch (entity.Type)
            {
                case "Line":
                {
                    var start = entity["StartPoint"].Vector(); var end = entity["EndPoint"].Vector();
                    var nextStart = Inside(start) ? start + delta : start; var nextEnd = Inside(end) ? end + delta : end;
                    if (Inside(start) || Inside(end)) Length(nextEnd - nextStart);
                    Vertex(entity, "StartPoint"); Vertex(entity, "EndPoint"); break;
                }
                case "Polyline2D":
                {
                    DraftingGuard.Planar(entity);
                    var vertices = entity["Vertexes"].Items.Take(limits.MaximumPoints + 1).ToArray();
                    if (vertices.Length < 2 || vertices.Length > limits.MaximumPoints) throw new ArgumentException("STRETCH polyline vertex budget exceeded or empty geometry.");
                    var next = vertices.Select(v => v["Position"].Vector()).Select(p => Inside(p) ? p + delta : p).ToArray();
                    var count = entity["IsClosed"].Boolean() ? next.Length : next.Length - 1;
                    for (var i = 0; i < count; i++) Length(next[(i + 1) % next.Length] - next[i]);
                    foreach (var vertex in vertices) Vertex(vertex, "Position");
                    break;
                }
                case "Circle": case "Ellipse":
                    MoveFrom(entity, "Center"); break;
                case "Point": case "Text": case "MText": case "Insert": case "Image":
                    MoveFrom(entity, "Position"); break;
                default:
                    if (!FullyEnclosed(entity))
                        throw new NotSupportedException($"Partially enclosed {entity.Type} geometry requires an exact STRETCH planner. No selected object was changed.");
                    fullyEnclosed.Add(id); affected++; break;
            }
            if (affected != previousAffected && entity["ProxyGraphics"].Exists) edit.Set(id, "ProxyGraphics", ShadowValue.Null);
        }
        var working = edit.Build().Apply(snapshot);
        if (moving.Count != 0) working = working.Apply(EditOperations.Translate(working, moving, delta));
        if (fullyEnclosed.Count != 0) working = AffineEditOperations.Plan(working, fullyEnclosed, Affine3.Move(delta),
            new(MaximumEntities: limits.MaximumEntities, MaximumCoordinates: limits.MaximumClonedObjects), token).Apply(working);
        if (affected == 0) throw new ArgumentException("The crossing window contains no supported endpoints, vertices, centers or insertion points.");
        token.ThrowIfCancellationRequested();
        return new("STRETCH", ShadowGraphPatch.Between(snapshot, working), []);

        void MoveFrom(ShadowReader entity, string property)
        {
            var position = CheckCoordinate(entity[property].Vector());
            if (!Inside(position)) return;
            CheckCoordinate(position + delta);
            // A block keeps its shared definition; attached attributes move together.
            if (entity.Type == "Insert")
                foreach (var attribute in entity["Attributes"].Items) CheckCoordinate(attribute["Position"].Vector() + delta);
            moving.Add(entity.Id!.Value); affected++;
        }

        bool FullyEnclosed(ShadowReader entity)
        {
            // Bound source-owned data before the projector allocates any curve arrays.
            // Shared block/table definitions are not followed by this primitive projector.
            var pending = new Stack<ShadowValue>(entity.Object!.Properties.Where(p => p.Key != "Owner").Select(p => p.Value));
            var visited = new HashSet<ObjectId>(); var count = 0;
            while (pending.TryPop(out var value))
            {
                token.ThrowIfCancellationRequested();
                if (++count > limits.MaximumClonedObjects) throw new InvalidOperationException("STRETCH containment budget exceeded.");
                IEnumerable<ShadowValue> children = value switch
                {
                    ShadowValue.Sequence sequence => sequence.Items,
                    ShadowValue.Structure structure => structure.Fields.Values,
                    ShadowValue.Reference reference when visited.Add(reference.Id) && snapshot.Objects[reference.Id].Kind == ShadowObjectKind.Object
                        => snapshot.Objects[reference.Id].Properties.Where(p => p.Key != "Owner").Select(p => p.Value),
                    _ => []
                };
                foreach (var child in children)
                {
                    if (pending.Count + count >= limits.MaximumClonedObjects) throw new InvalidOperationException("STRETCH containment budget exceeded.");
                    pending.Push(child);
                }
            }
            if (!new StandardEntityProjector().TryProject(entity, out var geometry, out var diagnostic) || diagnostic is not null ||
                geometry.Text is not null || geometry.Image is not null || !geometry.Components.IsEmpty) return false;
            var any = false;
            foreach (var curve in geometry.Contours.SelectMany(c => c.Curves))
            {
                token.ThrowIfCancellationRequested(); any = true;
                IEnumerable<Vec3> envelope = curve switch
                {
                    LineCurve line => [line.A, line.B],
                    EllipseCurve ellipse => ConicEnvelope(ellipse),
                    // Positive-weight NURBS remain inside the convex hull. Do not
                    // infer containment from a finite sampled path that can miss extrema.
                    NurbsCurve nurbs => nurbs.Controls,
                    _ => throw new NotSupportedException("Unknown curve containment representation.")
                };
                foreach (var point in envelope)
                { CheckCoordinate(point); CheckCoordinate(point + delta); if (!Inside(point)) return false; }
            }
            return any;
        }
        static IEnumerable<Vec3> ConicEnvelope(EllipseCurve curve)
        {
            yield return curve.Start; yield return curve.End;
            foreach (var angle in new[] { Math.Atan2(curve.V.X, curve.U.X), Math.Atan2(curve.V.Y, curve.U.Y) })
                foreach (var extremum in new[] { angle, angle + Math.PI })
                    if (PositiveAngle(curve.Sweep >= 0 ? extremum - curve.StartAngle : curve.StartAngle - extremum) <= Math.Abs(curve.Sweep) + 1e-12)
                        yield return curve.Center + curve.U * Math.Cos(extremum) + curve.V * Math.Sin(extremum);
        }
    }
}
