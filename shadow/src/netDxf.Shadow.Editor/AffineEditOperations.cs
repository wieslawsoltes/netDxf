using System.Collections.Immutable;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Editor;

public sealed record AffineEditOptions(int MaximumEntities = 100_000, int MaximumCoordinates = 1_000_000,
    double RelativeTolerance = 1e-10);
public readonly record struct AffineEntityEdit(ObjectId Entity, Affine3 Transform);

/// <summary>Representation-preserving affine plans over detached data. The result is one immutable
/// transaction, including readonly coordinate collections. No source or renderer is invoked.</summary>
public static partial class AffineEditOperations
{
    public static ShadowGraphPatch Plan(ShadowSnapshot snapshot, IEnumerable<ObjectId> entities,
        Affine3 transform, AffineEditOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return Plan(snapshot, entities.Select(id => new AffineEntityEdit(id, transform)), options, cancellationToken);
    }

    public static ShadowGraphPatch Plan(ShadowSnapshot snapshot, IEnumerable<AffineEntityEdit> edits,
        AffineEditOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(edits);
        var planner = new Planner(snapshot, options ?? new(), cancellationToken);
        foreach (var edit in edits) planner.Visit(snapshot.Read(edit.Entity), edit.Transform);
        return planner.Build();
    }

    /// <summary>Conjugates a display-space transform through paper/insert coordinates. Conflicting
    /// selections of the same definition through different views are rejected atomically.</summary>
    public static ShadowGraphPatch PlanSelection(ShadowSnapshot snapshot, RenderScene scene,
        IEnumerable<string> selection, Affine3 transform, bool editBlockDefinition = false,
        AffineEditOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);
        if (snapshot.DocumentId != scene.DocumentId || snapshot.Revision != scene.Revision)
            throw new ShadowConflictException("Selection belongs to an obsolete rendered snapshot.");
        Validate(transform);
        var keys = selection.ToImmutableHashSet(StringComparer.Ordinal);
        var targets = new List<AffineEntityEdit>();
        foreach (var item in scene.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!keys.Contains(item.Key)) continue;
            var map = editBlockDefinition ? item.SourceToWorld : item.Projection?.ModelToPaper ?? Affine3.Identity;
            if (!map.TryInverse(out var inverse)) throw new NotSupportedException("Singular selection coordinates.");
            var id = !editBlockDefinition && !item.InsertPath.IsEmpty ? item.InsertPath[0] : item.Entity;
            targets.Add(new(id, inverse * transform * map));
        }
        return Plan(snapshot, targets, options, cancellationToken);
    }

    private static void Validate(Affine3 transform)
    {
        if (!transform.X.IsFinite || !transform.Y.IsFinite || !transform.Z.IsFinite || !transform.Translation.IsFinite ||
            !transform.TryInverse(out _)) throw new ArgumentException("The affine edit must be finite and invertible.");
    }

    private sealed partial class Planner
    {
        private readonly ShadowSnapshot snapshot;
        private readonly ShadowGraphEdit edit;
        private readonly AffineEditOptions options;
        private readonly CancellationToken cancellation;
        private readonly Dictionary<ObjectId, Affine3> visited = new();
        private readonly Dictionary<(ObjectId, string), ShadowValue> assigned = new();
        private int coordinates;
        internal Planner(ShadowSnapshot snapshot, AffineEditOptions options, CancellationToken cancellation)
        {
            if (options.MaximumEntities < 1 || options.MaximumCoordinates < 1 ||
                !(options.RelativeTolerance > 0 && options.RelativeTolerance < 0.01))
                throw new ArgumentOutOfRangeException(nameof(options));
            this.snapshot = snapshot; this.options = options; this.cancellation = cancellation; edit = new(snapshot);
        }
        internal ShadowGraphPatch Build() { cancellation.ThrowIfCancellationRequested(); return edit.Build(); }
        internal void Visit(ShadowReader entity, Affine3 transform)
        {
            cancellation.ThrowIfCancellationRequested(); Validate(transform);
            if (entity.Id is not { } id || entity.Object is not { } node || (node.Kind & ShadowObjectKind.Entity) == 0 && node.Type != "Attribute")
                throw new ArgumentException("Affine targets must be existing entities.");
            if (visited.TryGetValue(id, out var previous))
            {
                if (!Same(previous, transform)) throw new ShadowConflictException("A shared entity has incompatible affine edits.");
                return;
            }
            if (visited.Count >= options.MaximumEntities) throw new InvalidOperationException("Affine entity budget exceeded.");
            visited.Add(id, transform);
            if (transform == Affine3.Identity) return;
            switch (entity.Type)
            {
                case "Point": Point(entity, "Position", transform); Extrusion(entity, transform); break;
                case "Line": Point(entity, "StartPoint", transform); Point(entity, "EndPoint", transform); Extrusion(entity, transform); break;
                case "Face3D":
                    foreach (var name in new[] { "FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex" }) Point(entity, name, transform);
                    break;
                case "Polyline3D": case "Mesh": case "PolyfaceMesh":
                    Sequence(entity, "Vertexes", transform); break;
                case "Spline":
                    Sequence(entity, "ControlPoints", transform);
                    if (entity["FitPoints"].Items.Any()) Sequence(entity, "FitPoints", transform);
                    foreach (var name in new[] { "StartTangent", "EndTangent" })
                        if (entity[name].Exists) Point(entity, name, transform, direction: true);
                    break;
                case "Circle": case "Arc": Conic(entity, transform); break;
                case "Ellipse": Ellipse(entity, transform); break;
                case "Polyline2D": Polyline(entity, transform); break;
                case "Solid": case "Trace":
                    var plane = Plane(entity, transform);
                    foreach (var name in new[] { "FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex" })
                        Vector(entity, name, plane.Local.Point(entity[name].Vector() with { Z = entity["Elevation"].Number() }));
                    SetPlane(entity, plane); Thickness(entity, transform, plane.Normal); break;
                case "Image": Image(entity, transform); break;
                case "Text": case "Attribute": case "AttributeDefinition": Text(entity, transform); break;
                case "MText": MultilineText(entity, transform); break;
                case "Insert": Insert(entity, transform); break;
                default: throw new NotSupportedException($"Affine editing of {entity.Type} requires a specialized planner; no source was changed.");
            }
        }
        private bool Same(Affine3 a, Affine3 b) => Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z) && Near(a.Translation, b.Translation);
        private bool Near(Vec3 a, Vec3 b) => (a - b).Length <= options.RelativeTolerance * Math.Max(1, Math.Max(a.Length, b.Length));
        private void Set(ShadowReader entity, string name, ShadowValue value)
        {
            cancellation.ThrowIfCancellationRequested();
            var id = entity.Id ?? throw new NotSupportedException("The value is not an independently editable node.");
            var key = (id, name);
            if (assigned.TryGetValue(key, out var old) && !ShadowValue.Equivalent(old, value))
                throw new ShadowConflictException($"Shared property {id}.{name} has incompatible edits.");
            assigned[key] = value;
            if (!ShadowValue.Equivalent(entity[name].Value, value)) edit.Set(id, name, value);
        }
        private void Number(ShadowReader entity, string name, double value)
        {
            if (!double.IsFinite(value)) throw new ArithmeticException("Affine value is not representable.");
            Set(entity, name, ShadowValue.From(value));
        }
        private ShadowValue Coordinate(ShadowReader before, Vec3 point)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++coordinates > options.MaximumCoordinates) throw new InvalidOperationException("Affine coordinate budget exceeded.");
            if (!point.IsFinite) throw new ArithmeticException("Affine coordinate is not representable.");
            if (before.Value is not ShadowValue.Structure structure || !structure.Fields.ContainsKey("X"))
                throw new NotSupportedException("The source value is not a coordinate structure.");
            var fields = structure.Fields.SetItem("X", ShadowValue.From(point.X)).SetItem("Y", ShadowValue.From(point.Y));
            if (fields.ContainsKey("Z")) fields = fields.SetItem("Z", ShadowValue.From(point.Z));
            return structure with { Fields = fields };
        }
        private void Vector(ShadowReader entity, string name, Vec3 value) => Set(entity, name, Coordinate(entity[name], value));
        private void Point(ShadowReader entity, string name, Affine3 transform, bool direction = false)
            => Vector(entity, name, direction ? transform.Direction(entity[name].Vector()) : transform.Point(entity[name].Vector()));
        private void Sequence(ShadowReader entity, string name, Affine3 transform)
        {
            var values = ImmutableArray.CreateBuilder<ShadowValue>();
            foreach (var value in entity[name].Items)
            {
                if (value.Id is not null) { Point(value, "Position", transform); values.Add(value.Value); }
                else values.Add(Coordinate(value, transform.Point(value.Vector())));
            }
            edit.ReplaceSequence(entity.Id!.Value, name, values);
        }
        private void Extrusion(ShadowReader entity, Affine3 transform)
        {
            var oldNormal = entity["Normal"].Vector(Vec3.UnitZ);
            var transformed = transform.Direction(oldNormal);
            var length = transformed.Length;
            if (!(length > 0) || !double.IsFinite(length)) throw new ArithmeticException("Invalid extrusion direction.");
            Vector(entity, "Normal", transformed / length);
            if (entity["Thickness"].Exists) Number(entity, "Thickness", entity["Thickness"].Number() * length);
        }
        private void Thickness(ShadowReader entity, Affine3 transform, Vec3 normal)
        {
            var thickness = entity["Thickness"].Number();
            if (thickness == 0) return;
            var direction = transform.Direction(entity["Normal"].Vector(Vec3.UnitZ));
            var scale = Vec3.Dot(direction, normal);
            if (!Near(direction, normal * scale)) throw new NotSupportedException("Oblique extrusion is not representable by this DXF entity.");
            Number(entity, "Thickness", thickness * scale);
        }
        private readonly record struct PlaneData(Affine3 Local, Affine3 Basis, Vec3 Normal, double ScaleX, double ScaleY, bool Similarity);
        private PlaneData Plane(ShadowReader entity, Affine3 transform)
        {
            var old = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
            var u = transform.Direction(old.X); var v = transform.Direction(old.Y);
            var sx = u.Length; var sy = v.Length;
            if (!(sx > 0 && sy > 0) || !double.IsFinite(sx + sy)) throw new ArithmeticException("Invalid affine plane.");
            var normal = Vec3.Cross(u / sx, v / sy).Normalized;
            var basis = Affine3.ObjectCoordinates(normal);
            if (!basis.TryInverse(out var inverse)) throw new ArithmeticException("Invalid object coordinate frame.");
            var similar = Math.Abs(sx - sy) <= options.RelativeTolerance * Math.Max(sx, sy) &&
                Math.Abs(Vec3.Dot(u / sx, v / sy)) <= options.RelativeTolerance;
            return new(inverse * transform * old, basis, normal, sx, sy, similar);
        }
        private void SetPlane(ShadowReader entity, PlaneData plane)
        {
            Vector(entity, "Normal", plane.Normal);
            if (entity["Elevation"].Exists)
                Number(entity, "Elevation", plane.Local.Point(new(0, 0, entity["Elevation"].Number())).Z);
        }
        private static double Degrees(Vec3 value) => Math.Atan2(value.Y, value.X) * 180 / Math.PI;
        private void RequireSimilarity(PlaneData plane)
        { if (!plane.Similarity) throw new NotSupportedException("This entity requires a plane similarity; nonuniform scale/shear would change its DXF representation."); }
    }
}
