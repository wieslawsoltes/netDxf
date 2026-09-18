// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Spline
    {
        private void TransformSplineAffine(Matrix3 transformation, Vector3 translation)
        {
            // Tangents are derivative vectors: keep magnitude and apply only the
            // linear map. Validate even the identity path, before staging arrays.
            CheckAffineTangent(this.startTangent);
            CheckAffineTangent(this.endTangent);
            Vector3 normal = base.Normal;
            var controls = VertexAffineTransform.Prepare(this.controlPoints, normal, transformation, translation);
            var fits = VertexAffineTransform.Prepare(this.fitPoints, normal, transformation, translation);
            Vector3? start = TransformAffineTangent(this.startTangent, transformation);
            Vector3? end = TransformAffineTangent(this.endTangent, transformation);
            if (!controls.Changed && !fits.Changed && SameAffineTangent(start, this.startTangent)
                && SameAffineTangent(end, this.endTangent)) return;

            // All arithmetic and validation has completed. Keep caller-visible
            // array identities and bypass a derived Normal setter/getter callback.
            // Do not touch an unchanged normal, including its cached unit flag.
            if (!SameAffineTangent(normal, controls.Normal)) base.Normal = controls.Normal;
            if (controls.Points != null) Array.Copy(controls.Points, this.controlPoints, controls.Points.Length);
            if (fits.Points != null) Array.Copy(fits.Points, this.fitPoints, fits.Points.Length);
            this.startTangent = start;
            this.endTangent = end;
            this.ClearProxyGraphics();
        }

        private static void CheckAffineTangent(Vector3? value)
        {
            if (!value.HasValue) return;
            for (int i = 0; i < 3; i++)
                if (double.IsNaN(value.Value[i]) || double.IsInfinity(value.Value[i]))
                    throw new ArgumentOutOfRangeException(nameof(value), "Spline tangent components must be finite.");
        }

        private static Vector3? TransformAffineTangent(Vector3? value, Matrix3 transformation)
        {
            if (!value.HasValue || transformation.IsIdentityExact) return value;
            return InfiniteLineTransform.TransformPoint(transformation, value.Value, Vector3.Zero);
        }

        private static bool SameAffineTangent(Vector3? left, Vector3? right)
        {
            if (!left.HasValue || !right.HasValue) return left.HasValue == right.HasValue;
            return left.Value.X == right.Value.X && left.Value.Y == right.Value.Y && left.Value.Z == right.Value.Z;
        }

        /// <summary>Applies a finite affine transform to stored spline points and tangents.</summary>
        /// <param name="transformation">An affine matrix with bottom row (0, 0, 0, 1).</param>
        /// <remarks>Projective or unrepresentable geometry rejects before the spline is changed.</remarks>
        public override void TransformBy(Matrix4 transformation)
        {
            InfiniteLineTransform.CheckAffine(transformation);
            // Preserve virtual Matrix3 dispatch, including specialized HELIX admission.
            base.TransformBy(transformation);
        }
    }
}
