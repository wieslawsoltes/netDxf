// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Ellipse
    {
        /// <summary>Maximum derivative order supported by ellipse parameter evaluation.</summary>
        public const int MaximumDerivativeOrder = 10;

        /// <summary>Evaluates the supporting ellipse at an eccentric parameter in radians.</summary>
        /// <param name="parameter">Finite eccentric parameter in the closed interval [0, 2*pi].</param>
        /// <returns>A world-coordinate point on the ellipse centerline.</returns>
        /// <remarks>See EvaluateDerivatives for parameter, trimming and numerical conventions.</remarks>
        public Vector3 PointAt(double parameter)
        {
            return this.EvaluateDerivatives(parameter, 0)[0];
        }

        /// <summary>Evaluates the ellipse point and derivatives with respect to eccentric radians.</summary>
        /// <param name="parameter">Finite eccentric parameter in [0, 2*pi]; it is not a polar angle.</param>
        /// <param name="order">Highest derivative order, from zero through MaximumDerivativeOrder.</param>
        /// <returns>An independent array: index zero is position; index k is the kth WCS derivative.</returns>
        /// <remarks>
        /// Evaluates the supporting ellipse, even outside an elliptical arc's stored trim interval.
        /// Parameters are not clamped, wrapped or converted from public degree-valued polar angles.
        /// Derivatives are not normalized tangents and are not arc-length derivatives. The center
        /// contributes only to position. Thickness, start/end angles, metadata and proxies are not
        /// modified or used to offset the centerline. The stored base normal is used without invoking
        /// derived Normal callbacks. Relevant geometric inputs must be finite with representable
        /// positive semi-axes; an unrepresentable result rejects without exposing a partial array.
        /// Exact quadrants use the shared conic trigonometric helper. Other trigonometry, rotation
        /// and local products are binary64. The final affine coordinate uses the shared exact dyadic
        /// dot and one rounding, not correctly rounded analytic trigonometry. Nonzero local-product
        /// or final-coordinate underflow to zero and overflow reject. Exact zero returns positive
        /// zero. Work and result allocation are bounded by eleven vectors; concurrent mutation is
        /// outside this read-only operation's contract.
        /// </remarks>
        public Vector3[] EvaluateDerivatives(double parameter, int order = 1)
        {
            if (double.IsNaN(parameter) || double.IsInfinity(parameter) || parameter < 0 || parameter > MathHelper.TwoPI)
                throw new ArgumentOutOfRangeException(nameof(parameter), "An eccentric parameter in [0, 2*pi] is required.");
            if (order < 0 || order > MaximumDerivativeOrder)
                throw new ArgumentOutOfRangeException(nameof(order), "The derivative order must be between zero and ten.");
            ConicParameter.CheckAxes(this.majorAxis, this.minorAxis);
            EllipseFinite(this.center); EllipseFinite(this.rotation); EllipseFinite(base.Normal);
            Matrix3 ocs = MathHelper.ArbitraryAxis(base.Normal);
            ConicParameter.SinCos(this.rotation * MathHelper.DegToRad, out double sr, out double cr);
            Matrix3 frame = ocs * new Matrix3(cr, -sr, 0, sr, cr, 0, 0, 0, 1);
            ConicParameter.SinCos(parameter, out double sine, out double cosine);
            double a = this.majorAxis * 0.5, b = this.minorAxis * 0.5;
            var result = new Vector3[order + 1];
            for (int i = 0; i <= order; i++)
            {
                // Recurrence avoids adding k*pi/2 to a small parameter and losing
                // its low bits. The fourth derivative repeats the original pair.
                double x, y;
                switch (i % 4)
                {
                    case 0: x = cosine; y = sine; break;
                    case 1: x = -sine; y = cosine; break;
                    case 2: x = -cosine; y = -sine; break;
                    default: x = sine; y = -cosine; break;
                }
                var local = new Vector3(EllipseParameterProduct(a, x), EllipseParameterProduct(b, y), 0);
                result[i] = InfiniteLineTransform.TransformPoint(frame, local, i == 0 ? this.center : Vector3.Zero);
            }
            return result;
        }

        private static double EllipseParameterProduct(double length, double factor)
        {
            double product = length * factor;
            if (factor != 0 && product == 0)
                throw new NotSupportedException("A nonzero ellipse parameter component underflows to zero.");
            return product;
        }
    }
}
