// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace netDxf.IO
{
    internal partial class DxfReader
    {
        private AttributeMText ReadAttributeMText(TextStyle fallback)
        {
            if (this.chunk.ReadString() != "Embedded Object")
                throw new NotSupportedException("Unknown attribute embedded object marker.");
            var fields = new List<DxfTag>();
            var content = new StringBuilder();
            bool final = false;
            TextStyle style = fallback;
            this.chunk.Next();
            while (this.chunk.Code != 0 && this.chunk.Code != 1001)
            {
                short code = this.chunk.Code;
                if (code == 999) { this.chunk.Next(); continue; }
                if (code == 1 || code == 3)
                {
                    if (final) throw new FormatException("Attribute embedded content has duplicate or out-of-order final chunks.");
                    content.Append(this.chunk.ReadString());
                    if (code == 1) { final = true; fields.Add(new DxfTag(1, "")); }
                }
                else
                {
                    if (!AttributeMText.IsSupportedCode(code))
                        throw new NotSupportedException("Unsupported attribute embedded MTEXT group " + code);
                    object value;
                    if (code == 7 || code == 431)
                    {
                        value = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                        if (code == 7) style = this.GetTextStyle((string)value);
                    }
                    else if (code == 90 || code == 421 || code == 441) value = this.chunk.ReadInt();
                    else if (code == 63 || code == 71 || code == 72 || code == 73) value = this.chunk.ReadShort();
                    else value = this.chunk.ReadDouble();
                    fields.Add(new DxfTag(code, value));
                }
                this.chunk.Next();
            }
            if (!final) throw new FormatException("Attribute embedded MTEXT is missing its final content chunk.");
            string logical = this.DecodeEncodedNonAsciiCharacters(content.ToString());
            return new AttributeMText(fields.Select(t => t.Code == 1 ? new DxfTag(1, logical) : t), style);
        }
    }

    internal partial class DxfWriter
    {
        private void WriteAttributeMText(AttributeMText text, bool definition)
        {
            if (text == null) return;
            if (this.doc.DrawingVariables.AcadVer != DxfVersion.AutoCad2018)
                throw new NotSupportedException("Multiline ATTRIB/ATTDEF output requires AutoCAD 2018 DXF.");
            this.chunk.Write(71, (short)(definition ? 4 : 2));
            this.chunk.Write(72, (short)0);
            this.chunk.Write(101, "Embedded Object");
            bool styleWritten = false;
            foreach (DxfTag tag in text.Tags)
            {
                if (tag.Code == 1) this.WriteMTextChunks(text.Value);
                else if (tag.Code == 7)
                { this.chunk.Write(7, this.EncodeNonAsciiCharacters(text.Style.Name)); styleWritten = true; }
                else if (tag.Code == 431) this.chunk.Write(431, this.EncodeNonAsciiCharacters((string)tag.Value));
                else this.WriteByCode(tag.Code, tag.Value);
            }
            if (!styleWritten) this.chunk.Write(7, this.EncodeNonAsciiCharacters(text.Style.Name));
        }

        private void ValidateAttributeMText()
        {
            foreach (var entry in this.doc.EmbeddedAttributeTextEntries())
            {
                if (this.doc.DrawingVariables.AcadVer != DxfVersion.AutoCad2018)
                    throw new NotSupportedException("Multiline ATTRIB/ATTDEF output requires AutoCAD 2018 DXF.");
                // Read-only validation: writing cannot repair or adopt a foreign style.
                if (!ReferenceEquals(entry.Value.Style.Owner, this.doc.TextStyles)
                    || !ReferenceEquals(this.doc.TextStyles[entry.Value.Style.Name], entry.Value.Style))
                    throw new InvalidOperationException("Embedded attribute text style is not the registered resource.");
                // Reuse ordinary MTEXT framing/style validation by checking all emitted strings
                // here, before the writer opens its transport or publishes bytes.
                foreach (DxfTag tag in entry.Value.Tags)
                    if (tag.Value is string value && (value.IndexOf('\0') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0))
                        throw new InvalidDataException("Embedded MTEXT strings require MTEXT formatting controls, not physical line delimiters.");
            }
        }
    }
}
