using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

/// <summary>Analytic interchange curves. No tessellation or backend handles are stored in the shadow DOM.</summary>
public abstract record Curve3
{
    public abstract Vec3 At(double parameter);
    public abstract Curve3 Transform(Affine3 transform);
    public virtual bool Closed => false;
    public Vec3 Start => At(0);
    public Vec3 End => At(1);
}

public sealed record LineCurve(Vec3 A, Vec3 B) : Curve3
{
    public override Vec3 At(double parameter) => A * (1 - parameter) + B * parameter;
    public override Curve3 Transform(Affine3 transform) => new LineCurve(transform.Point(A), transform.Point(B));
}

/// <summary>Also represents circles and affine images of circles, including mirrored/nonuniform inserts.</summary>
public sealed record EllipseCurve(Vec3 Center, Vec3 U, Vec3 V, double StartAngle, double Sweep) : Curve3
{
    public override Vec3 At(double parameter)
    {
        var angle = StartAngle + Sweep * parameter;
        return Center + U * Math.Cos(angle) + V * Math.Sin(angle);
    }
    public override Curve3 Transform(Affine3 transform) => new EllipseCurve(transform.Point(Center), transform.Direction(U), transform.Direction(V), StartAngle, Sweep);
    public override bool Closed => Math.Abs(Math.Abs(Sweep) - Math.Tau) < 1e-10;
}

public sealed record NurbsCurve : Curve3
{
    public ImmutableArray<Vec3> Controls { get; }
    public ImmutableArray<double> Weights { get; }
    public ImmutableArray<double> Knots { get; }
    public int Degree { get; }
    public override bool Closed { get; }
    public double DomainStart => Knots[Degree];
    public double DomainEnd => Knots[Controls.Length];

    public NurbsCurve(ImmutableArray<Vec3> controls, ImmutableArray<double> weights, ImmutableArray<double> knots, int degree, bool closed = false)
    {
        if (degree < 1 || degree > 32 || controls.Length <= degree || knots.Length != controls.Length + degree + 1)
            throw new ArgumentException("Invalid NURBS cardinality or degree (supported range 1..32).");
        if (weights.IsDefaultOrEmpty) weights = Enumerable.Repeat(1.0, controls.Length).ToImmutableArray();
        if (weights.Length != controls.Length || controls.Any(p => !p.IsFinite) || weights.Any(w => !double.IsFinite(w) || w <= 0))
            throw new ArgumentException("NURBS controls must be finite and weights positive and finite.");
        for (var i = 0; i < knots.Length; i++)
            if (!double.IsFinite(knots[i]) || i > 0 && knots[i] < knots[i - 1]) throw new ArgumentException("NURBS knots must be finite and nondecreasing.");
        if (!(knots[controls.Length] > knots[degree])) throw new ArgumentException("Empty NURBS domain.");
        Controls = controls; Weights = weights; Knots = knots; Degree = degree; Closed = closed;
    }

    private readonly record struct Homogeneous(Vec3 Numerator, double Weight)
    {
        public static Homogeneous Blend(Homogeneous a, Homogeneous b, double t) => new(a.Numerator * (1 - t) + b.Numerator * t, a.Weight * (1 - t) + b.Weight * t);
    }

    public override Vec3 At(double parameter)
    {
        if (!double.IsFinite(parameter) || parameter < 0 || parameter > 1) throw new ArgumentOutOfRangeException(nameof(parameter));
        var u = parameter == 0 ? DomainStart : parameter == 1 ? DomainEnd : DomainStart * (1 - parameter) + DomainEnd * parameter;
        var n = Controls.Length - 1;
        var span = n;
        if (u < DomainEnd)
        {
            var low = Degree; var high = n + 1;
            while (low + 1 < high)
            {
                var middle = (low + high) / 2;
                if (Knots[middle] <= u) low = middle; else high = middle;
            }
            span = low;
        }
        Span<Homogeneous> work = stackalloc Homogeneous[Degree + 1];
        var maxWeight = 0.0;
        for (var j = 0; j <= Degree; j++) maxWeight = Math.Max(maxWeight, Weights[span - Degree + j]);
        for (var j = 0; j <= Degree; j++)
        {
            var index = span - Degree + j;
            var weight = Weights[index] / maxWeight;
            work[j] = new(Controls[index] * weight, weight);
        }
        for (var r = 1; r <= Degree; r++)
            for (var j = Degree; j >= r; j--)
            {
                var index = span - Degree + j;
                var denominator = Knots[index + Degree - r + 1] - Knots[index];
                if (!double.IsFinite(denominator)) throw new ArithmeticException("NURBS knot span exceeds floating-point range.");
                var alpha = denominator == 0 ? 0 : (u - Knots[index]) / denominator;
                work[j] = Homogeneous.Blend(work[j - 1], work[j], alpha);
            }
        var value = work[Degree];
        var result = value.Numerator / value.Weight;
        if (value.Weight == 0 || !result.IsFinite) throw new ArithmeticException("NURBS sample is not representable.");
        return result;
    }

    public override Curve3 Transform(Affine3 transform) => new NurbsCurve(Controls.Select(transform.Point).ToImmutableArray(), Weights, Knots, Degree, Closed);
}

public sealed record SamplingOptions(double Tolerance = 0.05, int MaximumPoints = 262_144, int MaximumDepth = 20);

public static class CurveSampler
{
    public static ImmutableArray<Vec3> Sample(Curve3 curve, SamplingOptions? options = null)
    {
        options ??= new();
        if (!double.IsFinite(options.Tolerance) || options.Tolerance <= 0 || options.MaximumPoints < 2 || options.MaximumDepth is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (curve is LineCurve line) return [line.A, line.B];
        var points = ImmutableArray.CreateBuilder<Vec3>();
        void Add(Vec3 point)
        {
            if (!point.IsFinite) throw new ArithmeticException("Non-finite geometry point.");
            if (points.Count >= options.MaximumPoints) throw new InvalidOperationException("Curve sampling budget exceeded.");
            points.Add(point);
        }
        if (curve is EllipseCurve ellipse)
        {
            // ||U|| + ||V|| bounds the second derivative even after shear.
            var radiusBound = ellipse.U.Length + ellipse.V.Length;
            if (!double.IsFinite(radiusBound)) throw new ArithmeticException("Ellipse extent exceeds floating-point range.");
            var step = radiusBound <= options.Tolerance ? Math.PI / 2 : 2 * Math.Acos(Math.Clamp(1 - options.Tolerance / radiusBound, -1, 1));
            var countValue = Math.Ceiling(Math.Abs(ellipse.Sweep) / Math.Max(step, 1e-12));
            if (countValue >= options.MaximumPoints) throw new InvalidOperationException("Ellipse tolerance exceeds the sampling budget.");
            var count = Math.Max(2, (int)countValue);
            for (var i = 0; i <= count; i++) Add(ellipse.At((double)i / count));
            return points.ToImmutable();
        }
        var cuts = new SortedSet<double> { 0, 1 };
        if (curve is NurbsCurve nurbs)
        {
            foreach (var knot in nurbs.Knots)
                if (knot > nurbs.DomainStart && knot < nurbs.DomainEnd) cuts.Add((knot - nurbs.DomainStart) / (nurbs.DomainEnd - nurbs.DomainStart));
        }
        for (var i = 1; i < 16; i++) cuts.Add(i / 16.0);
        Add(curve.Start);
        var previous = 0.0;
        foreach (var end in cuts.Skip(1)) { Subdivide(previous, end, curve.At(previous), curve.At(end), 0); previous = end; }
        return points.ToImmutable();

        void Subdivide(double a, double b, Vec3 pa, Vec3 pb, int depth)
        {
            var middle = (a + b) / 2;
            var pm = curve.At(middle);
            var error = Math.Max(DistanceToSegment(pm, pa, pb), Math.Max(DistanceToSegment(curve.At(a * 0.75 + b * 0.25), pa, pb), DistanceToSegment(curve.At(a * 0.25 + b * 0.75), pa, pb)));
            if (error <= options.Tolerance) { Add(pb); return; }
            if (depth >= options.MaximumDepth) throw new InvalidOperationException("Adaptive curve tolerance was not reached within the depth budget.");
            Subdivide(a, middle, pa, pm, depth + 1);
            Subdivide(middle, b, pm, pb, depth + 1);
        }
    }

    public static double DistanceToSegment(Vec3 point, Vec3 a, Vec3 b)
    {
        var delta = b - a;
        var length2 = Vec3.Dot(delta, delta);
        return Vec3.Distance(point, length2 == 0 ? a : a + delta * Math.Clamp(Vec3.Dot(point - a, delta) / length2, 0, 1));
    }
}
