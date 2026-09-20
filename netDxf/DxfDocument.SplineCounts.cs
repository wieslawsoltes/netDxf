// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        private DxfSplineCountPolicy splineCountPolicy;

        /// <summary>Gets or sets count-field output for typed SPLINE and HELIX spline data.</summary>
        /// <remarks>
        /// The default is LegacyOmit, preserving existing output. Periodic counts include the
        /// repeated control prefix actually serialized. This process-local output preference is
        /// not stored in DXF; loaded documents start with the default. Raw documents are unaffected.
        /// The document must not be mutated concurrently with saving.
        /// </remarks>
        public DxfSplineCountPolicy SplineCountPolicy
        {
            get { return this.splineCountPolicy; }
            set
            {
                if (value < DxfSplineCountPolicy.LegacyOmit || value > DxfSplineCountPolicy.RequireRepresentable)
                    throw new ArgumentOutOfRangeException(nameof(value));
                this.splineCountPolicy = value;
            }
        }
    }
}
