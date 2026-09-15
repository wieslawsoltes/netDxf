using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Hatch
    {
        private static double AffineHypot(double x, double y)
        {
            double largest = Math.Max(Math.Abs(x), Math.Abs(y));
            RequireAffineFinite(largest);
            if (largest == 0) return 0;
            x /= largest; y /= largest;
            return largest * Math.Sqrt(x * x + y * y);
        }
        private static double AffineCross(Vector2 a, Vector2 b)
        { return a.X * b.Y - a.Y * b.X; }
        private static double AffineAngle(double angle)
        {
            angle %= 360;
            return angle < 0 ? angle + 360 : angle;
        }
        private static Vector2 AffinePolarDirection(double angle)
        {
            angle = AffineAngle(angle);
            if (angle == 0) return Vector2.UnitX;
            if (angle == 90) return Vector2.UnitY;
            if (angle == 180) return -Vector2.UnitX;
            if (angle == 270) return -Vector2.UnitY;
            angle *= MathHelper.DegToRad;
            return new Vector2(Math.Cos(angle), Math.Sin(angle));
        }
        private static void AffineConicInterval(double originalStart, double originalEnd, ref double start, ref double end)
        {
            double span = originalEnd - originalStart;
            RequireAffineFinite(span);
            if (Math.Abs(span) > 360) throw new NotSupportedException("HATCH conic transforms support a single stored turn.");
            if (span == 0) end = start;
            else if (Math.Abs(span) == 360)
            {
                // Both endpoints must use the larger endpoint's precision so a
                // later transform still reads an exact stored full turn.
                end = start + 360; start = end - 360;
            }
            else if (start == end) throw new ArgumentException("The transformed HATCH conic interval cannot be represented.");
        }
        private static HatchBoundaryPath.Edge TransformAffineArc(HatchBoundaryPath.Arc arc, Func<Vector2, bool, Vector2> map, bool similarity)
        {
            if (arc.Radius <= 0) throw new ArgumentException("A transformed HATCH arc requires a positive radius.");
            if (!similarity)
                return TransformAffineConic(arc.Center, new Vector2(arc.Radius, 0), 1, arc.StartAngle, arc.EndAngle, arc.IsCounterclockwise, map);
            Vector2 x = map(new Vector2(arc.Radius, 0), true), y = map(new Vector2(0, arc.Radius), true);
            double radius = AffineHypot(x.X, x.Y); RequireAffineFinite(radius);
            if (radius == 0) throw new ArgumentException("The transformed HATCH arc collapses.");
            bool ccw = arc.IsCounterclockwise == (AffineCross(x / radius, y / radius) > 0);
            Func<double, double> angle = value =>
            {
                Vector2 direction = AffinePolarDirection(arc.IsCounterclockwise ? value : -value);
                Vector2 point = x * direction.X + y * direction.Y;
                double angleValue = Math.Atan2(point.Y, point.X) * MathHelper.RadToDeg;
                return AffineAngle(ccw ? angleValue : -angleValue);
            };
            double start = angle(arc.StartAngle), end = angle(arc.EndAngle);
            AffineConicInterval(arc.StartAngle, arc.EndAngle, ref start, ref end);
            var result = new HatchBoundaryPath.Arc { Center = map(arc.Center, false), Radius = radius, StartAngle = start, EndAngle = end, IsCounterclockwise = ccw };
            ValidateAffineConicEndpoints(new HatchBoundaryPath.Ellipse { Center = result.Center, EndMajorAxis = new Vector2(radius, 0),
                MinorRatio = 1, StartAngle = start, EndAngle = end, IsCounterclockwise = ccw },
                map(AffineConicPoint(arc.Center, new Vector2(arc.Radius, 0), 1, arc.StartAngle, arc.IsCounterclockwise), false),
                map(AffineConicPoint(arc.Center, new Vector2(arc.Radius, 0), 1, arc.EndAngle, arc.IsCounterclockwise), false));
            return result;
        }
        private static HatchBoundaryPath.Ellipse TransformAffineConic(Vector2 center, Vector2 majorAxis, double ratio,
            double originalStart, double originalEnd, bool originalCcw, Func<Vector2, bool, Vector2> map)
        {
            if (ratio <= 0 || majorAxis.X == 0 && majorAxis.Y == 0)
                throw new ArgumentException("A transformed HATCH ellipse requires nonzero axes and a positive axis ratio.");
            Vector2 u = map(majorAxis, true), v = map(new Vector2(-majorAxis.Y * ratio, majorAxis.X * ratio), true);
            double largest = Math.Max(Math.Max(Math.Abs(u.X), Math.Abs(u.Y)), Math.Max(Math.Abs(v.X), Math.Abs(v.Y)));
            RequireAffineFinite(largest);
            if (largest == 0) throw new ArgumentException("The transformed HATCH conic collapses.");
            Vector2 a = u / largest, b = v / largest;
            double xx = a.X * a.X + b.X * b.X, xy = a.X * a.Y + b.X * b.Y, yy = a.Y * a.Y + b.Y * b.Y;
            double eigenvalue = 0.5 * (xx + yy + AffineHypot(xx - yy, 2 * xy));
            double major = Math.Sqrt(eigenvalue), determinant = AffineCross(a, b), minor = Math.Abs(determinant) / major;
            if (minor == 0) throw new ArgumentException("The transformed HATCH conic has an unrepresentable minor axis.");
            Vector2 direction;
            if (xy == 0)
                direction = xx == yy ? a / AffineHypot(a.X, a.Y) : xx > yy ? Vector2.UnitX : Vector2.UnitY;
            else
            {
                Vector2 first = new Vector2(eigenvalue - yy, xy), second = new Vector2(xy, eigenvalue - xx);
                direction = AffineHypot(first.X, first.Y) >= AffineHypot(second.X, second.Y) ? first : second;
                direction /= AffineHypot(direction.X, direction.Y);
            }
            if (Vector2.DotProduct(direction, a) < 0) direction = -direction;
            Vector2 perpendicular = new Vector2(-direction.Y, direction.X);
            double outputRatio = minor / major, length = major * largest;
            RequireAffineFinite(outputRatio); RequireAffineFinite(length);
            if (outputRatio <= 0 || length <= 0) throw new ArgumentException("The transformed HATCH ellipse cannot be represented.");
            bool ccw = originalCcw == (determinant > 0);
            Func<double, double> angle = value =>
            {
                Vector2 polar = AffinePolarDirection(originalCcw ? value : -value);
                // tan(parameter) = tan(polar angle) / ratio. Computing the unit
                // parameter vector directly avoids unstable trigonometric inverses.
                Vector2 parameter = new Vector2(ratio * polar.X, polar.Y);
                parameter /= AffineHypot(parameter.X, parameter.Y);
                Vector2 point = a * parameter.X + b * parameter.Y;
                double angleValue = Math.Atan2(Vector2.DotProduct(point, perpendicular), Vector2.DotProduct(point, direction)) * MathHelper.RadToDeg;
                return AffineAngle(ccw ? angleValue : -angleValue);
            };
            double start = angle(originalStart), end = angle(originalEnd);
            AffineConicInterval(originalStart, originalEnd, ref start, ref end);
            var result = new HatchBoundaryPath.Ellipse { Center = map(center, false), EndMajorAxis = direction * length,
                MinorRatio = Math.Min(1, outputRatio), StartAngle = start, EndAngle = end, IsCounterclockwise = ccw };
            ValidateAffineConicEndpoints(result, map(AffineConicPoint(center, majorAxis, ratio, originalStart, originalCcw), false),
                map(AffineConicPoint(center, majorAxis, ratio, originalEnd, originalCcw), false));
            return result;
        }
        private static Vector2 AffineConicPoint(Vector2 center, Vector2 major, double ratio, double angle, bool ccw)
        {
            Vector2 polar = AffinePolarDirection(ccw ? angle : -angle);
            Vector2 parameter = new Vector2(ratio * polar.X, polar.Y);
            parameter /= AffineHypot(parameter.X, parameter.Y);
            return center + major * parameter.X + new Vector2(-major.Y, major.X) * (ratio * parameter.Y);
        }
        private static void ValidateAffineConicEndpoints(HatchBoundaryPath.Ellipse ellipse, Vector2 start, Vector2 end)
        {
            RequireAffineFinite(start); RequireAffineFinite(end);
            double scale = Math.Max(1, Math.Max(Math.Max(Math.Abs(start.X), Math.Abs(start.Y)), Math.Max(Math.Abs(end.X), Math.Abs(end.Y))));
            foreach (bool first in new[] { true, false })
            {
                Vector2 expected = first ? start : end;
                Vector2 actual = AffineConicPoint(ellipse.Center, ellipse.EndMajorAxis, ellipse.MinorRatio,
                    first ? ellipse.StartAngle : ellipse.EndAngle, ellipse.IsCounterclockwise);
                RequireAffineFinite(actual);
                if (Math.Abs(actual.X - expected.X) > 1e-10 * scale || Math.Abs(actual.Y - expected.Y) > 1e-10 * scale)
                    throw new ArgumentException("The transformed HATCH conic cannot preserve its endpoints within relative tolerance 1e-10.");
            }
        }
        private static void TransformAffinePolyline(HatchBoundaryPath.Polyline polyline, Func<Vector2, bool, Vector2> map,
            bool similarity, bool reverses, List<HatchBoundaryPath.Edge> output)
        {
            int segments = polyline.IsClosed ? polyline.Vertexes.Length : Math.Max(0, polyline.Vertexes.Length - 1);
            bool curved = false;
            for (int i = 0; i < segments; i++) curved |= polyline.Vertexes[i].Z != 0;
            if (similarity || !curved)
            {
                var copy = new HatchBoundaryPath.Polyline { IsClosed = polyline.IsClosed, Vertexes = new Vector3[polyline.Vertexes.Length] };
                for (int i = 0; i < copy.Vertexes.Length; i++)
                {
                    Vector3 value = polyline.Vertexes[i]; Vector2 point = map(new Vector2(value.X, value.Y), false);
                    double bulge = i < segments && value.Z != 0 && reverses ? -value.Z : value.Z;
                    copy.Vertexes[i] = new Vector3(point.X, point.Y, bulge);
                }
                output.Add(copy); return;
            }
            if (!polyline.IsClosed && polyline.Vertexes[polyline.Vertexes.Length - 1].Z != 0)
                throw new NotSupportedException("Converting an open HATCH polyline would discard its unused terminal bulge.");
            for (int i = 0; i < segments; i++)
            {
                Vector3 first = polyline.Vertexes[i], second = polyline.Vertexes[(i + 1) % polyline.Vertexes.Length];
                Vector2 start = new Vector2(first.X, first.Y), end = new Vector2(second.X, second.Y);
                double bulge = first.Z;
                if (bulge == 0) { output.Add(new HatchBoundaryPath.Line { Start = map(start, false), End = map(end, false) }); continue; }
                Vector2 chord = end - start; double length = AffineHypot(chord.X, chord.Y);
                if (length == 0) throw new ArgumentException("A nonzero HATCH bulge requires distinct segment endpoints.");
                Vector2 left = new Vector2(-chord.Y / length, chord.X / length);
                double distance = (length / 4) * (1 / bulge - bulge), radius = (length / 4) * (Math.Abs(bulge) + 1 / Math.Abs(bulge));
                RequireAffineFinite(distance); RequireAffineFinite(radius);
                Vector2 center = start + chord / 2 + left * distance; RequireAffineFinite(center);
                double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X) * MathHelper.RadToDeg;
                double endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X) * MathHelper.RadToDeg;
                bool ccw = bulge > 0;
                var result = TransformAffineConic(center, new Vector2(radius, 0), 1,
                    AffineAngle(ccw ? startAngle : -startAngle), AffineAngle(ccw ? endAngle : -endAngle), ccw, map);
                // The original vertices define the endpoints. A huge derived center
                // must not enlarge the tolerance and hide cancellation at those vertices.
                ValidateAffineConicEndpoints(result, map(start, false), map(end, false));
                output.Add(result);
            }
        }
    }
}
