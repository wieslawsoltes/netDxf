using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Hatch
    {
        private bool HasExplicitAffinePattern()
        {
            return this.Pattern.Fill == HatchFillType.PatternFill
                && (this.Pattern.Type == HatchType.Predefined || this.Pattern.Type == HatchType.Custom);
        }

        private static void RequireAffinePatternNear(Vector2 expected, Vector2 actual)
        {
            RequireAffineFinite(expected); RequireAffineFinite(actual);
            double scale = Math.Max(1, Math.Max(Math.Abs(expected.X), Math.Abs(expected.Y)));
            if (Math.Abs(expected.X - actual.X) > 1e-10 * scale || Math.Abs(expected.Y - actual.Y) > 1e-10 * scale)
                throw new ArgumentException("The transformed HATCH pattern packet cannot preserve its geometry within relative tolerance 1e-10.");
        }

        private static Vector2 AffinePatternRotate(Vector2 value, double angle)
        {
            Vector2 direction = AffinePolarDirection(angle);
            return new Vector2(direction.X * value.X - direction.Y * value.Y, direction.Y * value.X + direction.X * value.Y);
        }

        private HatchPattern TransformAffinePattern(Matrix3 transformation, Vector3 translation,
            Func<Vector2, bool, Vector2> map, double scale, double angle, bool identity)
        {
            HatchPattern original = this.Pattern;
            if (!identity && original.GetType() != typeof(HatchPattern))
                throw new NotSupportedException("An unknown HATCH pattern subclass cannot be transformed without a storage contract.");
            if (!identity && original.LineDefinitions.Count == 0)
                throw new NotSupportedException("HATCH pattern transforms require explicit line definitions.");
            if (original.LineDefinitions.Count > short.MaxValue)
                throw new ArgumentException("The transformed HATCH pattern line count cannot be stored.");
            RequireAffineFinite(original.Origin);
            // Autodesk defines Origin as a WCS Point2d. This API embeds that stored
            // datum at Z=0; it must not be confused with the OCS line base points.
            Vector3 origin = transformation * new Vector3(original.Origin.X, original.Origin.Y, 0) + translation;
            RequireAffineFinite(origin);
            double termX = transformation.M31 * original.Origin.X, termY = transformation.M32 * original.Origin.Y;
            RequireAffineFinite(termX); RequireAffineFinite(termY);
            const double originRoundoff = 8 * 2.2204460492503131e-16;
            double originTolerance = originRoundoff * Math.Abs(termX) + originRoundoff * Math.Abs(termY) + originRoundoff * Math.Abs(translation.Z);
            if (Math.Abs(origin.Z) > originTolerance)
                throw new NotSupportedException("The transformed HATCH pattern origin cannot be represented by its stored WCS Point2d.");
            var result = new HatchPattern(original.Name, original.Description)
            {
                Style = original.Style, Fill = original.Fill, Type = original.Type, IsDouble = original.IsDouble,
                Origin = new Vector2(origin.X, origin.Y), Angle = angle, Scale = scale
            };
            foreach (HatchPatternLineDefinition line in original.LineDefinitions)
            {
                if (line == null) throw new ArgumentException("HATCH pattern lines must be supplied.");
                RequireAffineFinite(line.Angle); RequireAffineFinite(line.Origin); RequireAffineFinite(line.Delta);
                if (line.DashPattern.Count > short.MaxValue)
                    throw new ArgumentException("The transformed HATCH dash count cannot be stored.");
                double oldAngle = original.Angle + line.Angle;
                Vector2 oldBase = original.Scale * AffinePatternRotate(line.Origin, original.Angle);
                Vector2 oldOffset = original.Scale * AffinePatternRotate(line.Delta, oldAngle);
                Vector2 oldDirection = AffinePolarDirection(oldAngle);
                Vector2 direction = map(oldDirection, true);
                double stretch = AffineHypot(direction.X, direction.Y);
                RequireAffineFinite(stretch);
                if (stretch == 0) throw new ArgumentException("A HATCH pattern line direction collapses.");
                double newAngle = Math.Atan2(direction.Y, direction.X) * MathHelper.RadToDeg;
                Vector2 newBase = map(oldBase, false), newOffset = map(oldOffset, true);
                var copy = new HatchPatternLineDefinition
                {
                    Angle = newAngle - angle,
                    Origin = AffinePatternRotate(newBase, -angle) / scale,
                    Delta = AffinePatternRotate(newOffset, -newAngle) / scale
                };
                RequireAffineFinite(copy.Angle); RequireAffineFinite(copy.Origin); RequireAffineFinite(copy.Delta);
                if (line.Delta.Y != 0 && copy.Delta.Y == 0)
                    throw new ArgumentException("The transformed HATCH pattern line spacing cannot be represented.");
                double encodedAngle = result.Angle + copy.Angle;
                RequireAffinePatternNear(direction / stretch, AffinePolarDirection(encodedAngle));
                RequireAffinePatternNear(newBase, result.Scale * AffinePatternRotate(copy.Origin, result.Angle));
                RequireAffinePatternNear(newOffset, result.Scale * AffinePatternRotate(copy.Delta, encodedAngle));
                foreach (double dash in line.DashPattern)
                {
                    RequireAffineFinite(dash);
                    double expected = (original.Scale * dash) * stretch;
                    RequireAffineFinite(expected);
                    // Zero is a dot, including the sign bit of a stored signed zero.
                    double value = dash == 0 ? dash : expected / result.Scale;
                    RequireAffineFinite(value);
                    double encoded = value * result.Scale;
                    RequireAffineFinite(encoded);
                    if (dash != 0 && (expected == 0 || value == 0 || encoded == 0))
                        throw new ArgumentException("The transformed HATCH dash cannot be represented without becoming a dot.");
                    if (Math.Abs(expected - encoded) > 1e-10 * Math.Max(1, Math.Abs(expected)))
                        throw new ArgumentException("The transformed HATCH dash length cannot be represented.");
                    copy.DashPattern.Add(value);
                }
                result.LineDefinitions.Add(copy);
            }
            return result;
        }
    }
}
