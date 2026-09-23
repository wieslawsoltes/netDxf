// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Entities
{
    public static partial class DimensionBlock
    {
        private static void AddExtensionLine(List<EntityObject> entities, Vector2 origin,
            Vector2 dimensionPoint, Vector2 legacyStart, Vector2 legacyEnd,
            DimensionStyle style, Linetype linetype)
        {
            if (!style.ExtLineFixed)
            {
                entities.Add(ExtensionLine(legacyStart, legacyEnd, style, linetype));
                return;
            }

            // DIMFXL measures below the dimension line; DIMEXE is independent.
            // DIMEXO remains a minimum gap, even when it shortens the fixed part.
            double length = style.ExtLineFixedLength * style.DimScaleOverall;
            double offset = style.ExtLineOffset * style.DimScaleOverall;
            double extend = style.ExtLineExtend * style.DimScaleOverall;
            if (!FiniteTextValue(length) || length < 0.0 || !FiniteTextValue(offset) || offset < 0.0 ||
                !FiniteTextValue(extend) || extend < 0.0)
                throw new ArgumentOutOfRangeException(nameof(style), "Extension line lengths must be finite and nonnegative.");

            Vector2 delta = dimensionPoint - origin;
            double distance;
            Vector2 direction = ExtensionDirection(delta, out distance);
            if (distance == 0.0)
            {
                double unused;
                direction = ExtensionDirection(legacyEnd - dimensionPoint, out unused);
                if (unused == 0.0) return; // No direction or visible segment is defined.
            }
            double endDistance = distance + extend;
            double startDistance = Math.Max(offset, distance - length);
            if (!FiniteTextValue(endDistance) || !FiniteTextValue(startDistance))
                throw new ArgumentOutOfRangeException(nameof(style), "Extension line placement overflowed.");
            if (startDistance >= endDistance) return; // The required gap consumes the segment.

            Vector2 start = startDistance == offset ? origin + offset * direction
                : dimensionPoint - length * direction;
            Vector2 end = dimensionPoint + extend * direction;
            if (!FiniteTextValue(start.X) || !FiniteTextValue(start.Y) ||
                !FiniteTextValue(end.X) || !FiniteTextValue(end.Y))
                throw new ArgumentOutOfRangeException(nameof(style), "Extension line endpoints must be finite.");
            entities.Add(ExtensionLine(start, end, style, linetype));
        }

        private static Vector2 ExtensionDirection(Vector2 delta, out double distance)
        {
            double maximum = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
            if (!FiniteTextValue(maximum))
                throw new ArgumentException("Extension line geometry must be finite.", nameof(delta));
            if (maximum == 0.0) { distance = 0.0; return Vector2.Zero; }
            double x = delta.X / maximum, y = delta.Y / maximum;
            double normalizedLength = Math.Sqrt(x * x + y * y);
            distance = maximum * normalizedLength;
            if (!FiniteTextValue(distance))
                throw new ArgumentException("Extension line length overflowed.", nameof(delta));
            // Length admission is exact, not a test against the global geometry epsilon.
            return new Vector2(x / normalizedLength, y / normalizedLength);
        }
    }
}
