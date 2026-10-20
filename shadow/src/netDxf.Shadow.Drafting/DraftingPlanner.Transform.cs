using System.Collections.Immutable;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Drafting;

public sealed partial class DraftingPlanner
{
    private DraftingPlan Transform(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner, CancellationToken token)
    {
        Selected(command, 1); Need(command.Points, 1);
        var p = command.Points; var origin = p[0];
        if (command.Name == "MOVE")
        {
            Exact(p, 2); var after = snapshot.Apply(EditOperations.Translate(snapshot, command.Entities, p[1] - p[0]));
            return new("MOVE", ShadowGraphPatch.Between(snapshot, after), []);
        }
        Affine3 local;
        if (command.Name is "ROTATE" or "SCALE")
        {
            Exact(p, 1);
            var options = command.Transform;
            if ((options?.Reference is null) != (options?.Target is null))
                throw new ArgumentException("Reference transforms require both a reference and a target measure.");
            var value = command.Value;
            if (options?.Reference is { } reference)
            {
                var source = TransformMeasure(reference, command.Name);
                var target = TransformMeasure(options.Target!, command.Name);
                value = command.Name == "ROTATE" ? target - source : target / source;
            }
            if (command.Name == "ROTATE")
            {
                if (!double.IsFinite(value)) throw new ArgumentException("Rotation must be finite.");
                // Exact full turns stay identity transforms, including around a nonzero base point.
                value %= 360;
                local = value == 0 ? Affine3.Identity : Affine3.RotateZ(value * Math.PI / 180);
            }
            else
            {
                var amount = Positive(value);
                local = amount == 1 ? Affine3.Identity : Affine3.Scale(new(amount, amount, amount));
            }
        }
        else
        {
            Exact(p, 2); PlanarPoints(p); Length(p[1] - p[0]);
            var angle = Math.Atan2(p[1].Y - p[0].Y, p[1].X - p[0].X);
            local = Affine3.RotateZ(angle) * Affine3.Scale(new(1, -1, 1)) * Affine3.RotateZ(-angle);
        }
        var transform = local == Affine3.Identity ? local : Affine3.Move(origin) * local * Affine3.Move(-origin);
        if (command.Transform?.Copy != true)
            return new(command.Name, AffineEditOperations.Plan(snapshot, command.Entities, transform,
                cancellationToken: token), []);

        // Use the same bounded, resource-sharing clone graph as COPY. The complete
        // cloned-and-transformed result is detached until one atomic source commit.
        foreach (var id in command.Entities)
            if (snapshot.Read(id)["Owner"].Id != owner)
                throw new ArgumentException("Copied transform roots must belong to the current editing space.");
        var edit = new ShadowGraphEdit(snapshot); var budget = 0;
        var copies = Clone(snapshot, edit, command.Entities, owner, token, ref budget);
        var intermediate = edit.Build().Apply(snapshot);
        var transformed = AffineEditOperations.Plan(intermediate, copies, transform,
            cancellationToken: token).Apply(intermediate);
        return new(command.Name + " Copy", ShadowGraphPatch.Between(snapshot, transformed), copies);
    }

    private double TransformMeasure(DraftingTransformMeasure measure, string command)
    {
        var points = measure.Points.IsDefault ? ImmutableArray<Vec3>.Empty : measure.Points;
        if (measure.Value.HasValue == !points.IsEmpty)
            throw new ArgumentException("Specify either a scalar measure or exactly two reference points.");
        double value;
        if (measure.Value is { } scalar)
        {
            if (!double.IsFinite(scalar) || Math.Abs(scalar) > limits.MaximumCoordinate)
                throw new ArgumentException("Reference measures must be finite and within the configured drafting range.");
            value = scalar;
        }
        else
        {
            Exact(points, 2);
            foreach (var point in points) CheckPoint(point);
            var delta = points[1] - points[0];
            if (command == "ROTATE")
            {
                // Angles are measured in the WCS XY plane; tilted source entities
                // retain the existing affine planner's representation constraints.
                Length(new(delta.X, delta.Y));
                value = Math.Atan2(delta.Y, delta.X) * 180 / Math.PI;
            }
            else value = Length(delta);
        }
        return command == "SCALE" ? Positive(value) : value;
    }
}
