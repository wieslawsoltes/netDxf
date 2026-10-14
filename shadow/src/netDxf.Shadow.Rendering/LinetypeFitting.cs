using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Shared distance-domain fitting for strokes and decorations. Fitting changes only the
/// first/last dash on open paths; closed paths distribute residual length over first dashes.
/// Interior gaps, secondary dashes and glyph dimensions are not stretched.</summary>
internal static class LinetypeFitting
{
    internal readonly record struct Event(int Element, double Distance);
    private readonly record struct Fit(double Period, double Shift, double FirstDelta,
        double EndDash, long Cycles, bool Solid);

    internal static ImmutableArray<DashInterval> Strokes(double length, ImmutableArray<double> pattern,
        CadLinetypeAlignment alignment, bool closed, int maximumSegments)
    {
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (alignment == CadLinetypeAlignment.Periodic)
            return DashPattern.Clip(0, length, pattern, maximumSegments: maximumSegments);
        var fit = Create(length, pattern, closed);
        if (maximumSegments < 1) throw new ArgumentOutOfRangeException(nameof(maximumSegments));
        if (fit.Solid) return [new(0, length)];
        var result = ImmutableArray.CreateBuilder<DashInterval>();
        if (closed)
        {
            var adjusted = pattern.SetItem(0, pattern[0] + fit.FirstDelta);
            foreach (var interval in DashPattern.Clip(0, length, adjusted, fit.Shift, maximumSegments))
                if (!interval.IsDot || interval.Start != length) Add(interval);
        }
        else
        {
            Add(new(0, fit.EndDash));
            // Phase is determined by the full path, before any display/depth clipping.
            foreach (var interval in DashPattern.Clip(fit.EndDash, length - fit.EndDash,
                pattern, fit.Shift, maximumSegments)) Add(interval);
            Add(new(length - fit.EndDash, length));
        }
        return result.ToImmutable();

        void Add(DashInterval interval)
        {
            if (result.Count > 0 && interval.Start <= result[^1].End)
            {
                result[^1] = new(result[^1].Start, Math.Max(result[^1].End, interval.End));
                return;
            }
            if (result.Count >= maximumSegments) throw new InvalidOperationException("Fitted stroke segment budget exceeded.");
            result.Add(interval);
        }
    }

    internal static ImmutableArray<Event> Events(double length, ImmutableArray<double> pattern,
        ImmutableArray<int> elements, CadLinetypeAlignment alignment, bool closed, int maximumPlacements,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (maximumPlacements < 0) throw new ArgumentOutOfRangeException(nameof(maximumPlacements));
        var period = Period(pattern);
        var fit = alignment == CadLinetypeAlignment.A ? Create(length, pattern, closed) :
            new Fit(period, 0, 0, 0, CycleCount(length, period), false);
        if (fit.Solid || elements.IsDefaultOrEmpty) return [];
        var terminal = alignment == CadLinetypeAlignment.A && !closed && elements.Contains(0);
        // This lower bound is checked before allocating or walking a microscopic repeat pattern.
        if ((double)fit.Cycles * elements.Length + (terminal ? 1 : 0) > maximumPlacements)
            throw new InvalidOperationException("Linetype placement budget exceeded.");
        var schedule = new List<Event>(elements.Length);
        var requested = elements.ToHashSet();
        if (requested.Count != elements.Length || requested.Any(i => i < 0 || i >= pattern.Length))
            throw new ArgumentException("Decoration element indices must be unique and within the pattern.", nameof(elements));
        var offset = 0.0;
        for (var i = 0; i < pattern.Length; i++)
        {
            offset += Math.Abs(pattern[i]);
            if (!requested.Contains(i)) continue;
            var at = offset + fit.FirstDelta + fit.Shift;
            if (closed && at >= fit.Period) at -= fit.Period;
            schedule.Add(new(i, at));
        }
        schedule.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Element.CompareTo(b.Element));
        var result = ImmutableArray.CreateBuilder<Event>();
        var end = alignment == CadLinetypeAlignment.A ? fit.Cycles : fit.Cycles + 1;
        for (long cycle = 0; cycle < end; cycle++)
        {
            foreach (var item in schedule)
            {
                token.ThrowIfCancellationRequested();
                var at = cycle * fit.Period + item.Distance;
                if (at > length || closed && at >= length) continue;
                if (result.Count >= maximumPlacements) throw new InvalidOperationException("Linetype placement budget exceeded.");
                result.Add(new(item.Element, at));
            }
        }
        if (terminal)
        {
            if (result.Count >= maximumPlacements) throw new InvalidOperationException("Linetype placement budget exceeded.");
            result.Add(new(0, length));
        }
        return result.ToImmutable();
    }

    private static Fit Create(double length, ImmutableArray<double> pattern, bool closed)
    {
        if (!(length >= 0) || !double.IsFinite(length)) throw new ArgumentOutOfRangeException(nameof(length));
        var period = Period(pattern);
        if (pattern.Length < 2 || pattern[0] < 0)
            throw new ArgumentException("A alignment requires at least two descriptors and a nonnegative first dash.", nameof(pattern));
        var cycles = CycleCount(length, period);
        if (cycles == 0) return new(period, 0, 0, length, 0, true);
        var remainder = Math.FusedMultiplyAdd(-cycles, period, length);
        // Rounded division may choose the next integer just below a repeat boundary.
        if (remainder < 0) { cycles--; remainder += period; }
        if (remainder >= period) { cycles++; remainder -= period; }
        if (cycles == 0) return new(period, 0, 0, length, 0, true);
        if (!(remainder >= 0) || !double.IsFinite(remainder)) throw new ArithmeticException("Fitted linetype remainder is not representable.");
        if (closed)
        {
            var extra = remainder / cycles;
            var first = pattern[0] + extra;
            return new(period + extra, -first / 2, extra, first / 2, cycles, false);
        }
        var endDash = remainder / 2 + pattern[0] / 2;
        return new(period, endDash - pattern[0], 0, endDash, cycles, false);
    }

    private static long CycleCount(double length, double period)
    {
        if (!(length >= 0) || !double.IsFinite(length)) throw new ArgumentOutOfRangeException(nameof(length));
        var count = Math.Floor(length / period);
        if (!double.IsFinite(count) || count > 4_503_599_627_370_496)
            throw new InvalidOperationException("Linetype repetition count exceeds the supported numeric range.");
        return (long)count;
    }

    private static double Period(ImmutableArray<double> pattern)
    {
        if (pattern.IsDefaultOrEmpty) throw new ArgumentException("A fitted linetype needs a nonempty pattern.", nameof(pattern));
        var total = 0.0;
        foreach (var value in pattern)
        {
            var length = Math.Abs(value); var next = total + length;
            if (!double.IsFinite(next) || length > 0 && next <= total)
                throw new ArgumentException("Linetype period exceeds numeric resolution.", nameof(pattern));
            total = next;
        }
        if (!(total > 0)) throw new ArgumentException("A fitted linetype needs a positive period.", nameof(pattern));
        return total;
    }
}
