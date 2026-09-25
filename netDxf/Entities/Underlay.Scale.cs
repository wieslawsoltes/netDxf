// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Underlay
    {
        private double scaleZ = 1.0;

        /// <summary>Gets or sets the independent DXF group 43 scale factor.</summary>
        /// <remarks>
        /// Defaults to 1. Assignments require a finite, nonzero value and preserve its sign.
        /// This stored scalar does not participate in the two-dimensional page geometry;
        /// affine page transformations retain it unchanged. A loaded finite zero is
        /// retained for file fidelity rather than repaired. Changing the scalar clears
        /// common proxy graphics; rejected assignments leave the entity unchanged.
        /// </remarks>
        public double ScaleZ
        {
            get { return this.scaleZ; }
            set
            {
                FiniteUnderlay(value);
                if (value == 0.0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The underlay Z scale must be nonzero.");
                PrimitiveGeometryMutation.Assign(this, ref this.scaleZ, value);
            }
        }

        // Restoration is not a public geometry edit. Do not repair signs, subnormal
        // magnitudes or finite zero values, or invalidate the reader's common data.
        internal void SetScaleFromDxf(Vector3 value)
        {
            FiniteUnderlay(value);
            this.scale = new Vector2(value.X, value.Y);
            this.scaleZ = value.Z;
        }
    }
}
