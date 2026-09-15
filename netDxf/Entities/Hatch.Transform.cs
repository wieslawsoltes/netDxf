using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Hatch
    {
        /// <summary>Transforms stored boundary geometry and the hatch plane.</summary>
        /// <param name="transformation">Linear transformation, using column vectors.</param>
        /// <param name="translation">Translation in world coordinates.</param>
        /// <remarks>
        /// Line, polyline, conic and spline boundaries in solid fills support
        /// finite affine transformations that preserve a two-dimensional plane. Spline
        /// knots, weights, rationality, periodicity and optional fit metadata retain
        /// their stored meanings; this operation does not evaluate or refit a curve.
        /// Circular boundaries become ellipse edges when required. Conic results whose
        /// stored angles cannot preserve their endpoints within relative tolerance
        /// 1e-10 are rejected. Explicit predefined/custom pattern line families support
        /// affine changes when their stored WCS Point2d origin remains representable.
        /// Nonuniform user-defined, doubled and gradient patterns are rejected.
        /// A successful transform unlinks associative
        /// sources without transforming or removing those source entities. Validation
        /// completes before unlinking or changing stored state.
        /// </remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    RequireAffineFinite(transformation[row, column]);
            RequireAffineFinite(translation);
            RequireAffineFinite(this.Elevation);
            RequireAffineFinite(this.Pattern.Angle);
            RequireAffineFinite(this.Pattern.Scale);
            ValidateAffinePathData(this.BoundaryPaths);

            Matrix3 oldOcs = MathHelper.ArbitraryAxis(this.Normal);
            Vector3 xAxis = transformation * (oldOcs * Vector3.UnitX);
            Vector3 yAxis = transformation * (oldOcs * Vector3.UnitY);
            RequireAffineFinite(xAxis); RequireAffineFinite(yAxis);
            Vector3 xUnit = AffineUnit(xAxis), yUnit = AffineUnit(yAxis);
            Vector3 normal = AffineUnit(Vector3.CrossProduct(xUnit, yUnit));
            Vector3 normalHint = transformation * this.Normal;
            RequireAffineFinite(normalHint);
            // Either sign describes the plane. Keep the historical orientation where
            // its transformed normal has a nonzero component perpendicular to it.
            if (Vector3.DotProduct(normal, normalHint) < 0) normal = -normal;
            Matrix3 newOcs = MathHelper.ArbitraryAxis(normal).Transpose();
            double xLength = xAxis.Modulus(), yLength = yAxis.Modulus();
            RequireAffineFinite(xLength); RequireAffineFinite(yLength);
            bool similarity = Math.Abs(Vector3.DotProduct(xUnit, yUnit)) <= 1e-12
                && Math.Abs(xLength - yLength) <= 1e-12 * Math.Max(xLength, yLength);
            bool explicitPattern = this.HasExplicitAffinePattern();
            if (!similarity && (this.Pattern is HatchGradientPattern
                || this.Pattern.Fill != HatchFillType.SolidFill && (!explicitPattern || this.Pattern.IsDouble)))
                throw new NotSupportedException("Nonuniform HATCH transforms require a solid fill or an explicit, non-doubled predefined/custom pattern.");

            Vector3 position = newOcs * (transformation * (oldOcs * new Vector3(0, 0, this.Elevation)) + translation);
            RequireAffineFinite(position);
            Func<Vector2, bool, Vector2> map = (value, vector) =>
            {
                RequireAffineFinite(value.X); RequireAffineFinite(value.Y);
                Vector3 point = newOcs * (transformation * (oldOcs * new Vector3(value.X, value.Y, vector ? 0 : this.Elevation)) + (vector ? Vector3.Zero : translation));
                RequireAffineFinite(point);
                return new Vector2(point.X, point.Y);
            };
            var paths = new List<HatchBoundaryPath>();
            foreach (HatchBoundaryPath path in this.BoundaryPaths)
            {
                var edges = new List<HatchBoundaryPath.Edge>();
                foreach (HatchBoundaryPath.Edge edge in path.Edges)
                {
                    if (edge is HatchBoundaryPath.Line line)
                        edges.Add(new HatchBoundaryPath.Line { Start = map(line.Start, false), End = map(line.End, false) });
                    else if (edge is HatchBoundaryPath.Spline spline)
                    {
                        edges.Add(TransformAffineSpline(spline, map));
                    }
                    else if (edge is HatchBoundaryPath.Arc arc)
                        edges.Add(TransformAffineArc(arc, map, similarity));
                    else if (edge is HatchBoundaryPath.Ellipse ellipse)
                        edges.Add(TransformAffineConic(ellipse.Center, ellipse.EndMajorAxis, ellipse.MinorRatio, ellipse.StartAngle, ellipse.EndAngle, ellipse.IsCounterclockwise, map));
                    else
                        TransformAffinePolyline((HatchBoundaryPath.Polyline)edge, map, similarity,
                            AffineCross(map(Vector2.UnitX, true), map(Vector2.UnitY, true)) < 0, edges);
                }
                var transformedPath = new HatchBoundaryPath(edges);
                transformedPath.PathType = (path.PathType & ~HatchBoundaryPathTypeFlags.Polyline)
                    | (transformedPath.PathType & HatchBoundaryPathTypeFlags.Polyline);
                paths.Add(transformedPath);
            }
            ValidateAffinePathData(paths);
            var seeds = new List<Vector2>();
            foreach (Vector2 seed in this.seedPoints) seeds.Add(map(seed, false));
            Vector2 direction = this.Pattern.Scale * Vector2.Rotate(Vector2.UnitX, this.Pattern.Angle * MathHelper.DegToRad);
            Vector2 axis = map(direction, true);
            double scale = axis.Modulus(), angle = Vector2.Angle(axis) * MathHelper.RadToDeg;
            RequireAffineFinite(scale); RequireAffineFinite(angle);
            if (scale <= 0) throw new ArgumentException("The HATCH pattern direction collapses under this transformation.");

            bool identity = translation.X == 0 && translation.Y == 0 && translation.Z == 0;
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    identity &= transformation[row, column] == (row == column ? 1 : 0);
            HatchPattern transformedPattern = explicitPattern
                ? this.TransformAffinePattern(transformation, translation, map, scale, angle, identity) : null;
            // Everything above is temporary. Unlink only after validation succeeds.
            if (this.associative) this.UnLinkBoundary();
            if (identity) return;
            if (transformedPattern != null) this.pattern = transformedPattern;
            else { this.Pattern.Scale = scale; this.Pattern.Angle = angle; }
            this.Elevation = position.Z; this.Normal = normal;
            this.BoundaryPaths.Clear(); this.BoundaryPaths.AddRange(paths);
            this.seedPoints.Clear(); foreach (Vector2 seed in seeds) this.seedPoints.Add(seed);
        }

        private static HatchBoundaryPath.Spline TransformAffineSpline(HatchBoundaryPath.Spline spline, Func<Vector2, bool, Vector2> map)
        {
                        HatchSplineData.Validate(spline);
                        var copy = new HatchBoundaryPath.Spline
                        {
                            Degree = spline.Degree, IsRational = spline.IsRational, IsPeriodic = spline.IsPeriodic,
                            Knots = (double[])spline.Knots.Clone(), ControlPoints = (Vector3[])spline.ControlPoints.Clone(),
                            StartTangent = spline.StartTangent, EndTangent = spline.EndTangent
                        };
                        foreach (Vector2 fit in spline.FitPoints) copy.FitPoints.Add(fit);
                        for (int i = 0; i < copy.ControlPoints.Length; i++)
                        {
                            Vector3 value = copy.ControlPoints[i]; Vector2 point = map(new Vector2(value.X, value.Y), false);
                            copy.ControlPoints[i] = new Vector3(point.X, point.Y, value.Z);
                        }
                        for (int i = 0; i < copy.FitPoints.Count; i++) copy.FitPoints[i] = map(copy.FitPoints[i], false);
                        if (copy.StartTangent.HasValue) copy.StartTangent = map(copy.StartTangent.Value, true);
                        if (copy.EndTangent.HasValue) copy.EndTangent = map(copy.EndTangent.Value, true);
                        return copy;
        }

        private static void ValidateAffinePathData(IEnumerable<HatchBoundaryPath> paths)
        {
            foreach (HatchBoundaryPath path in paths)
                foreach (HatchBoundaryPath.Edge edge in path.Edges)
                {
                    if (edge is HatchBoundaryPath.Line line)
                    { RequireAffineFinite(line.Start); RequireAffineFinite(line.End); }
                    else if (edge is HatchBoundaryPath.Spline spline)
                    {
                        HatchSplineData.Validate(spline);
                        foreach (Vector2 fit in spline.FitPoints) RequireAffineFinite(fit);
                        if (spline.StartTangent.HasValue) RequireAffineFinite(spline.StartTangent.Value);
                        if (spline.EndTangent.HasValue) RequireAffineFinite(spline.EndTangent.Value);
                    }
                    else if (edge is HatchBoundaryPath.Polyline polyline)
                    {
                        if (polyline.Vertexes == null) throw new ArgumentException("HATCH polyline vertices must be supplied.");
                        foreach (Vector3 vertex in polyline.Vertexes) RequireAffineFinite(vertex);
                    }
                    else if (edge is HatchBoundaryPath.Arc arc)
                    { RequireAffineFinite(arc.Center); RequireAffineFinite(arc.Radius); RequireAffineFinite(arc.StartAngle); RequireAffineFinite(arc.EndAngle); }
                    else if (edge is HatchBoundaryPath.Ellipse ellipse)
                    { RequireAffineFinite(ellipse.Center); RequireAffineFinite(ellipse.EndMajorAxis); RequireAffineFinite(ellipse.MinorRatio); RequireAffineFinite(ellipse.StartAngle); RequireAffineFinite(ellipse.EndAngle); }
                    else throw new ArgumentException("Unsupported or absent HATCH boundary edge.");
                }
        }

        private static Vector3 AffineUnit(Vector3 value)
        {
            double largest = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
            if (largest == 0) throw new ArgumentException("The HATCH plane collapses under this transformation.");
            RequireAffineFinite(largest);
            value /= largest;
            return value / value.Modulus();
        }
        private static void RequireAffineFinite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("HATCH transforms require finite input and representable results.");
        }
        private static void RequireAffineFinite(Vector2 value)
        { RequireAffineFinite(value.X); RequireAffineFinite(value.Y); }
        private static void RequireAffineFinite(Vector3 value)
        { RequireAffineFinite(value.X); RequireAffineFinite(value.Y); RequireAffineFinite(value.Z); }
    }
}
