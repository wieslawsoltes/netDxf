// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf
{
    /// <summary>Controls optional 16-bit SPLINE count fields in typed DXF output.</summary>
    public enum DxfSplineCountPolicy
    {
        /// <summary>Preserves the legacy omission of groups 72, 73 and 74.</summary>
        LegacyOmit = 0,
        /// <summary>Writes all three counts only when each fits a nonnegative Int16.</summary>
        WhenRepresentable = 1,
        /// <summary>Requires representable counts, rejecting before typed output preparation otherwise.</summary>
        RequireRepresentable = 2
    }
}
