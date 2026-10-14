using System.Collections.Immutable;

namespace netDxf.Shadow.Rendering;

public enum LinetypeMetricSpace { World, Definition }
public enum LinetypeTextRotationPivot { BaselineOrigin, NominalCapHeightCenter }

/// <summary>Bounded affine path transport preserving authored restart indices.</summary>
internal static class LinetypeCoordinates
{
    internal static ImmutableArray<RenderContour> Transform(ImmutableArray<RenderContour> contours, Affine3 matrix,
        int maximumPoints = 2_000_000, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!matrix.IsFinite || maximumPoints < 1) throw new ArgumentException("Invalid linetype coordinate transform or point budget.");
        if (contours.IsDefaultOrEmpty) return contours;
        long count = 0;
        foreach (var contour in contours)
        {
            token.ThrowIfCancellationRequested();
            count += contour.Points.Length;
            if (count > maximumPoints) throw new InvalidOperationException("Linetype coordinate point budget exceeded.");
        }
        var result = ImmutableArray.CreateBuilder<RenderContour>(contours.Length);
        foreach (var contour in contours)
        {
            var points = ImmutableArray.CreateBuilder<Vec3>(contour.Points.Length);
            foreach (var point in contour.Points)
            {
                token.ThrowIfCancellationRequested();
                var projected = matrix.Point(point);
                if (!point.IsFinite || !projected.IsFinite) throw new ArithmeticException("Linetype transform exceeds the coordinate range.");
                points.Add(projected);
            }
            result.Add(contour with { Points = points.MoveToImmutable() });
        }
        return result.MoveToImmutable();
    }
}
