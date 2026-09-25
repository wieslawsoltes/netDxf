// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Underlay
    {
        private static bool SameUnderlay(double a, double b)
        { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }

        private static bool SameUnderlay(Vector3 a, Vector3 b)
        { return SameUnderlay(a.X, b.X) && SameUnderlay(a.Y, b.Y) && SameUnderlay(a.Z, b.Z); }

        private static void FiniteUnderlay(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Underlay geometry and transformations must be finite.");
        }

        private static void FiniteUnderlay(Vector3 value)
        { FiniteUnderlay(value.X); FiniteUnderlay(value.Y); FiniteUnderlay(value.Z); }

        private static Vector2 UnderlayAxis(Vector3 value, double sourceScale, out double nextScale)
        {
            FiniteUnderlay(value);
            double scale = Math.Max(Math.Abs(value.X), Math.Abs(value.Y));
            if (scale == 0)
                throw new NotSupportedException("The transformed underlay axis collapses to zero.");
            double x = value.X / scale, y = value.Y / scale;
            double length = Math.Sqrt(x * x + y * y);
            if (Math.Abs(value.Z / scale) > 1e-12)
                throw new NotSupportedException("The transformed underlay axis does not lie in its stored plane.");
            nextScale = (Math.Abs(sourceScale) * scale) * length;
            FiniteUnderlay(nextScale);
            // Match the existing public Scale admission and reader/clone path.
            // Never replace a collapsed or too-small scale with Epsilon.
            if (nextScale == 0 || MathHelper.IsZero(nextScale))
                throw new NotSupportedException("The transformed underlay scale cannot be stored by this model.");
            double sign = sourceScale < 0 ? -1 : 1;
            return new Vector2(sign * x / length, sign * y / length);
        }

        private void ApplyUnderlayTransform(Matrix3 matrix, Vector3 translation)
        {
            bool linearIdentity = true;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
            {
                FiniteUnderlay(matrix[r, c]);
                linearIdentity &= matrix[r, c] == (r == c ? 1.0 : 0.0);
            }
            FiniteUnderlay(translation); FiniteUnderlay(this.position); FiniteUnderlay(base.Normal);
            FiniteUnderlay(this.rotation); FiniteUnderlay(this.scale.X); FiniteUnderlay(this.scale.Y);
            if (this.scale.X == 0 || this.scale.Y == 0)
                throw new NotSupportedException("An underlay requires two nonzero local scales.");
            if (linearIdentity && translation.X == 0 && translation.Y == 0 && translation.Z == 0) return;

            Vector3 nextPosition = InfiniteLineTransform.TransformPoint(matrix, this.position, translation);
            if (linearIdentity)
            {
                PrimitiveGeometryMutation.Assign(this, ref this.position, nextPosition);
                return;
            }

            // A*n is not generally a transformed plane normal. The shared helper
            // obtains it from the actual plane axes and rejects numerical rank loss.
            var plane = PlanarEntityTransform.Prepare(matrix, Vector3.Zero, new Vector2[0], base.Normal, 0, 0);
            Vector3 nextNormal = plane.Normal;
            if ((this.scale.X < 0) != (this.scale.Y < 0)) nextNormal = -nextNormal;
            Matrix3 fromObject = MathHelper.ArbitraryAxis(base.Normal);
            Matrix3 toObject = MathHelper.ArbitraryAxis(nextNormal).Transpose();
            double angle = this.rotation * MathHelper.DegToRad;
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            Vector3 u = toObject * (matrix * (fromObject * new Vector3(cosine, sine, 0)));
            Vector3 v = toObject * (matrix * (fromObject * new Vector3(-sine, cosine, 0)));
            double scaleX, scaleY;
            Vector2 unitU = UnderlayAxis(u, this.scale.X, out scaleX);
            Vector2 unitV = UnderlayAxis(v, this.scale.Y, out scaleY);
            if (Math.Abs(unitU.X * unitV.X + unitU.Y * unitV.Y) > 1e-12
                || unitU.X * unitV.Y - unitU.Y * unitV.X <= 0)
                throw new NotSupportedException("The transformed underlay would require nonorthogonal local axes.");

            // Both magnitudes are positive. Reflection is represented by the
            // oriented normal, not by a diagonal-product determinant heuristic.
            double nextRotation = MathHelper.NormalizeAngle(Math.Atan2(unitU.Y, unitU.X) * MathHelper.RadToDeg);
            double nextAngle = nextRotation * MathHelper.DegToRad;
            if (Math.Abs(Math.Cos(nextAngle) - unitU.X) > 1e-12
                || Math.Abs(Math.Sin(nextAngle) - unitU.Y) > 1e-12)
                throw new NotSupportedException("Angle normalization cannot represent the transformed underlay axis.");
            if (SameUnderlay(nextPosition, this.position) && SameUnderlay(nextNormal, base.Normal)
                && SameUnderlay(scaleX, this.scale.X) && SameUnderlay(scaleY, this.scale.Y)
                && SameUnderlay(nextRotation, this.rotation)) return;

            // All computations and refusals precede publication. Bypass a
            // subclass's virtual Normal callback, as in the other planar entities.
            this.ClearProxyGraphics();
            base.Normal = nextNormal;
            this.position = nextPosition;
            this.scale = new Vector2(scaleX, scaleY);
            this.rotation = nextRotation;
        }

        /// <summary>Transforms this underlay by a finite affine matrix.</summary>
        /// <param name="transformation">Affine matrix using column vectors.</param>
        /// <remarks>Projective input rejects before the virtual affine entry point is called.</remarks>
        public override void TransformBy(Matrix4 transformation)
        {
            PlanarEntityTransform.CheckAffine(transformation);
            base.TransformBy(transformation);
        }
    }
}
