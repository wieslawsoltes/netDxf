// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Image
    {
        private static bool SameImage(double a, double b)
        { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }
        private static bool SameImage(Vector2 a, Vector2 b)
        { return SameImage(a.X, b.X) && SameImage(a.Y, b.Y); }
        private static bool SameImage(Vector3 a, Vector3 b)
        { return SameImage(a.X, b.X) && SameImage(a.Y, b.Y) && SameImage(a.Z, b.Z); }
        private static void FiniteImage(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Image geometry and transformations must be finite.");
        }
        private static void FiniteImage(Vector3 value)
        { FiniteImage(value.X); FiniteImage(value.Y); FiniteImage(value.Z); }
        private static void FiniteImage(Vector2 value)
        { FiniteImage(value.X); FiniteImage(value.Y); }

        private void AssignImageAxis(ref Vector2 field, Vector2 value)
        {
            bool changed = !SameImage(field, value);
            field = value;
            if (changed) this.ClearProxyGraphics();
        }

        // Normalize after scaling, without the global geometric epsilon or a
        // reciprocal that overflows for finite subnormal components.
        private static Vector2 ImageAxis(Vector2 value, out double scale, out double factor)
        {
            FiniteImage(value);
            scale = Math.Max(Math.Abs(value.X), Math.Abs(value.Y));
            if (scale == 0) throw new NotSupportedException("The image axis must not collapse to zero.");
            double x = value.X / scale, y = value.Y / scale;
            factor = Math.Sqrt(x * x + y * y);
            return new Vector2(x / factor, y / factor);
        }

        private void SetAbsoluteRotation(double value)
        {
            FiniteImage(value); FiniteImage(this.uvector); FiniteImage(this.vvector);
            double unusedScale, unusedFactor;
            ImageAxis(this.uvector, out unusedScale, out unusedFactor);
            ImageAxis(this.vvector, out unusedScale, out unusedFactor);
            double target = MathHelper.NormalizeAngle(value);
            double current = this.Rotation;
            if (target == current) return;
            double radians = target * MathHelper.DegToRad;
            Vector2 nextU = new Vector2(Math.Cos(radians), Math.Sin(radians));
            // Store a deterministic absolute U direction. This also makes a
            // repeated assignment stable when atan2 rounds the reported angle.
            if (SameImage(this.uvector, nextU)) return;
            double delta = (target - current) * MathHelper.DegToRad;
            double c = Math.Cos(delta), s = Math.Sin(delta);
            Vector2 nextV = new Vector2(c * this.vvector.X - s * this.vvector.Y,
                s * this.vvector.X + c * this.vvector.Y);
            FiniteImage(nextV);
            this.ClearProxyGraphics();
            this.uvector = nextU;
            this.vvector = nextV;
        }

        private void ApplyImageTransform(Matrix3 matrix, Vector3 translation)
        {
            bool linearIdentity = true;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
            {
                FiniteImage(matrix[r, c]);
                linearIdentity &= matrix[r, c] == (r == c ? 1.0 : 0.0);
            }
            FiniteImage(translation); FiniteImage(this.position); FiniteImage(base.Normal);
            FiniteImage(this.width); FiniteImage(this.height);
            if (this.width <= 0 || this.height <= 0)
                throw new NotSupportedException("An image requires positive finite drawing dimensions.");
            double scale, factor;
            Vector2 sourceU = ImageAxis(this.uvector, out scale, out factor);
            Vector2 sourceV = ImageAxis(this.vvector, out scale, out factor);
            if (Math.Abs(sourceU.X * sourceV.Y - sourceU.Y * sourceV.X) <= 1e-14)
                throw new NotSupportedException("The image pixel axes are numerically collinear.");
            if (linearIdentity && translation.X == 0 && translation.Y == 0 && translation.Z == 0) return;

            Vector3 nextPosition = InfiniteLineTransform.TransformPoint(matrix, this.position, translation);
            if (linearIdentity)
            {
                PrimitiveGeometryMutation.Assign(this, ref this.position, nextPosition);
                return;
            }

            // A*n is generally not the transformed image plane normal. Reuse
            // the established planar rank/normal policy without subtracting
            // translated corners (which could erase small image dimensions).
            var plane = PlanarEntityTransform.Prepare(matrix, Vector3.Zero, new Vector2[0], base.Normal, 0, 0);
            Matrix3 toWorld = MathHelper.ArbitraryAxis(base.Normal);
            Matrix3 toObject = MathHelper.ArbitraryAxis(plane.Normal).Transpose();
            Vector3 u = toObject * (matrix * (toWorld * new Vector3(this.uvector.X, this.uvector.Y, 0)));
            Vector3 v = toObject * (matrix * (toWorld * new Vector3(this.vvector.X, this.vvector.Y, 0)));
            FiniteImage(u); FiniteImage(v);
            Vector2 nextU = ImageAxis(new Vector2(u.X, u.Y), out scale, out factor);
            double nextWidth = (this.width * scale) * factor;
            Vector2 nextV = ImageAxis(new Vector2(v.X, v.Y), out scale, out factor);
            double nextHeight = (this.height * scale) * factor;
            FiniteImage(nextWidth); FiniteImage(nextHeight);
            if (nextWidth <= 0 || nextHeight <= 0)
                throw new NotSupportedException("The transformed image dimensions underflow to zero.");
            if (Math.Abs(nextU.X * nextV.Y - nextU.Y * nextV.X) <= 1e-14)
                throw new NotSupportedException("The transformed image pixel axes are numerically collinear.");
            if (SameImage(nextPosition, this.position) && SameImage(plane.Normal, base.Normal)
                && SameImage(nextU, this.uvector) && SameImage(nextV, this.vvector)
                && SameImage(nextWidth, this.width) && SameImage(nextHeight, this.height)) return;

            // Publish only after all calculations/validation. No virtual Normal
            // callback or clipping/definition mutation participates in this step.
            this.ClearProxyGraphics();
            base.Normal = plane.Normal;
            this.position = nextPosition;
            this.uvector = nextU;
            this.vvector = nextV;
            this.width = nextWidth;
            this.height = nextHeight;
        }

        /// <summary>Transforms this image by a finite affine matrix.</summary>
        /// <param name="transformation">Affine matrix using column vectors.</param>
        /// <remarks>Projective input rejects before the virtual affine entry point is called.</remarks>
        public override void TransformBy(Matrix4 transformation)
        {
            PlanarEntityTransform.CheckAffine(transformation);
            base.TransformBy(transformation);
        }
    }
}
