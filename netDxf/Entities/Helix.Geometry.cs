#region netDxf library licensed under the MIT License
// 
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
#endregion

using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Entities
{
    public sealed partial class Helix
    {
        /// <summary>Constructs a cylindrical/tapered helix or planar spiral with an error-bounded cubic Hermite spline.</summary>
        /// <param name="axisBasePoint">World-coordinate center at the start of the axis.</param>
        /// <param name="startPoint">World-coordinate start, in the plane perpendicular to the axis through its base.</param>
        /// <param name="axisVector">Finite nonzero world-coordinate axis; its original magnitude is retained.</param>
        /// <param name="radius">Nonnegative terminal radius. Initial radius comes from the start point.</param>
        /// <param name="turns">Positive number of turns, including fractional turns.</param>
        /// <param name="turnHeight">Signed height per turn; zero creates a planar spiral.</param>
        /// <param name="isRightHanded">True for right-handed angular progression.</param>
        /// <param name="tolerance">Positive absolute world-distance bound on the cubic approximation, excluding floating-point error.</param>
        /// <param name="maximumSegments">Allocation/work bound, from 1 through 1,048,576 cubic segments.</param>
        /// <returns>A new unowned HELIX whose stored spline approximates its analytic definition.</returns>
        public static Helix Create(Vector3 axisBasePoint, Vector3 startPoint, Vector3 axisVector,
            double radius, double turns, double turnHeight, bool isRightHanded = true,
            double tolerance = 1.0e-5, int maximumSegments = 65536)
        {
            return CreateCore(axisBasePoint, startPoint, axisVector, radius, turns, turnHeight,
                isRightHanded, tolerance, maximumSegments, null);
        }

        private static Helix CreateCore(Vector3 axisBasePoint, Vector3 startPoint, Vector3 axisVector,
            double radius, double turns, double turnHeight, bool isRightHanded,
            double tolerance, int maximumSegments, Vector3? phaseHint)
        {
            ValidateFinite(radius, nameof(radius)); ValidateFinite(turns, nameof(turns)); ValidateFinite(turnHeight, nameof(turnHeight));
            if (radius < 0.0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (turns <= 0.0) throw new ArgumentOutOfRangeException(nameof(turns));
            DefinitionFrame frame = GetDefinitionFrame(axisBasePoint, startPoint, axisVector);
            if (frame.Radius == 0.0 && radius != 0.0 && phaseHint.HasValue) ApplyPhaseHint(ref frame, phaseHint.Value);
            double angle = (isRightHanded ? 2.0 : -2.0) * Math.PI * turns;
            double height = turns * turnHeight;
            ValidateFinite(angle, nameof(turns)); ValidateFinite(height, nameof(turnHeight));
            int segments = SelectSegments(frame.Radius, radius, turns, tolerance, maximumSegments);
            var curves = new List<BezierCurveCubic>(segments);
            Vector3 p0, d0; Evaluate(frame, axisBasePoint, radius, angle, height, 0.0, out p0, out d0);
            Vector3 firstDerivative = d0;
            for (int i = 0; i < segments; ++i)
            {
                Vector3 p1, d1;
                Evaluate(frame, axisBasePoint, radius, angle, height, (i + 1.0) / segments, out p1, out d1);
                Vector3 c1 = p0 + d0 / (3.0 * segments), c2 = p1 - d1 / (3.0 * segments);
                ValidateFinite(c1, nameof(tolerance)); ValidateFinite(c2, nameof(tolerance));
                curves.Add(new BezierCurveCubic(p0, c1, c2, p1));
                p0 = p1; d0 = d1;
            }
            var spline = new Spline(curves) { StartTangent = firstDerivative, EndTangent = d0 };
            return new Helix(spline)
            {
                AxisBasePoint = axisBasePoint, StartPoint = startPoint, AxisVector = axisVector,
                Radius = radius, Turns = turns, TurnHeight = turnHeight, IsRightHanded = isRightHanded,
                Normal = frame.Axis
            };
        }

        /// <summary>Evaluates the analytic parameter definition at a normalized parameter in [0,1], not the stored spline.</summary>
        public Vector3 EvaluateDefinition(double parameter)
        {
            Vector3 point, derivative;
            this.EvaluateDefinition(parameter, out point, out derivative);
            return point;
        }

        /// <summary>Evaluates the analytic derivative with respect to the normalized parameter; the vector is not normalized.</summary>
        public Vector3 EvaluateDefinitionDerivative(double parameter)
        {
            Vector3 point, derivative;
            this.EvaluateDefinition(parameter, out point, out derivative);
            return derivative;
        }

        private void EvaluateDefinition(double parameter, out Vector3 point, out Vector3 derivative)
        {
            ValidateFinite(parameter, nameof(parameter));
            if (parameter < 0.0 || parameter > 1.0) throw new ArgumentOutOfRangeException(nameof(parameter));
            DefinitionFrame frame = GetDefinitionFrame(this.axisBasePoint, this.startPoint, this.axisVector);
            if (frame.Radius == 0.0 && this.radius != 0.0)
            {
                Vector3? hint = this.GetDefinitionPhaseHint();
                if (hint.HasValue) ApplyPhaseHint(ref frame, hint.Value);
            }
            double angle = (this.IsRightHanded ? 2.0 : -2.0) * Math.PI * this.turns, height = this.turns * this.turnHeight;
            ValidateFinite(angle, nameof(this.Turns)); ValidateFinite(height, nameof(this.TurnHeight));
            Evaluate(frame, this.axisBasePoint, this.radius, angle, height, parameter, out point, out derivative);
        }

        /// <summary>Returns a conservative exact-arithmetic cubic Hermite deviation bound for equal parameter intervals.</summary>
        /// <remarks>This bounds a newly generated approximation with that segment count, not an arbitrary imported/edited spline.</remarks>
        public double GetApproximationErrorBound(int segments)
        {
            if (segments <= 0) throw new ArgumentOutOfRangeException(nameof(segments));
            DefinitionFrame frame = GetDefinitionFrame(this.axisBasePoint, this.startPoint, this.axisVector);
            return ErrorBound(frame.Radius, this.radius, this.turns, segments);
        }

        /// <summary>Returns a new helix with a regenerated error-bounded spline, retaining parameters, common metadata and XData.</summary>
        /// <remarks>The source, its database identity and its old spline arrays are not mutated. Old fit metadata is replaced, not merged.</remarks>
        public Helix WithRegeneratedSpline(double tolerance = 1.0e-5, int maximumSegments = 65536)
        {
            Helix result = CreateCore(this.axisBasePoint, this.startPoint, this.axisVector, this.radius, this.turns,
                this.turnHeight, this.IsRightHanded, tolerance, maximumSegments, this.GetDefinitionPhaseHint());
            result.MajorReleaseNumber = this.majorReleaseNumber; result.MaintenanceReleaseNumber = this.maintenanceReleaseNumber;
            result.Constraint = this.constraint;
            result.Layer = (Layer) this.Layer.Clone(); result.Linetype = (Linetype) this.Linetype.Clone();
            result.Color = (AciColor) this.Color.Clone(); result.Transparency = (Transparency) this.Transparency.Clone();
            result.Lineweight = this.Lineweight; result.LinetypeScale = this.LinetypeScale;
            result.IsVisible = this.IsVisible; result.Normal = this.Normal;
            result.KnotTolerance = this.KnotTolerance; result.CtrlPointTolerance = this.CtrlPointTolerance; result.FitTolerance = this.FitTolerance;
            foreach (XData data in this.XData.Values) result.XData.Add((XData) data.Clone());
            return result;
        }

        private struct DefinitionFrame
        {
            internal Vector3 Axis, Radial, Perpendicular;
            internal double Radius;
        }

        // At a zero-radius start the parameter subclass does not encode
        // radial phase. An authored spline tangent supplies that missing phase;
        // without a usable hint the deterministic arbitrary-axis frame is used.
        private Vector3? GetDefinitionPhaseHint()
        {
            return this.StartTangent;
        }

        private static Vector3 DivideComponents(Vector3 value, double scale)
        {
            return new Vector3(value.X / scale, value.Y / scale, value.Z / scale);
        }

        private static void ApplyPhaseHint(ref DefinitionFrame frame, Vector3 hint)
        {
            ValidateFinite(hint, nameof(hint));
            Vector3 radial = hint - frame.Axis * Vector3.DotProduct(hint, frame.Axis);
            double length = StableLength(radial);
            if (!IsFinite(length)) throw new ArgumentOutOfRangeException(nameof(hint));
            if (length == 0.0) return;
            frame.Radial = DivideComponents(radial, length);
            frame.Perpendicular = Vector3.CrossProduct(frame.Axis, frame.Radial);
        }

        private static DefinitionFrame GetDefinitionFrame(Vector3 axisBase, Vector3 start, Vector3 axis)
        {
            ValidateFinite(axisBase, nameof(axisBase)); ValidateFinite(start, nameof(start)); ValidateFinite(axis, nameof(axis));
            double length = StableLength(axis);
            if (!IsFinite(length) || length == 0.0) throw new ArgumentOutOfRangeException(nameof(axis));
            Vector3 n = DivideComponents(axis, length), radial = start - axisBase;
            ValidateFinite(radial, nameof(start));
            double r = StableLength(radial), axial = Vector3.DotProduct(radial, n);
            if (!IsFinite(r) || Math.Abs(axial) > 1.0e-10 * r)
                throw new ArgumentException("HELIX start point must lie in the plane through the axis base perpendicular to its axis.", nameof(start));
            radial -= n * axial; // Remove accepted floating-point orthogonality residual.
            r = StableLength(radial);
            Vector3 u = r == 0.0 ? MathHelper.ArbitraryAxis(n) * Vector3.UnitX : DivideComponents(radial, r);
            return new DefinitionFrame { Axis = n, Radial = u, Perpendicular = Vector3.CrossProduct(n, u), Radius = r };
        }

        private static void Evaluate(DefinitionFrame frame, Vector3 axisBase, double endRadius, double angle,
            double height, double parameter, out Vector3 point, out Vector3 derivative)
        {
            double deltaRadius = endRadius - frame.Radius;
            double radiusAt = frame.Radius * (1.0 - parameter) + endRadius * parameter;
            double phase = angle * parameter, cos = Math.Cos(phase), sin = Math.Sin(phase);
            Vector3 radial = frame.Radial * cos + frame.Perpendicular * sin;
            Vector3 angular = -frame.Radial * sin + frame.Perpendicular * cos;
            point = axisBase + frame.Axis * (height * parameter) + radial * radiusAt;
            derivative = frame.Axis * height + radial * deltaRadius + angular * (radiusAt * angle);
            ValidateFinite(point, nameof(parameter)); ValidateFinite(derivative, nameof(parameter));
        }

        private static double ErrorBound(double startRadius, double endRadius, double turns, int segments)
        {
            if (startRadius == 0.0 && endRadius == 0.0) return 0.0;
            // Evaluate the fourth-derivative/Hermite bound in log space so
            // large radius and tiny angular step cannot produce infinity*zero.
            double logStep = Math.Log(2.0 * Math.PI) + Math.Log(turns) - Math.Log(segments);
            double radial = Math.Log(Math.Max(startRadius, endRadius)) + 4.0 * logStep;
            double delta = Math.Abs(endRadius - startRadius);
            double taper = delta == 0.0 ? double.NegativeInfinity :
                Math.Log(4.0) + Math.Log(delta) - Math.Log(segments) + 3.0 * logStep;
            double max = Math.Max(radial, taper);
            double sum = max + Math.Log(Math.Exp(radial - max) + Math.Exp(taper - max));
            // The scalar Hermite remainder is h^4/384. sqrt(3) converts
            // component-wise fourth-derivative bounds to Euclidean distance.
            double bound = Math.Exp(sum + Math.Log(Math.Sqrt(3.0) / 384.0)) * (1.0 + 1.0e-12);
            if (double.IsPositiveInfinity(bound)) return bound;
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(bound) + 1);

        }

        private static int SelectSegments(double startRadius, double endRadius, double turns, double tolerance, int maximumSegments)
        {
            ValidateFinite(tolerance, nameof(tolerance));
            if (tolerance <= 0.0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            if (maximumSegments < 1 || maximumSegments > 1048576) throw new ArgumentOutOfRangeException(nameof(maximumSegments));
            double minimum = startRadius == 0.0 && endRadius == 0.0 ? 1.0 : Math.Max(1.0, Math.Ceiling(4.0 * turns));
            if (minimum > maximumSegments) throw new ArgumentException("HELIX turn count exceeds the supplied cubic segment budget.", nameof(maximumSegments));
            int segments = (int) minimum;
            while (ErrorBound(startRadius, endRadius, turns, segments) > tolerance)
            {
                if (segments == maximumSegments)
                    throw new ArgumentException("HELIX approximation cannot meet tolerance within the supplied segment budget.", nameof(maximumSegments));
                segments = Math.Min(maximumSegments, checked(segments * 2));
            }
            return segments;
        }
    }
}
