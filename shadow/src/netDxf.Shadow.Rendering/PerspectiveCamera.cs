using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public enum PerspectiveFieldOfView { TargetPlaneHeight, LensLength }

/// <summary>Explicit projective view policy. Null on PaperSpaceOptions preserves legacy refusal.
/// The near distance is relative to the authored eye-to-target distance, never the drawing units.</summary>
public sealed record PerspectiveOptions(PerspectiveFieldOfView FieldOfView = PerspectiveFieldOfView.TargetPlaneHeight,
    double FilmHeightMillimeters = 24, double NearDistanceFraction = 1.0 / 64,
    int MaximumPoints = 1_000_000, int MaximumWork = 8_000_000)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(FieldOfView) || !(FilmHeightMillimeters > 0) || !double.IsFinite(FilmHeightMillimeters) ||
            !(NearDistanceFraction > 0 && NearDistanceFraction < 1) || !double.IsFinite(NearDistanceFraction) ||
            MaximumPoints < 4 || MaximumWork < 4) throw new ArgumentOutOfRangeException(nameof(PerspectiveOptions));
    }
}

public readonly record struct PickingRay(Vec3 Origin, Vec3 Direction)
{
    /// <summary>Intersects a forward ray with a caller-selected model-space work plane.</summary>
    public bool TryIntersectPlane(Vec3 origin, Vec3 normal, out Vec3 point)
    {
        point = default;
        var normalLength = normal.Length; var directionLength = Direction.Length;
        if (!Origin.IsFinite || !Direction.IsFinite || !origin.IsFinite || !normal.IsFinite ||
            !(normalLength > 0) || !double.IsFinite(normalLength) ||
            !(directionLength > 0) || !double.IsFinite(directionLength))
            throw new ArgumentException("Ray and plane must be finite and nondegenerate.");
        normal /= normalLength;
        var denominator = Vec3.Dot(Direction, normal);
        if (Math.Abs(denominator) <= 1e-12 * directionLength) return false;
        var t = Vec3.Dot(origin - Origin, normal) / denominator;
        if (!(t >= 0) || !double.IsFinite(t)) return false;
        point = Origin + Direction * t;
        return point.IsFinite;
    }
}

/// <summary>Immutable pinhole mapping with exact ray/unprojection APIs. ModelToTarget includes
/// paper scale and camera twist in XY and unscaled camera depth in Z. It is NOT a projective inverse.</summary>
public sealed record PerspectiveCamera
{
    public Affine3 ModelToTarget { get; }
    public Affine3 TargetToModel { get; }
    public Vec3 Center { get; }
    public double EyeDistance { get; }
    public double NearDistanceFraction { get; }
    public double MaximumDepth => EyeDistance * (1 - NearDistanceFraction);

    public PerspectiveCamera(Affine3 modelToTarget, Vec3 center, double eyeDistance, double nearDistanceFraction = 1.0 / 64)
    {
        if (!center.IsFinite || center.Z != 0 || !(eyeDistance > 0) || !double.IsFinite(eyeDistance) ||
            !(nearDistanceFraction > 0 && nearDistanceFraction < 1) || !double.IsFinite(nearDistanceFraction) ||
            !modelToTarget.TryInverse(out var inverse)) throw new ArgumentException("Invalid projective camera.");
        if (!(eyeDistance * nearDistanceFraction > 0) || !(eyeDistance * (1 - nearDistanceFraction) < eyeDistance))
            throw new ArgumentException("Near distance cannot be represented at this camera scale.");
        ModelToTarget = modelToTarget; TargetToModel = inverse; Center = center;
        EyeDistance = eyeDistance; NearDistanceFraction = nearDistanceFraction;
    }
    public bool TryProject(Vec3 model, out Vec3 paper) => TryProjectTarget(ModelToTarget.Point(model), out paper);
    internal bool TryProjectTarget(Vec3 q, out Vec3 paper)
    {
        paper = default;
        var w = 1 - q.Z / EyeDistance;
        if (!q.IsFinite || !(w > 0) || q.Z > MaximumDepth + 1e-12 * EyeDistance) return false;
        paper = new(Center.X + (q.X - Center.X) / w, Center.Y + (q.Y - Center.Y) / w, q.Z);
        return paper.IsFinite;
    }
    public Vec3 Unproject(Vec3 paper, double cameraDepth)
    {
        if (!paper.IsFinite || !double.IsFinite(cameraDepth) || cameraDepth > MaximumDepth)
            throw new ArgumentOutOfRangeException(nameof(cameraDepth));
        var w = 1 - cameraDepth / EyeDistance;
        var result = TargetToModel.Point(new(Center.X + (paper.X - Center.X) * w,
            Center.Y + (paper.Y - Center.Y) * w, cameraDepth));
        if (!result.IsFinite) throw new ArithmeticException("Perspective unprojection overflow.");
        return result;
    }
    public PickingRay Ray(Vec3 paper)
    {
        if (!paper.IsFinite) throw new ArgumentException("Nonfinite paper point.", nameof(paper));
        var eye = TargetToModel.Point(new(Center.X, Center.Y, EyeDistance));
        var direction = Unproject(paper, 0) - eye;
        if (!eye.IsFinite || !(direction.Length > 0) || !double.IsFinite(direction.Length))
            throw new ArithmeticException("Perspective picking ray exceeds numeric range.");
        return new(eye, direction / direction.Length);
    }
}
