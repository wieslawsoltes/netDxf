using System.Collections.Immutable;

namespace netDxf.Shadow.Editor;

/// <summary>Atomic, detached edit planners. Unsupported transforms fail before any source mutation.</summary>
public static class EditOperations
{
    public static ShadowEditBatch Translate(ShadowSnapshot snapshot, IEnumerable<ObjectId> ids, Vec3 delta)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(ids);
        if (!delta.IsFinite) throw new ArgumentOutOfRangeException(nameof(delta));
        var changes = new Dictionary<(ObjectId, string), ShadowPropertyEdit>();
        foreach (var id in ids.Distinct())
        {
            var entity = snapshot.Read(id);
            if (!entity.Exists || entity.Object is null) throw new ArgumentException($"Unknown object {id}.", nameof(ids));
            if (DimensionTranslation.TryPlan(entity, delta, out var dimensionEdits))
            {
                foreach (var edit in dimensionEdits) changes[(edit.Object, edit.Property)] = edit;
                continue;
            }
            switch (entity.Type)
            {
                case "Line": Move(entity, "StartPoint", delta); Move(entity, "EndPoint", delta); break;
                case "Viewport":
                    if (entity["ClippingBoundary"].Id is { } boundaryId)
                        foreach (var edit in Translate(snapshot, [boundaryId], delta).Edits)
                            changes[(edit.Object, edit.Property)] = edit;
                    Move(entity, "Center", delta); break;
                case "Circle": case "Arc": case "Ellipse": Move(entity, "Center", delta); break;
                case "Attribute": case "AttributeDefinition": MoveAttribute(entity); break;
                case "Text": case "MText": case "Point": case "Image":
                    Move(entity, "Position", delta); break;
                case "Insert":
                    Move(entity, "Position", delta);
                    foreach (var attribute in entity["Attributes"].Items) MoveAttribute(attribute);
                    break;
                case "Polyline2D":
                    var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
                    var local = new Vec3(Vec3.Dot(delta, basis.X), Vec3.Dot(delta, basis.Y), Vec3.Dot(delta, basis.Z));
                    foreach (var vertex in entity["Vertexes"].Items) Move(vertex, "Position", local with { Z = 0 });
                    if (Math.Abs(local.Z) > 0) Change(entity, "Elevation", ShadowValue.From(entity["Elevation"].Number() + local.Z));
                    break;
                case "Polyline3D": case "Leader": case "Mesh":
                    MoveSequence(entity, "Vertexes", delta); break;
                case "Spline":
                    MoveSequence(entity, "ControlPoints", delta); break;
                case "Face3D":
                    foreach (var property in new[] { "FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex" }) Move(entity, property, delta);
                    break;
                case "Solid": case "Trace":
                    var ocs = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
                    var offset = new Vec3(Vec3.Dot(delta, ocs.X), Vec3.Dot(delta, ocs.Y), Vec3.Dot(delta, ocs.Z));
                    foreach (var property in new[] { "FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex" }) Move(entity, property, offset with { Z = 0 });
                    Change(entity, "Elevation", ShadowValue.From(entity["Elevation"].Number() + offset.Z));
                    break;
                default: throw new NotSupportedException($"Translation of {entity.Type} requires a specialized edit planner. No changes have been applied.");
            }
        }
        return new(snapshot.DocumentId, snapshot.Revision, changes.Values.ToImmutableArray());

        void MoveAttribute(ShadowReader attribute)
        {
            var patch = AffineEditOperations.Plan(snapshot, [attribute.Id!.Value], Affine3.Move(delta));
            foreach (var item in patch.Changes)
                foreach (var property in item.After!.Properties)
                    if (!ShadowValue.Equivalent(item.Before![property.Key], property.Value)) Change(snapshot.Read(item.Id), property.Key, property.Value);
        }

        void Change(ShadowReader reader, string property, ShadowValue after)
        {
            if (reader.Id is not { } id || reader.Object is not { } node || !node.WritableProperties.Contains(property))
                throw new NotSupportedException($"{reader.Type}.{property} is not writable through this source adapter.");
            var before = node[property];
            if (!ShadowValue.Equivalent(before, after)) changes[(id, property)] = new(id, property, before, after);
        }
        void Move(ShadowReader reader, string property, Vec3 movement)
        {
            var value = reader[property];
            if (value.Value is not ShadowValue.Structure vector || !vector.Fields.ContainsKey("X"))
                throw new NotSupportedException($"{reader.Type}.{property} is not a coordinate value.");
            var fields = vector.Fields.SetItem("X", ShadowValue.From(value["X"].Number() + movement.X)).SetItem("Y", ShadowValue.From(value["Y"].Number() + movement.Y));
            if (fields.ContainsKey("Z")) fields = fields.SetItem("Z", ShadowValue.From(value["Z"].Number() + movement.Z));
            Change(reader, property, vector with { Fields = fields });
        }
        void MoveSequence(ShadowReader reader, string property, Vec3 movement)
        {
            var value = reader[property];
            if (value.Value is not ShadowValue.Sequence sequence)
                throw new NotSupportedException($"{reader.Type}.{property} requires a collection editor.");
            var items = sequence.Items.Select(item => item is ShadowValue.Structure structure
                ? (ShadowValue)(structure with { Fields = structure.Fields.SetItem("X", ShadowValue.From(new ShadowReader(snapshot, item)["X"].Number() + movement.X))
                    .SetItem("Y", ShadowValue.From(new ShadowReader(snapshot, item)["Y"].Number() + movement.Y))
                    .SetItem("Z", ShadowValue.From(new ShadowReader(snapshot, item)["Z"].Number() + movement.Z)) })
                : throw new NotSupportedException("Only direct coordinate arrays can use this edit planner.")).ToImmutableArray();
            Change(reader, property, new ShadowValue.Sequence(items));
        }
    }
}
