// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.IO;
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        // Run before any output preparation can add layouts, registries or
        // generated entities. Registered nested blocks are visited once through
        // the block table; following INSERT graphs recursively is unnecessary.
        private void ValidateEntityTextStrings()
        {
            foreach (Block block in this.doc.Blocks)
            {
                foreach (AttributeDefinition definition in block.AttributeDefinitions.Values)
                {
                    this.ValidateEntityTextString(definition.Value, "ATTDEF value");
                    this.ValidateEntityTextString(definition.Prompt, "ATTDEF prompt");
                }
                foreach (EntityObject entity in block.Entities)
                {
                    if (entity is Text text)
                        this.ValidateEntityTextString(text.Value, "TEXT value");
                    else if (entity is MText mtext)
                        this.ValidateEntityTextString(mtext.Value, "MTEXT value");
                    else if (entity is Insert insert)
                    {
                        foreach (netDxf.Entities.Attribute attribute in insert.Attributes)
                            this.ValidateEntityTextString(attribute.Value, "ATTRIB value");
                    }
                    else if (entity is Dimension dimension)
                        this.ValidateEntityTextString(dimension.UserText, "DIMENSION user text");
                }
            }
        }

        private void ValidateEntityTextString(string value, string field)
        {
            if (value == null) return;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\0' || (!this.isBinary && (c == '\r' || c == '\n')))
                    throw new InvalidDataException(field + " contains a character that breaks DXF string framing.");
                if (!char.IsSurrogate(c)) continue;
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }
                throw new InvalidDataException(field + " contains an unpaired UTF-16 surrogate.");
            }
        }
    }
}
