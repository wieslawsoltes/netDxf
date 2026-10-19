using System.Collections.Immutable;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Drafting;

/// <summary>Editable source grips, not tessellation vertices. A grip token is validated against
/// the current entity shape before any property can be modified.</summary>
public static class DraftingGrips
{
    public static ImmutableArray<DraftingGrip> Read(ShadowSnapshot snapshot, IEnumerable<ObjectId> entities, int maximum = 2048)
    {
        var result = ImmutableArray.CreateBuilder<DraftingGrip>();
        foreach (var id in entities.Distinct())
        {
            var e = DraftingGuard.Entity(snapshot, id, false);
            if (e["Layer"]["IsLocked"].Boolean()) continue;
            void Add(string property, Vec3 position, string kind = "Point", int index = -1)
            {
                if (result.Count >= maximum) throw new InvalidOperationException("Grip budget exceeded; select fewer objects.");
                result.Add(new(property + ":" + index, id, property, position, kind, index));
            }
            switch (e.Type)
            {
                case "Line": Add("StartPoint", e["StartPoint"].Vector()); Add("EndPoint", e["EndPoint"].Vector());
                    Add("Move", (e["StartPoint"].Vector() + e["EndPoint"].Vector()) / 2, "Move"); break;
                case "Circle": case "Arc":
                    var center = e["Center"].Vector(); var basis = Affine3.ObjectCoordinates(e["Normal"].Vector(Vec3.UnitZ));
                    Add("Center", center, "Move");
                    if (e.Type == "Circle")
                        for (var i = 0; i < 4; i++) Add("Radius", center + (basis.X * Math.Cos(i * Math.PI / 2) + basis.Y * Math.Sin(i * Math.PI / 2)) * e["Radius"].Number(), "Radius", i);
                    else foreach (var property in new[] { "StartAngle", "EndAngle" })
                    { var angle = e[property].Number() * Math.PI / 180; Add(property, center + (basis.X * Math.Cos(angle) + basis.Y * Math.Sin(angle)) * e["Radius"].Number(), "Angle"); }
                    break;
                case "Ellipse": Add("Center", e["Center"].Vector(), "Move"); break;
                case "Polyline2D":
                    var ocs = Affine3.ObjectCoordinates(e["Normal"].Vector(Vec3.UnitZ)); var n = 0;
                    foreach (var vertex in e["Vertexes"].Items) Add("Vertexes", ocs.Point(vertex["Position"].Vector() with { Z = e["Elevation"].Number() }), "Vertex", n++);
                    break;
                case "Spline":
                    var k = 0; foreach (var point in e["ControlPoints"].Items) Add("ControlPoints", point.Vector(), "Control", k++); break;
                case "Point": case "Text": case "MText": case "Insert": case "Image":
                    Add("Position", e["Position"].Vector(), "Move"); break;
                case "AttributeDefinition":
                    Add("Position", netDxf.Shadow.Geometry.CadTextProjection.Read(e).Text.Origin, "Move"); break;
                case "Viewport": Add("Center", e["Center"].Vector(), "Move"); break;
                case "AlignedDimension": case "LinearDimension":
                    var frame = Affine3.ObjectCoordinates(e["Normal"].Vector(Vec3.UnitZ));
                    foreach (var name in new[] { "FirstReferencePoint", "SecondReferencePoint" })
                        Add(name, frame.Point(e[name].Vector() with { Z = e["Elevation"].Number() }), "Point");
                    break;
            }
        }
        return result.ToImmutable();
    }

    public static DraftingPlan Move(ShadowSnapshot snapshot, ObjectId entity, string grip, Vec3 point)
    {
        if (!point.IsFinite || Math.Max(Math.Abs(point.X), Math.Max(Math.Abs(point.Y), Math.Abs(point.Z))) > 1e12)
            throw new ArgumentOutOfRangeException(nameof(point));
        var e = DraftingGuard.Entity(snapshot, entity);
        var handle = Read(snapshot, [entity]).SingleOrDefault(g => g.Id == grip) ?? throw new ShadowConflictException("The source grip is no longer available.");
        var edit = new ShadowGraphEdit(snapshot);
        if (handle.Property == "Move" || handle.Kind == "Move")
        {
            var next = snapshot.Apply(EditOperations.Translate(snapshot, [entity], point - handle.Position));
            return new("GRIP MOVE", ShadowGraphPatch.Between(snapshot, next), []);
        }
        if (handle.Property == "Vertexes")
        {
            var basis = Affine3.ObjectCoordinates(e["Normal"].Vector(Vec3.UnitZ));
            if (!basis.TryInverse(out var inverse)) throw new InvalidOperationException("Invalid entity plane.");
            var local = inverse.Point(point);
            if (Math.Abs(local.Z - e["Elevation"].Number()) > 1e-7) throw new NotSupportedException("A polyline vertex must remain in its authored plane.");
            var vertex = e["Vertexes"].Items.ElementAt(handle.Index);
            edit.Set(vertex.Id!.Value, "Position", ShadowValue.From(local, true));
        }
        else if (handle.Property == "ControlPoints")
        {
            var points = e["ControlPoints"].Items.Select(p => p.Value).ToArray(); points[handle.Index] = ShadowValue.From(point);
            edit.ReplaceSequence(entity, "ControlPoints", points);
        }
        else if (handle.Kind is "Radius" or "Angle")
        {
            var basis = Affine3.ObjectCoordinates(e["Normal"].Vector(Vec3.UnitZ)); var delta = point - e["Center"].Vector();
            if (Math.Abs(Vec3.Dot(delta, basis.Z)) > 1e-7) throw new NotSupportedException("The grip must remain in the entity plane.");
            if (delta.Length < 1e-9) throw new ArgumentException("A radial grip cannot coincide with the center.");
            var value = handle.Kind == "Radius" ? delta.Length : PlanarGeometry.Degrees(new(Vec3.Dot(delta, basis.X), Vec3.Dot(delta, basis.Y)));
            edit.Set(entity, handle.Property, ShadowValue.From(value));
        }
        else if (e[handle.Property].Value is ShadowValue.Structure structure)
        {
            var local = point;
            if (!structure.Fields.ContainsKey("Z"))
            {
                var basis = Affine3.ObjectCoordinates(e["Normal"].Vector(Vec3.UnitZ)); basis.TryInverse(out var inverse); local = inverse.Point(point);
                if (Math.Abs(local.Z - e["Elevation"].Number()) > 1e-7) throw new NotSupportedException("The grip must remain in its authored plane.");
            }
            edit.Set(entity, handle.Property, ShadowValue.From(local, !structure.Fields.ContainsKey("Z")));
        }
        else throw new NotSupportedException("This grip does not expose a writable coordinate.");
        return new("GRIP EDIT", edit.Build(), []);
    }
}
