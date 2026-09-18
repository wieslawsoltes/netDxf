// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class PolygonMesh
    {
        /// <summary>Maximum number of vector interpolation operations in one Bézier sampling call.</summary>
        /// <remarks>Checked before allocation using the cheaper of the two separable evaluation orders.</remarks>
        public const long MaximumBezierBlendOperations = 64000000;

        private List<Vector3> BezierMeshVertexes(int precisionU, int precisionV)
        {
            long blendsU = (long)this.u * (this.u - 1) / 2;
            long blendsV = (long)this.v * (this.v - 1) / 2;
            long orderU = precisionU * ((long)this.v * blendsU + (long)precisionV * blendsV);
            long orderV = precisionV * ((long)this.u * blendsV + (long)precisionU * blendsU);
            bool firstU = orderU <= orderV;
            if (Math.Min(orderU, orderV) > MaximumBezierBlendOperations)
                throw new ArgumentOutOfRangeException(nameof(precisionU), "The Bézier evaluation exceeds MaximumBezierBlendOperations.");
            int firstSamples = firstU ? precisionU : precisionV;
            int secondSamples = firstU ? precisionV : precisionU;
            int firstControls = firstU ? this.u : this.v;
            int secondControls = firstU ? this.v : this.u;
            if ((long)firstSamples * secondControls > MaximumSurfaceSamples)
                throw new ArgumentOutOfRangeException(nameof(precisionU), "The Bézier intermediate grid exceeds MaximumSurfaceSamples.");

            var intermediate = new Vector3[firstSamples * secondControls];
            var scratch = new Vector3[Math.Max(firstControls, secondControls)];
            for (int i = 0; i < firstSamples; i++)
            {
                double t = i / (firstSamples - 1.0);
                for (int j = 0; j < secondControls; j++)
                    intermediate[i + firstSamples * j] = BezierCurve(this.vertexes,
                        firstU ? j * this.u : j, firstU ? 1 : this.u, firstControls, t, scratch);
            }
            var points = new Vector3[precisionU * precisionV];
            for (int j = 0; j < secondSamples; j++)
            {
                double t = j / (secondSamples - 1.0);
                for (int i = 0; i < firstSamples; i++)
                {
                    Vector3 point = BezierCurve(intermediate, i, firstSamples, secondControls, t, scratch);
                    points[firstU ? i + j * precisionU : j + i * precisionU] = point;
                }
            }
            return new List<Vector3>(points);
        }

        private static Vector3 BezierCurve(Vector3[] controls, int start, int stride, int count, double t, Vector3[] scratch)
        {
            if (t == 0.0) return controls[start];
            if (t == 1.0) return controls[start + (count - 1) * stride];
            for (int i = 0; i < count; i++) scratch[i] = controls[start + i * stride];
            // de Casteljau avoids binomial overflow and underflow of a tiny
            // Bernstein weight whose product with a large control is representable.
            for (int remaining = count - 1; remaining > 0; remaining--)
                for (int i = 0; i < remaining; i++)
                    scratch[i] = new Vector3(BezierBlend(scratch[i].X, scratch[i + 1].X, t),
                        BezierBlend(scratch[i].Y, scratch[i + 1].Y, t),
                        BezierBlend(scratch[i].Z, scratch[i + 1].Z, t));
            return scratch[0];
        }

        private static double BezierBlend(double a, double b, double t)
        {
            if (a == b) return a;
            // Never form b-a: opposite extreme finite controls can overflow it.
            double value = (1.0 - t) * a + t * b;
            // Positive convex coefficients put the exact value in [a,b]. Clamp
            // a rounding overshoot, including a same-sign sum overflowing at MaxValue.
            double minimum = Math.Min(a, b), maximum = Math.Max(a, b);
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }
}
