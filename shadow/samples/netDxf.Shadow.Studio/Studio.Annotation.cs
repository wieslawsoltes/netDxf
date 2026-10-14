using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed record AnnotationViewRequest(long Revision, string Mode, double PaperUnits = 1, double DrawingUnits = 1);

public sealed partial class Studio
{
    private ModelAnnotationContext liveAnnotation = ModelAnnotationContext.Default;
    private ModelAnnotationContext Annotation
    {
        get => activeBranch?.Annotation ?? liveAnnotation;
        set { if (activeBranch is { } branch) branch.Annotation = value; else liveAnnotation = value; }
    }

    /// <summary>Workspace presentation only. Drawing mode reads captured values; Enabled uses explicit units.</summary>
    public object SetAnnotationView(AnnotationViewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode is not ("Drawing" or "Disabled" or "Enabled")) throw new ArgumentException("Unknown annotation mode.");
        var units = new AnnotationScale(request.PaperUnits, request.DrawingUnits);
        _ = units.Multiplier; // Reject invalid API values even in disabled/drawing mode.
        var context = new ModelAnnotationContext(request.Mode == "Enabled" ? units : null,
            Enum.Parse<ModelAnnotationMode>(request.Mode));
        lock (gate)
        {
            Check(request.Revision);
            if (Annotation == context) return State();
            var next = checked(viewVersion + 1);
            Annotation = context; viewVersion = next;
            editor.Select([]);
            return UpdatedState();
        }
    }
}
