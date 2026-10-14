using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace netDxf.Shadow.Rendering;

/// <summary>Shares authored-boundary partitioning between stroke and symbol planning without
/// changing the original contour used for semantic picking and snapping.</summary>
internal static class LinetypePaths
{
    private sealed record Entry(ImmutableArray<RenderContour> Paths);
    private static readonly ConditionalWeakTable<RenderContour, Entry> cache = new();

    internal static ImmutableArray<RenderContour> Split(ImmutableArray<RenderContour> contours,
        int maximumPoints = 4_000_000, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumPoints < 1) throw new ArgumentOutOfRangeException(nameof(maximumPoints));
        if (contours.IsDefaultOrEmpty) return [];
        if (contours.Length > maximumPoints) throw new InvalidOperationException("Linetype path budget exceeded.");
        long points = 0;
        var partitioned = false;
        // Validate every boundary and the complete allocation budget before copying any points.
        foreach (var contour in contours)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(contour);
            var length = contour.Points.IsDefault ? 0 : contour.Points.Length;
            points += length;
            if (length > 1 && contour.Closed && contour.Points[0] != contour.Points[^1]) points++;
            if (!contour.PatternBreaks.IsDefaultOrEmpty)
            {
                partitioned = true;
                var previous = 0;
                foreach (var boundary in contour.PatternBreaks)
                {
                    if (boundary <= previous || boundary >= length - 1)
                        throw new ArgumentException("Pattern breaks must be strictly increasing interior point indices.", nameof(contours));
                    previous = boundary;
                }
                points += contour.PatternBreaks.Length; // Adjacent paths share a copied endpoint.
            }
            if (points > maximumPoints) throw new InvalidOperationException("Linetype point budget exceeded.");
        }
        if (!partitioned) return contours;
        var output = ImmutableArray.CreateBuilder<RenderContour>();
        foreach (var contour in contours)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (contour.PatternBreaks.IsDefaultOrEmpty) output.Add(contour);
            else output.AddRange(cache.GetValue(contour, static source => new(Partition(source))).Paths);
        }
        return output.ToImmutable();
    }

    private static ImmutableArray<RenderContour> Partition(RenderContour source)
    {
        var result = ImmutableArray.CreateBuilder<RenderContour>(source.PatternBreaks.Length + 1);
        var start = 0;
        foreach (var boundary in source.PatternBreaks)
        {
            result.Add(new(ImmutableArray.Create(source.Points.AsSpan(start, boundary - start + 1)), false));
            start = boundary;
        }
        var tail = ImmutableArray.Create(source.Points.AsSpan(start, source.Points.Length - start));
        if (source.Closed && tail[^1] != source.Points[0]) tail = tail.Add(source.Points[0]);
        result.Add(new(tail, false));
        return result.MoveToImmutable();
    }
}
