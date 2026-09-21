// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Polyline2D
    {
        // Retained legacy VERTEX chains have their own admission/publication path.
        // This path handles authored and lightweight definitions, preserving the
        // caller-visible List and vertex objects (including shared references).
        private void ApplyOrdinaryAffine(Matrix3 matrix, Vector3 translation)
        {
            this.ValidateVertexFidelity();
            if (this.constantWidth.HasValue) ValidateWidth(this.constantWidth.Value, "ConstantWidth");
            var positions = new Vector2[this.vertexes.Count];
            bool curved = false, wide = this.constantWidth.GetValueOrDefault() > 0;
            for (int i = 0; i < positions.Length; i++)
            {
                Polyline2DVertex vertex = this.vertexes[i];
                if (!LegacyFinite(vertex.Bulge))
                    throw new InvalidOperationException("Polyline bulges must be finite before transformation.");
                positions[i] = vertex.Position;
                curved |= vertex.Bulge != 0;
                wide |= vertex.StartWidth > 0 || vertex.EndWidth > 0;
            }

            // Shared planar preparation validates even identity inputs, derives the
            // normal from transformed in-plane axes, and handles signed thickness.
            PlanarEntityTransform prepared = PlanarEntityTransform.Prepare(matrix, translation,
                positions, base.Normal, this.elevation, this.thickness);
            bool identity = matrix.IsIdentityExact && translation.X == 0 && translation.Y == 0 && translation.Z == 0;
            if (identity) return;

            double scale = 1;
            if (curved || wide)
            {
                // A circular bulge or an isotropic stroke cannot encode arbitrary
                // nonuniform scale/shear. Fixed-tolerance shared circle admission
                // must not depend on the application-wide approximate-equality epsilon.
                CircularEntityTransform circle = CircularEntityTransform.Prepare(matrix, Vector3.Zero,
                    Vector3.Zero, base.Normal, 1, 0);
                scale = circle.Radius;
                // Preserve the existing conservative wide-arc reflection and
                // perpendicular-normal policy, including existing rejection tests.
                if (wide) this.GetWidthTransformScale(matrix, base.Normal);
            }
            double? constant = ScalePreparedWidth(this.constantWidth, scale);
            var starts = new double?[positions.Length];
            var ends = new double?[positions.Length];
            bool changed = prepared.Changed || !SamePreparedWidth(constant, this.constantWidth);
            for (int i = 0; i < positions.Length; i++)
            {
                Polyline2DVertex vertex = this.vertexes[i];
                starts[i] = ScalePreparedWidth(vertex.StartWidthOverride, scale);
                ends[i] = ScalePreparedWidth(vertex.EndWidthOverride, scale);
                changed |= !SamePreparedWidth(starts[i], vertex.StartWidthOverride) ||
                    !SamePreparedWidth(ends[i], vertex.EndWidthOverride);
            }
            if (!changed) return;

            // No overridable Normal accessor participates in publication. All
            // remaining setters are nonvirtual and receive already validated data.
            base.Normal = prepared.Normal;
            for (int i = 0; i < positions.Length; i++)
            {
                this.vertexes[i].Position = prepared.Vertexes[i];
                this.vertexes[i].StartWidthOverride = starts[i];
                this.vertexes[i].EndWidthOverride = ends[i];
            }
            this.constantWidth = constant;
            this.elevation = prepared.Elevation;
            this.thickness = prepared.Thickness;
            this.ClearProxyGraphics();
        }

        private static double? ScalePreparedWidth(double? width, double scale)
        {
            if (!width.HasValue || width.Value == 0 || scale == 1) return width;
            double result = width.Value * scale;
            ValidateWidth(result, "transformedWidth");
            if (result == 0)
                throw new NotSupportedException("A nonzero transformed polyline width underflows to zero.");
            return result;
        }

        private static bool SamePreparedWidth(double? first, double? second)
        {
            return first.HasValue == second.HasValue && (!first.HasValue ||
                BitConverter.DoubleToInt64Bits(first.Value) == BitConverter.DoubleToInt64Bits(second.Value));
        }
    }
}
