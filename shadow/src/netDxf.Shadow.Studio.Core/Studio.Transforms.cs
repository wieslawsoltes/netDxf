namespace netDxf.Shadow.Studio;

public sealed record TransformRequest(long Revision, string Operation, double Amount = 1, bool Definition = false);

public sealed partial class Studio
{
    public object Transform(TransformRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (!double.IsFinite(request.Amount)) throw new ArgumentException("The transform amount must be finite.");
            var scene = Scene(); var selected = editor.State.Selection;
            var bounds = scene.Items.Where(i => selected.Contains(i.Key)).Aggregate(Bounds2.Empty, (b, item) => b.Union(item.Bounds));
            if (bounds.IsEmpty) throw new ArgumentException("Select geometry before transforming it.");
            var local = request.Operation switch
            {
                "rotate" => Affine3.RotateZ(request.Amount * Math.PI / 180),
                "scale" when request.Amount > 0 => Affine3.Scale(new(request.Amount, request.Amount, request.Amount)),
                "mirror-x" => Affine3.Scale(new(-1, 1, 1)),
                "mirror-y" => Affine3.Scale(new(1, -1, 1)),
                _ => throw new ArgumentException("Choose rotate, positive scale, mirror-x or mirror-y.")
            };
            var transform = Affine3.Move(bounds.Center) * local * Affine3.Move(-bounds.Center);
            editor.Transform(scene, transform, request.Definition);
            return UpdatedState();
        }
    }
}
