// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    // Keep assignment semantics (including Vector2's normalization flag), while
    // deciding cache invalidation only from the stored geometry components.
    internal static class PlanarGeometryAssignment
    {
        internal static bool Assign(ref double target, double value)
        {
            bool changed = !Same(target, value);
            target = value;
            return changed;
        }

        internal static bool Assign(ref Vector2 target, Vector2 value)
        {
            bool changed = !Same(target.X, value.X) || !Same(target.Y, value.Y);
            target = value;
            return changed;
        }

        private static bool Same(double a, double b)
        { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }
    }
}
