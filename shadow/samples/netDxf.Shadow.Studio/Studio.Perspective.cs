using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed record PerspectiveViewRequest(long Revision, string Mode, double WorkPlaneZ = 0);
public sealed record PerspectiveMoveRequest(long Revision, double FromX, double FromY, double ToX, double ToY,
    bool Definition = false);

public sealed partial class Studio
{
    private sealed record PerspectiveViewSettings(string Mode = "Disabled", double WorkPlaneZ = 0)
    {
        internal PerspectiveOptions? Options => Mode == "Disabled" ? null : new(
            FieldOfView: Mode == "LensLength" ? PerspectiveFieldOfView.LensLength : PerspectiveFieldOfView.TargetPlaneHeight);
    }
    private PerspectiveViewSettings livePerspective = new();
    private PerspectiveViewSettings PerspectiveView
    {
        get => activeBranch?.PerspectiveView ?? livePerspective;
        set { if (activeBranch is { } branch) branch.PerspectiveView = value; else livePerspective = value; }
    }

    /// <summary>Changes workspace display and the explicit XY work plane; never changes the DXF camera.
    /// The source camera remains editable through ordinary source properties.</summary>
    public object SetPerspectiveView(PerspectiveViewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode is not ("Disabled" or "TargetPlaneHeight" or "LensLength") || !double.IsFinite(request.WorkPlaneZ))
            throw new ArgumentException("Choose a recognized perspective mode and a finite model-space work-plane Z.");
        lock (gate)
        {
            Check(request.Revision);
            var next = new PerspectiveViewSettings(request.Mode, request.WorkPlaneZ);
            if (next == PerspectiveView) return State();
            var epoch = checked(viewVersion + 1);
            PerspectiveView = next; viewVersion = epoch; editor.Select([]);
            return UpdatedState();
        }
    }

    /// <summary>Ray-plane drag. Paper coordinates are not an affine model-space delta.</summary>
    public object TranslatePerspective(PerspectiveMoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            Check(request.Revision);
            if (PerspectiveView.Options is null) throw new InvalidOperationException("Enable perspective before using a perspective work-plane drag.");
            editor.TranslatePerspective(Scene(), new(request.FromX, request.FromY), new(request.ToX, request.ToY),
                new(0, 0, PerspectiveView.WorkPlaneZ), Vec3.UnitZ, request.Definition);
            return UpdatedState();
        }
    }
}
