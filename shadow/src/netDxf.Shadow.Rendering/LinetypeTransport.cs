using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public enum LinetypeFrameMode { AuthoredPlane, ParallelTransport }

/// <summary>Orthonormal rotation-minimizing frames over an immutable sampled path.
/// At exact cusps, a half turn around the retained side axis resolves the ambiguous rotation.
/// Closed paths optionally distribute residual roll by arc length to make transported seam frames agree.</summary>
public sealed class LinetypeTransport
{
    private readonly ImmutableArray<Vec3> tangents, sides;
    private readonly double length, seamRoll;
    private LinetypeTransport(ImmutableArray<Vec3> tangents, ImmutableArray<Vec3> sides, double length, double seamRoll)
    { this.tangents = tangents; this.sides = sides; this.length = length; this.seamRoll = seamRoll; }

    public static LinetypeTransport Create(ImmutableArray<Vec3> points, Vec3 referenceNormal,
        bool closed = false, bool closeFrameSeam = true, int maximumPoints = 2_000_000, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumPoints < 2 || points.IsDefault || points.Length < 2 || points.Length > maximumPoints)
            throw new ArgumentException("Transport requires a bounded path containing at least two points.", nameof(points));
        if (!referenceNormal.IsFinite || !(referenceNormal.Length > 0)) throw new ArgumentException("A finite nonzero seed normal is required.");
        if (closed && points[0] != points[^1])
        {
            if (points.Length >= maximumPoints) throw new InvalidOperationException("Transport closure exceeds the point budget.");
            points = points.Add(points[0]);
        }
        var directions = new Vec3[points.Length - 1]; var side = new Vec3[directions.Length];
        var total = 0.0; var first = -1; var previous = -1;
        for (var i = 0; i < directions.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!points[i].IsFinite || !points[i + 1].IsFinite) throw new ArgumentException("Nonfinite transport path.");
            var delta = points[i + 1] - points[i]; var distance = delta.Length;
            if (!double.IsFinite(distance)) throw new ArgumentException("Transport path exceeds the coordinate range.");
            if (distance == 0) continue;
            var next = total + distance;
            if (!double.IsFinite(next) || !(next > total)) throw new ArgumentException("Transport distances exceed numeric resolution.");
            total = next;
            var tangent = delta / distance; directions[i] = tangent;
            if (first < 0) { first = i; side[i] = Seed(tangent, referenceNormal); }
            else side[i] = Rotate(sides: side[previous], from: directions[previous], to: tangent);
            previous = i;
        }
        if (first < 0) throw new ArgumentException("A zero-length path has no transport tangent.");
        // Degenerate edges inherit a neighbor's frame, but consume no length or rotation.
        for (var i = first - 1; i >= 0; i--) { directions[i] = directions[first]; side[i] = side[first]; }
        for (var i = first + 1; i < directions.Length; i++)
            if (directions[i] == Vec3.Zero) { directions[i] = directions[i - 1]; side[i] = side[i - 1]; }
        var residual = 0.0;
        if (closed && closeFrameSeam)
        {
            var seam = Rotate(side[previous], directions[previous], directions[first]);
            residual = Math.Atan2(Vec3.Dot(directions[first], Vec3.Cross(seam, side[first])), Math.Clamp(Vec3.Dot(seam, side[first]), -1, 1));
        }
        return new(directions.ToImmutableArray(), side.ToImmutableArray(), total, residual);
    }

    public Affine3 At(int edge, double distance, Vec3 origin)
    {
        if ((uint)edge >= (uint)tangents.Length || !double.IsFinite(distance) || distance < 0 || distance > length || !origin.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(edge), "Invalid frame query.");
        var x = tangents[edge]; var y = sides[edge]; var z = Vec3.Cross(x, y);
        var angle = seamRoll * (distance / length);
        y = Unit(y * Math.Cos(angle) + z * Math.Sin(angle));
        return new(x, y, Unit(Vec3.Cross(x, y)), origin);
    }

    private static Vec3 Seed(Vec3 tangent, Vec3 normal)
    {
        normal = Unit(normal);
        var side = Vec3.Cross(normal, tangent);
        if (side.Length < 1e-10)
        {
            // Least-aligned Cartesian axis maximizes the seed projection's conditioning.
            var axis = Math.Abs(tangent.X) <= Math.Abs(tangent.Y) && Math.Abs(tangent.X) <= Math.Abs(tangent.Z)
                ? Vec3.UnitX : Math.Abs(tangent.Y) <= Math.Abs(tangent.Z) ? Vec3.UnitY : Vec3.UnitZ;
            side = Vec3.Cross(axis, tangent);
        }
        return Unit(side);
    }
    private static Vec3 Rotate(Vec3 sides, Vec3 from, Vec3 to)
    {
        var c = Math.Clamp(Vec3.Dot(from, to), -1, 1);
        Vec3 result;
        if (c < -1 + 1e-12) result = sides; // Deterministic cusp policy; the side axis is the half-turn axis.
        else
        {
            var axis = Vec3.Cross(from, to);
            result = sides + Vec3.Cross(axis, sides) + Vec3.Cross(axis, Vec3.Cross(axis, sides)) / (1 + c);
        }
        return Unit(result - to * Vec3.Dot(result, to));
    }
    private static Vec3 Unit(Vec3 vector)
    {
        var length = vector.Length;
        if (!(length > 0) || !double.IsFinite(length)) throw new ArgumentException("Transport frame is singular or outside the numeric range.");
        return vector / length;
    }
}
