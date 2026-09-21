// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Blocks;

namespace netDxf.Entities
{
    public static partial class DimensionBlock
    {
        // Every concrete builder reaches this method, including direct overload calls.
        // Only the newly generated, top-level dimension labels are edited. Text in
        // custom arrow blocks is never traversed or modified.
        private static Block FinishTextBlock(Dimension dim, string name,
            List<EntityObject> entities, Vector2 automaticPosition)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            bool manual = dim.TextPositionManuallySet;
            Vector2 position = manual ? dim.TextReferencePoint : automaticPosition;
            if (!FiniteTextValue(position.X) || !FiniteTextValue(position.Y) ||
                !FiniteTextValue(dim.TextRotation))
            {
                throw new ArgumentException("Dimension text position and rotation must be finite.", nameof(dim));
            }
            if (!FiniteTextValue(dim.LineSpacingFactor) || dim.LineSpacingFactor < 0.25 || dim.LineSpacingFactor > 4.0 ||
                (dim.LineSpacingStyle != MTextLineSpacingStyle.AtLeast && dim.LineSpacingStyle != MTextLineSpacingStyle.Exact))
            {
                throw new ArgumentException("Dimension text line spacing is not representable by MTEXT.", nameof(dim));
            }
            if (manual && ((int)dim.AttachmentPoint < 1 || (int)dim.AttachmentPoint > 9))
            {
                throw new ArgumentException("Dimension text attachment point is invalid.", nameof(dim));
            }

            MText primary = null;
            for (int i = 0; i < entities.Count; i++)
            {
                MText text = entities[i] as MText;
                if (text == null) continue;
                if (manual && primary != null)
                {
                    // A manually placed label has one explicit attachment anchor.
                    // Keep the second \\X text as a paragraph in that same label.
                    primary.Value += "\\P" + text.Value;
                    entities.RemoveAt(i--);
                    continue;
                }
                primary = text;
                if (manual)
                {
                    text.Position = new Vector3(position.X, position.Y, 0.0);
                    text.AttachmentPoint = dim.AttachmentPoint;
                }
                // Public TextRotation is the offset from the default orientation.
                // Do not reassign a zero offset: preserve existing default output.
                if (dim.TextRotation != 0.0) text.Rotation += dim.TextRotation;
                text.LineSpacingStyle = dim.LineSpacingStyle;
                text.LineSpacingFactor = dim.LineSpacingFactor;
            }

            // Construct before publishing an automatic reference point. A failed
            // block construction must not erase an existing manual flag/position.
            Block block = new Block(name, entities, null, false) { Flags = BlockTypeFlags.AnonymousBlock };
            if (!manual)
            {
                dim.TextReferencePoint = automaticPosition;
                dim.TextPositionManuallySet = false;
            }
            return block;
        }

        private static bool FiniteTextValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
