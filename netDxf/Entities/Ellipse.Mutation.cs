// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Ellipse
    {
        private void AssignEllipseCenter(Vector3 value)
        {
            bool changed = !SameEllipseGeometryBits(this.center.X, value.X) ||
                !SameEllipseGeometryBits(this.center.Y, value.Y) || !SameEllipseGeometryBits(this.center.Z, value.Z);
            // Always assign the complete struct, including its normalization-cache flag.
            this.center = value;
            if (changed) this.ClearProxyGraphics();
        }

        private void AssignEllipseScalar(ref double field, double value)
        {
            bool changed = !SameEllipseGeometryBits(field, value);
            field = value;
            if (changed) this.ClearProxyGraphics();
        }

        private static bool SameEllipseGeometryBits(double first, double second)
        {
            return BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);
        }
    }
}
