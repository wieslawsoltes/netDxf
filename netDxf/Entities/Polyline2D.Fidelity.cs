// netDxf is distributed under the MIT license; see LICENSE in the repository root.
using System;

namespace netDxf.Entities
{
    public partial class Polyline2D
    {
        private double? constantWidth;

        /// <summary>Gets or sets the optional LWPOLYLINE group 43 constant width.</summary>
        /// <remarks>Null means absent; zero is an explicitly stored zero. This property never
        /// rewrites the raw per-vertex widths. For effective segment widths use GetEffectiveStartWidth
        /// and GetEffectiveEndWidth. Nonzero constant width takes precedence in the supported ezdxf-compatible profile.</remarks>
        public double? ConstantWidth
        {
            get { return this.constantWidth; }
            set
            {
                if (this.HasStoredRecords && value.HasValue)
                    throw new NotSupportedException("ConstantWidth is a LWPOLYLINE field; use vertex width overrides for retained legacy POLYLINE records.");
                if (value.HasValue) ValidateWidth(value.Value, nameof(value));
                this.constantWidth = value;
            }
        }

        /// <summary>Gets the effective outgoing segment start width for the specified vertex.</summary>
        /// <param name="vertexIndex">Index of the outgoing segment's vertex.</param>
        /// <returns>Nonzero lightweight constant width, or the vertex override; an omitted legacy override inherits the parent default.</returns>
        public double GetEffectiveStartWidth(int vertexIndex)
        {
            Polyline2DVertex vertex = this.vertexes[vertexIndex];
            return this.ConstantWidth.GetValueOrDefault() > 0 ? this.ConstantWidth.Value : vertex.StartWidthOverride ?? this.LegacyDefaultStartWidth.GetValueOrDefault();
        }

        /// <summary>Gets the effective outgoing segment end width for the specified vertex.</summary>
        /// <param name="vertexIndex">Index of the outgoing segment's vertex.</param>
        /// <returns>Nonzero lightweight constant width, or the vertex override; an omitted legacy override inherits the parent default.</returns>
        public double GetEffectiveEndWidth(int vertexIndex)
        {
            Polyline2DVertex vertex = this.vertexes[vertexIndex];
            return this.ConstantWidth.GetValueOrDefault() > 0 ? this.ConstantWidth.Value : vertex.EndWidthOverride ?? this.LegacyDefaultEndWidth.GetValueOrDefault();
        }

        internal static void ValidateWidth(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, value, "Polyline width must be finite and nonnegative.");
        }

        internal void ValidateVertexFidelity()
        {
            foreach (Polyline2DVertex vertex in this.vertexes)
            {
                if (vertex == null) throw new InvalidOperationException("A polyline cannot contain a null vertex.");
                ValidateWidth(vertex.StartWidth, "StartWidth");
                ValidateWidth(vertex.EndWidth, "EndWidth");
            }
        }

        private double GetWidthTransformScale(Matrix3 transformation)
        {
            this.ValidateVertexFidelity();
            bool wide = this.ConstantWidth.GetValueOrDefault() > 0 || this.LegacyDefaultStartWidth.GetValueOrDefault() > 0
                || this.LegacyDefaultEndWidth.GetValueOrDefault() > 0;
            if (this.HasStoredRecords) foreach (Polyline2DVertex vertex in this.vertexes) wide |= vertex.Bulge != 0;
            foreach (Polyline2DVertex vertex in this.vertexes) wide |= vertex.StartWidth > 0 || vertex.EndWidth > 0;
            if (!wide) return 1;
            Matrix3 ocs = MathHelper.ArbitraryAxis(this.Normal);
            Vector3 x = transformation * (ocs * Vector3.UnitX);
            Vector3 y = transformation * (ocs * Vector3.UnitY);
            double scale = x.Modulus(), otherScale = y.Modulus();
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0 ||
                double.IsNaN(otherScale) || double.IsInfinity(otherScale) || otherScale <= 0 ||
                Math.Abs(scale - otherScale) > MathHelper.Epsilon * Math.Max(scale, otherScale) ||
                Math.Abs(Vector3.DotProduct(x / scale, y / otherScale)) > MathHelper.Epsilon)
                throw new NotSupportedException("Wide polylines require a nonsingular uniform scale in their plane; nonuniform scaling or shear cannot preserve the stored stroke widths.");
            Vector3 transformedNormal = transformation * this.Normal;
            double normalScale = transformedNormal.Modulus();
            if (normalScale <= 0 || double.IsNaN(normalScale) || double.IsInfinity(normalScale) ||
                Math.Abs(Vector3.DotProduct(x / scale, transformedNormal / normalScale)) > MathHelper.Epsilon ||
                Math.Abs(Vector3.DotProduct(y / otherScale, transformedNormal / normalScale)) > MathHelper.Epsilon)
                throw new NotSupportedException("Wide polyline transforms must preserve the normal perpendicular to the entity plane.");
            if (Vector3.DotProduct(Vector3.CrossProduct(x / scale, y / otherScale), transformedNormal / normalScale) < 0)
                foreach (Polyline2DVertex vertex in this.vertexes)
                    if (vertex.Bulge != 0)
                        throw new NotSupportedException("Reflections of wide polylines with arc segments require explicit geometry conversion.");
            if (this.ConstantWidth.HasValue) ValidateWidth(this.ConstantWidth.Value * scale, "ConstantWidth");
            if (this.LegacyDefaultStartWidth.HasValue) ValidateWidth(this.LegacyDefaultStartWidth.Value * scale, "LegacyDefaultStartWidth");
            if (this.LegacyDefaultEndWidth.HasValue) ValidateWidth(this.LegacyDefaultEndWidth.Value * scale, "LegacyDefaultEndWidth");
            foreach (Polyline2DVertex vertex in this.vertexes)
            {
                ValidateWidth(vertex.StartWidth * scale, "StartWidth");
                ValidateWidth(vertex.EndWidth * scale, "EndWidth");
            }
            return scale;
        }
    }
}
