using System.Collections.Immutable;
using System.Globalization;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Drafting;

/// <summary>Reusable CAD command planners. No netDxf objects, HTTP services, native renderer or UI state.
/// All validation and geometry work precedes publication; each command is one immutable transaction.</summary>
public sealed partial class DraftingPlanner
{
    private readonly DraftingLimits limits;
    public DraftingPlanner(DraftingLimits? limits = null)
    {
        this.limits = limits ?? new();
        if (this.limits.MaximumPoints < 3 || this.limits.MaximumEntities < 1 || this.limits.MaximumClonedObjects < 1 ||
            !(this.limits.MaximumCoordinate > 0) || !double.IsFinite(this.limits.MaximumCoordinate) ||
            !(this.limits.MinimumLength > 0) || !double.IsFinite(this.limits.MinimumLength))
            throw new ArgumentOutOfRangeException(nameof(limits));
    }

    public DraftingPlan Plan(ShadowSnapshot snapshot, DraftingCommand command, string layout = "Model",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var owner = snapshot.Layout(layout)["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown drawing layout.");
        return PlanInOwner(snapshot, command, owner, cancellationToken);
    }

    /// <summary>Plan against a named definition without changing the drawing's active layout.</summary>
    public DraftingPlan PlanInBlock(ShadowSnapshot snapshot, DraftingCommand command, ObjectId definition,
        CancellationToken cancellationToken = default)
    {
        DraftingBlocks.Definition(snapshot, definition);
        return PlanInOwner(snapshot, command, definition, cancellationToken, restrictOwner: true);
    }

    private DraftingPlan PlanInOwner(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner, CancellationToken cancellationToken, bool restrictOwner = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);
        var points = command.Points.IsDefault ? ImmutableArray<Vec3>.Empty : command.Points;
        var ids = command.Entities.IsDefault ? ImmutableArray<ObjectId>.Empty : command.Entities.Distinct().ToImmutableArray();
        if (points.Length > limits.MaximumPoints || ids.Length > limits.MaximumEntities)
            throw new ArgumentException("The drafting command exceeds its configured work budget.");
        foreach (var point in points) CheckPoint(point);
        foreach (var number in new[] { command.Value, command.Secondary, command.Rotation })
            if (!double.IsFinite(number)) throw new ArgumentException("Command parameters must be finite.");
        foreach (var id in ids)
        {
            var entity = DraftingGuard.Entity(snapshot, id);
            if (restrictOwner && entity["Owner"].Id != owner) throw new ArgumentException("The selection must belong to the current editing space.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        command = command with { Name = command.Name.Trim().ToUpperInvariant(), Points = points, Entities = ids };
        return command.Name switch
        {
            "BLOCK" => CreateBlockInOwner(snapshot, new(command.Text ?? "", points.Length == 1 ? points[0] :
                throw new ArgumentException("BLOCK requires one base point."), ids, Layer: command.Layer), owner, cancellationToken),
            "MOVE" or "ROTATE" or "SCALE" or "MIRROR" => Transform(snapshot, command, cancellationToken),
            "STRETCH" => Stretch(snapshot, command, owner, cancellationToken),
            "BREAK" or "BREAKATPOINT" => Break(snapshot, command, owner, cancellationToken),
            "COPY" or "ARRAY" => Copy(snapshot, command, owner, cancellationToken),
            "ERASE" => Erase(snapshot, command),
            "OFFSET" => Offset(snapshot, command, owner, cancellationToken),
            "TRIM" or "EXTEND" => TrimExtend(snapshot, command),
            "FILLET" or "CHAMFER" => Corner(snapshot, command, owner),
            "JOIN" => JoinExplode(snapshot, command, owner),
            "EXPLODE" => Explode(snapshot, command, owner, cancellationToken),
            _ => Create(snapshot, command, owner, cancellationToken)
        };
    }

    private DraftingPlan Create(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner, CancellationToken token)
    {
        var layer = Layer(snapshot, command.Layer);
        if (layer["IsLocked"].Boolean() || layer["IsFrozen"].Boolean())
            throw new InvalidOperationException("Choose an unlocked, thawed layer before drawing.");
        var edit = new ShadowGraphEdit(snapshot); var created = ImmutableArray.CreateBuilder<ObjectId>();
        var p = command.Points;
        ObjectId Add(string type, Dictionary<string, ShadowValue> values)
        {
            token.ThrowIfCancellationRequested();
            if (created.Count >= limits.MaximumEntities) throw new InvalidOperationException("Entity creation budget exceeded.");
            values["Layer"] = layer.Value; values["IsVisible"] = ShadowValue.From(true);
            values.TryAdd("Normal", ShadowValue.From(Vec3.UnitZ));
            values.TryAdd("LinetypeScale", ShadowValue.From(1.0));
            values.TryAdd("Lineweight", new ShadowValue.Atom("netDxf.Lineweight", "ByLayer"));
            var currentLinetype = DraftingResources.Current(snapshot, "linetype");
            var byLayer = snapshot.Document["Linetypes"].Items.FirstOrDefault(v => v["Name"].Text.Equals(currentLinetype.Length == 0 ? "ByLayer" : currentLinetype, StringComparison.OrdinalIgnoreCase));
            if (byLayer.Exists) values.TryAdd("Linetype", byLayer.Value);
            var id = edit.AddEntity(owner, "netDxf.Entities." + type, values); created.Add(id); return id;
        }
        void Polyline(ImmutableArray<Vec3> vertices, bool closed)
        {
            Need(vertices, closed ? 3 : 2); PlanarPoints(vertices);
            for (var i = 1; i < vertices.Length; i++) Length(vertices[i] - vertices[i - 1]);
            if (closed) Length(vertices[^1] - vertices[0]);
            var values = vertices.Select(v => (ShadowValue)new ShadowValue.Reference(edit.Create("netDxf.Entities.Polyline2DVertex",
                ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
                { ["Position"] = ShadowValue.From(v, true), ["Bulge"] = ShadowValue.From(0.0),
                    ["StartWidth"] = ShadowValue.From(0.0), ["EndWidth"] = ShadowValue.From(0.0) }))).ToImmutableArray();
            Add("Polyline2D", new() { ["Vertexes"] = new ShadowValue.Sequence(values), ["IsClosed"] = ShadowValue.From(closed),
                ["Elevation"] = ShadowValue.From(0.0), ["Thickness"] = ShadowValue.From(0.0) });
        }
        switch (command.Name)
        {
            case "LINE":
                Need(p, 2);
                for (var i = 1; i < p.Length; i++)
                { Length(p[i] - p[i - 1]); Add("Line", new() { ["StartPoint"] = V(p[i - 1]), ["EndPoint"] = V(p[i]) }); }
                if (command.Closed && p.Length > 2) { Length(p[^1] - p[0]); Add("Line", new() { ["StartPoint"] = V(p[^1]), ["EndPoint"] = V(p[0]) }); }
                break;
            case "PLINE": Polyline(p, command.Closed); break;
            case "RECTANG":
                Exact(p, 2); PlanarPoints(p);
                Positive(Math.Abs(p[1].X - p[0].X)); Positive(Math.Abs(p[1].Y - p[0].Y));
                Polyline([p[0], new(p[1].X, p[0].Y), p[1], new(p[0].X, p[1].Y)], true); break;
            case "POLYGON":
                Need(p, 1); PlanarPoints(p);
                if (command.Count is < 3 or > 1024) throw new ArgumentException("Polygon sides must be between 3 and 1024.");
                var radius = p.Length > 1 ? Length(p[1] - p[0]) : Positive(command.Value);
                var phase = p.Length > 1 ? Math.Atan2(p[1].Y - p[0].Y, p[1].X - p[0].X) : command.Rotation * Math.PI / 180;
                Polyline(Enumerable.Range(0, command.Count).Select(i => p[0] +
                    new Vec3(Math.Cos(phase + Math.Tau * i / command.Count), Math.Sin(phase + Math.Tau * i / command.Count)) * radius).ToImmutableArray(), true);
                break;
            case "CIRCLE":
                Need(p, 1); PlanarPoints(p);
                Add("Circle", new() { ["Center"] = V(p[0]), ["Radius"] = N(p.Length > 1 ? Length(p[1] - p[0]) : Positive(command.Value)) }); break;
            case "ARC":
                Exact(p, 3); PlanarPoints(p);
                var arc = PlanarGeometry.ThreePointArc(p[0], p[1], p[2], limits.MinimumLength);
                Add("Arc", new() { ["Center"] = V(arc.Center), ["Radius"] = N(arc.Radius),
                    ["StartAngle"] = N(arc.StartDegrees), ["EndAngle"] = N(arc.EndDegrees) }); break;
            case "ELLIPSE":
                Exact(p, 3); PlanarPoints(p);
                var axis = p[1] - p[0]; var major = Length(axis);
                var minor = Positive(Math.Abs(PlanarGeometry.Cross(axis / major, p[2] - p[0])));
                var rotation = Math.Atan2(axis.Y, axis.X) * 180 / Math.PI;
                if (minor > major) { (major, minor) = (minor, major); rotation += 90; }
                Add("Ellipse", new() { ["Center"] = V(p[0]), ["MajorAxis"] = N(major * 2), ["MinorAxis"] = N(minor * 2),
                    ["Rotation"] = N(rotation), ["StartAngle"] = N(0), ["EndAngle"] = N(360) }); break;
            case "POINT":
                Exact(p, 1); Add("Point", new() { ["Position"] = V(p[0]) }); break;
            case "TEXT": case "MTEXT":
                Exact(p, 1);
                if (string.IsNullOrWhiteSpace(command.Text) || command.Text.Length > 16_384)
                    throw new ArgumentException("Text requires 1–16384 characters.");
                var text = new Dictionary<string, ShadowValue> { ["Position"] = V(p[0]), ["Height"] = N(Positive(command.Value)),
                    ["Value"] = ShadowValue.From(command.Text), ["Rotation"] = N(command.Rotation) };
                if (command.Name == "TEXT") { text["WidthFactor"] = N(1); text["ObliqueAngle"] = N(0); }
                else text["RectangleWidth"] = N(Math.Max(0, command.Secondary));
                var currentTextStyle = DraftingResources.Current(snapshot, "textstyle");
                var style = snapshot.Document["TextStyles"].Items.FirstOrDefault(v => v["Name"].Text.Equals(currentTextStyle.Length == 0 ? "Standard" : currentTextStyle, StringComparison.OrdinalIgnoreCase));
                if (style.Exists)
                {
                    text["Style"] = style.Value;
                    if (style["Height"].Number() > 0) text["Height"] = N(style["Height"].Number());
                    if (command.Name == "TEXT")
                    {
                        text["WidthFactor"] = N(style["WidthFactor"].Number(1)); text["ObliqueAngle"] = N(style["ObliqueAngle"].Number());
                        text["IsBackward"] = ShadowValue.From(style["IsBackward"].Boolean()); text["IsUpsideDown"] = ShadowValue.From(style["IsUpsideDown"].Boolean());
                    }
                }
                Add(command.Name == "TEXT" ? "Text" : "MText", text); break;
            case "SPLINE":
                Need(p, 4);
                // A clamped cubic control-point spline, not a mislabeled polyline or fit-point approximation.
                var count = p.Length;
                var knots = Enumerable.Range(0, count + 4).Select(i => N(i < 4 ? 0 : i >= count ? 1 : (double)(i - 3) / (count - 3))).ToImmutableArray();
                Add("Spline", new() { ["ControlPoints"] = new ShadowValue.Sequence(p.Select(V).ToImmutableArray()),
                    ["Weights"] = new ShadowValue.Sequence(Enumerable.Repeat(N(1), count).ToImmutableArray()),
                    ["Knots"] = new ShadowValue.Sequence(knots), ["Degree"] = new ShadowValue.Atom("System.Int16", "3"),
                    ["IsClosedPeriodic"] = ShadowValue.From(false) }); break;
            case "DIMALIGNED": case "DIMLINEAR":
                Exact(p, 3); PlanarPoints(p); var direction = p[1] - p[0]; var distance = Length(direction);
                var dimension = new Dictionary<string, ShadowValue> { ["FirstReferencePoint"] = V2(p[0]),
                    ["SecondReferencePoint"] = V2(p[1]), ["Offset"] = N(PlanarGeometry.Cross(direction / distance, p[2] - p[0])),
                    ["Elevation"] = N(0), ["UserText"] = ShadowValue.From(command.Text ?? "<>"),
                    ["TextRotation"] = N(0), ["TextPositionManuallySet"] = ShadowValue.From(false),
                    ["AttachmentPoint"] = new ShadowValue.Atom("netDxf.Entities.MTextAttachmentPoint", "MiddleCenter"),
                    ["LineSpacingStyle"] = new ShadowValue.Atom("netDxf.Entities.MTextLineSpacingStyle", "AtLeast"),
                    ["LineSpacingFactor"] = N(1) };
                if (command.Name == "DIMLINEAR")
                {
                    dimension["Rotation"] = N(command.Rotation);
                    var u = new Vec3(Math.Cos(command.Rotation * Math.PI / 180), Math.Sin(command.Rotation * Math.PI / 180));
                    dimension["Offset"] = N(PlanarGeometry.Cross(u, p[2] - p[0]));
                }
                var dimStyle = snapshot.Document["DimensionStyles"].Items.FirstOrDefault(v => v["Name"].Text.Equals("Standard", StringComparison.OrdinalIgnoreCase));
                if (dimStyle.Exists) dimension["Style"] = dimStyle.Value;
                Add(command.Name == "DIMALIGNED" ? "AlignedDimension" : "LinearDimension", dimension); break;
            case "INSERT":
                Exact(p, 1);
                if (command.Reference is not { } block || snapshot.Read(block).Object is not { Kind: ShadowObjectKind.Block } blockNode ||
                    blockNode["Name"] is not ShadowValue.Atom blockName || blockName.Text.StartsWith('*'))
                    throw new ArgumentException("Choose an existing named block definition.");
                DraftingBlocks.ValidateReference(snapshot, owner, block, limits.MaximumClonedObjects, token);
                var scale = command.Scale ?? new Vec3(command.Value == 0 ? 1 : command.Value, command.Value == 0 ? 1 : command.Value, command.Value == 0 ? 1 : command.Value);
                CheckInsertScale(scale);
                if (snapshot.Read(block)["Record"]["ScaleUniformly"].Boolean() &&
                    (Math.Abs(Math.Abs(scale.X) - Math.Abs(scale.Y)) > limits.MinimumLength || Math.Abs(Math.Abs(scale.X) - Math.Abs(scale.Z)) > limits.MinimumLength))
                    throw new ArgumentException("This block definition requires uniform scale magnitudes on all axes.");
                var position = p[0];
                if (command.BasePoint is { } basePoint)
                {
                    CheckPoint(basePoint);
                    var linear = Affine3.RotateZ(command.Rotation * Math.PI / 180) * Affine3.Scale(scale * BlockUnitFactor(snapshot, owner, snapshot.Read(block)));
                    position += linear.Direction(snapshot.Read(block)["Origin"].Vector() - basePoint);
                    CheckPoint(position);
                }
                Add("Insert", new() { ["Block"] = new ShadowValue.Reference(block), ["Position"] = V(position),
                    ["Scale"] = V(scale), ["Rotation"] = N(command.Rotation) }); break;
            default: throw new NotSupportedException($"Unknown drafting command '{command.Name}'.");
        }
        token.ThrowIfCancellationRequested();
        return new(command.Name, edit.Build(), created.ToImmutable());
    }

    private DraftingPlan Erase(ShadowSnapshot snapshot, DraftingCommand command)
    {
        Selected(command, 1); var edit = new ShadowGraphEdit(snapshot); edit.RemoveEntities(command.Entities);
        return new("ERASE", edit.Build(), []);
    }

    private DraftingPlan Transform(ShadowSnapshot snapshot, DraftingCommand command, CancellationToken token)
    {
        Selected(command, 1); Need(command.Points, 1);
        var p = command.Points; var origin = p[0];
        if (command.Name == "MOVE")
        {
            Exact(p, 2); var after = snapshot.Apply(EditOperations.Translate(snapshot, command.Entities, p[1] - p[0]));
            return new("MOVE", ShadowGraphPatch.Between(snapshot, after), []);
        }
        Affine3 local;
        switch (command.Name)
        {
            case "ROTATE": local = Affine3.RotateZ(command.Value * Math.PI / 180); break;
            case "SCALE": var amount = Positive(command.Value); local = Affine3.Scale(new(amount, amount, amount)); break;
            default:
                Exact(p, 2); PlanarPoints(p); Length(p[1] - p[0]);
                var angle = Math.Atan2(p[1].Y - p[0].Y, p[1].X - p[0].X);
                local = Affine3.RotateZ(angle) * Affine3.Scale(new(1, -1, 1)) * Affine3.RotateZ(-angle); break;
        }
        return new(command.Name, AffineEditOperations.Plan(snapshot, command.Entities,
            Affine3.Move(origin) * local * Affine3.Move(-origin), cancellationToken: token), []);
    }

    private static ShadowReader Layer(ShadowSnapshot snapshot, string name)
    {
        var result = snapshot.Document["Layers"].Items.FirstOrDefault(v => v["Name"].Text.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (result.Id is null) throw new ArgumentException($"Unknown layer '{name}'.");
        return result;
    }
    private void CheckPoint(Vec3 value)
    {
        if (!value.IsFinite || Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z))) > limits.MaximumCoordinate)
            throw new ArgumentOutOfRangeException(nameof(value), "Coordinates exceed the finite drafting range.");
    }
    private double Positive(double value)
    {
        if (!double.IsFinite(value) || value < limits.MinimumLength || value > limits.MaximumCoordinate)
            throw new ArgumentOutOfRangeException(nameof(value), "Specify a positive, finite distance in the configured drafting range.");
        return value;
    }
    private double Length(Vec3 value) => Positive(value.Length);
    private void PlanarPoints(ImmutableArray<Vec3> points)
    { if (points.Any(p => Math.Abs(p.Z) > limits.MinimumLength)) throw new NotSupportedException("This command requires points in the world XY plane."); }
    private static void Need(ImmutableArray<Vec3> points, int minimum)
    { if (points.Length < minimum) throw new ArgumentException($"This command requires at least {minimum} points."); }
    private static void Exact(ImmutableArray<Vec3> points, int count)
    { if (points.Length != count) throw new ArgumentException($"This command requires exactly {count} points."); }
    private static void Selected(DraftingCommand command, int count)
    { if (command.Entities.Length < count) throw new ArgumentException($"Select at least {count} entities."); }
    private static ShadowValue V(Vec3 point) => ShadowValue.From(point);
    private static ShadowValue V2(Vec3 point) => ShadowValue.From(point, true);
    private static ShadowValue N(double value) => ShadowValue.From(value);
}
