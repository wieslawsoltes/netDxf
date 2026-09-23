// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    // Stored geometry bits, not approximate geometric equality, determine whether
    // previously attached common proxy graphics can still describe the entity.
    internal static class PrimitiveGeometryMutation
    {
        internal static void Assign(EntityObject entity, ref double field, double value)
        {
            bool changed = Bits(field) != Bits(value);
            field = value;
            if (changed) entity.ClearProxyGraphics();
        }

        internal static void Assign(EntityObject entity, ref Vector3 field, Vector3 value)
        {
            bool changed = Bits(field.X) != Bits(value.X) || Bits(field.Y) != Bits(value.Y) || Bits(field.Z) != Bits(value.Z);
            // A coordinate-identical assignment still transfers the complete struct.
            field = value;
            if (changed) entity.ClearProxyGraphics();
        }

        private static long Bits(double value) { return BitConverter.DoubleToInt64Bits(value); }
    }
}
