// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    internal partial class DxfReader
    {
        private sealed class AttributeTextReadState
        {
            internal bool InAttribute, SeenTag, IsDefinition;
            internal int AlignmentMask;
            internal Vector3 Alignment;
            internal readonly AttributeTextState State = new AttributeTextState();
            internal AttributeTextReadState(bool definition) { this.IsDefinition = definition; }
            internal AttributeTextState Finish()
            {
                if (AlignmentMask != 0 && AlignmentMask != 7) throw new InvalidDataException("Incomplete secondary attribute alignment.");
                if (AlignmentMask != 0) State.SecondaryAlignment = Alignment;
                if (State.Content != null && State.Kind != (IsDefinition ? (short)4 : (short)2))
                    throw new InvalidDataException("Embedded content and attribute type disagree.");
                if (State.Content == null && (State.Kind == 2 || State.Kind == 4))
                    throw new InvalidDataException("Multiline attribute type requires embedded content.");
                return State.Version.HasValue || State.Locked.HasValue || State.Kind.HasValue || State.FieldLength.HasValue || State.Content != null || State.Auxiliary.Count != 0 ? State : null;
            }
        }
        private bool TryReadAttributeText(AttributeTextReadState scope)
        {
            short code = this.chunk.Code; AttributeTextState state = scope.State;
            if (code == 100)
            {
                string name = this.chunk.ReadString();
                if (name == (scope.IsDefinition ? SubclassMarker.AttributeDefinition : SubclassMarker.Attribute))
                { scope.InAttribute = true; this.chunk.Next(); return true; }
                if (name == "AcDbXrecord")
                {
                    if (!scope.InAttribute || state.Auxiliary.Count != 0) throw new InvalidDataException("Invalid attribute auxiliary subclass.");
                    this.chunk.Next();
                    while (this.chunk.Code != 0 && this.chunk.Code != 101 && this.chunk.Code != 1001)
                    {
                        code = this.chunk.Code;
                        if (code == 100) throw new NotSupportedException("Unknown attribute subclass after AcDbXrecord.");
                        if (code == 5 || code == 105 || (code >= 320 && code <= 369) || (code >= 390 && code <= 399) || code == 480 || code == 481)
                            throw new NotSupportedException("Pointer-bearing attribute auxiliary records require explicit graph mapping.");
                        object v = this.chunk.Value;
                        if (v is string text) v = this.DecodeEncodedNonAsciiCharacters(text);
                        state.Auxiliary.Add(new DxfTag(code, v));
                        if (state.Auxiliary.Count > 4096) throw new InvalidDataException("Attribute auxiliary tag budget exceeded.");
                        this.chunk.Next();
                    }
                    return true;
                }
                return false;
            }
            if (code == 101)
            {
                if (!scope.InAttribute || state.Content != null || this.chunk.ReadString() != "Embedded Object")
                    throw new InvalidDataException("Invalid or duplicate embedded attribute boundary.");
                var tags = new List<DxfTag>(); int textLength = 0; this.chunk.Next();
                while (this.chunk.Code != 0 && this.chunk.Code != 1001)
                {
                    code = this.chunk.Code;
                    object v = this.chunk.Value;
                    if (v is string s)
                    {
                        textLength = checked(textLength + s.Length);
                        if (textLength > 16777216) throw new InvalidDataException("Embedded attribute text budget exceeded.");
                        if (code != 1 && code != 3) v = this.DecodeEncodedNonAsciiCharacters(s);
                    }
                    tags.Add(new DxfTag(code, v));
                    if (tags.Count > 131072) throw new InvalidDataException("Embedded attribute tag budget exceeded.");
                    this.chunk.Next();
                }
                state.Content = AttributeMText.Read(tags, this.GetTextStyle);
                state.Content.Value = this.DecodeEncodedNonAsciiCharacters(state.Content.Value);
                return true;
            }
            if (!scope.InAttribute) return false;
            switch (code)
            {
                case 2: scope.SeenTag = true; return false;
                case 280:
                    // DXF 280 is an Int16 in both text and binary readers, not a boxed byte.
                    short v = this.chunk.ReadShort();
                    if (!scope.SeenTag && !state.Version.HasValue)
                    { if (v != 0) throw new NotSupportedException("Unknown attribute version."); state.Version = v; }
                    else
                    { if (state.Locked.HasValue || (v != 0 && v != 1)) throw new InvalidDataException("Invalid attribute lock flag."); state.Locked = v == 1; }
                    break;
                case 71:
                    if (state.Kind.HasValue) throw new InvalidDataException("Duplicate attribute type.");
                    short kind = this.chunk.ReadShort();
                    if (kind != 0 && kind != 1 && kind != 2 && kind != 4) throw new NotSupportedException("Unknown attribute type.");
                    state.Kind = kind; break;
                case 72:
                    if (!state.Kind.HasValue) return false;
                    if (state.SecondaryFlag.HasValue) throw new InvalidDataException("Duplicate secondary attribute flag.");
                    state.SecondaryFlag = this.chunk.ReadShort(); break;
                case 11:
                case 21:
                case 31:
                    if (!state.Kind.HasValue) return false;
                    int index = (code - 11) / 10;
                    if ((scope.AlignmentMask & (1 << index)) != 0) throw new InvalidDataException("Duplicate secondary alignment coordinate.");
                    double n = this.chunk.ReadDouble(); Insert.FiniteInsert(n); scope.Alignment[index] = n; scope.AlignmentMask |= 1 << index; break;
                case 73:
                    if (state.FieldLength.HasValue) throw new InvalidDataException("Duplicate attribute field length.");
                    state.FieldLength = this.chunk.ReadShort(); break;
                default: return false;
            }
            this.chunk.Next(); return true;
        }
    }
    internal partial class DxfWriter
    {
        private void ValidateAttributeText(DxfObject host, AttributeTextState state)
        {
            if (state == null) return;
            DxfVersion version = this.doc.DrawingVariables.AcadVer;
            if ((state.Version.HasValue || state.Locked.HasValue) && version < DxfVersion.AutoCad2010)
                throw new NotSupportedException("Stored attribute version/lock data requires the 2010 writer profile.");
            if ((state.Kind.HasValue || state.Content != null || state.Auxiliary.Count != 0) && version < DxfVersion.AutoCad2018)
                throw new NotSupportedException("Embedded/auxiliary attribute data requires DXF 2018. Remove it explicitly before down-saving.");
            foreach (DxfTag tag in state.Auxiliary) if (tag.Value is string s) this.ValidateEntityTextString(s, "Attribute auxiliary field");
            if (state.Content == null) return;
            AttributeMText text = state.Content; text.Validate();
            if (!ReferenceEquals(text.Style.Owner, this.doc.TextStyles) || !ReferenceEquals(text.Style, this.doc.TextStyles[text.Style.Name]))
                throw new InvalidDataException("Embedded attribute text style is not a canonical document resource.");
            this.ValidateEntityTextString(text.Value, "Embedded attribute MTEXT");
            foreach (DxfTag tag in text.Fields) if (tag.Value is string s) this.ValidateEntityTextString(s, "Embedded attribute field");
        }
        private void WriteAttributeTextVersion(AttributeTextState state)
        {
            if (state?.Version != null || state?.Content != null) this.chunk.Write(280, state.Version ?? (short)0);
        }
        private void WriteAttributeTextTail(AttributeTextState state)
        {
            if (state == null) return;
            if (state.FieldLength.HasValue) this.chunk.Write(73, state.FieldLength.Value);
            if (state.Locked.HasValue) this.chunk.Write(280, state.Locked.Value ? (short)1 : (short)0);
            if (state.Kind.HasValue) this.chunk.Write(71, state.Kind.Value);
            if (state.SecondaryFlag.HasValue) this.chunk.Write(72, state.SecondaryFlag.Value);
            if (state.SecondaryAlignment.HasValue)
            { Vector3 p = state.SecondaryAlignment.Value; this.chunk.Write(11, p.X); this.chunk.Write(21, p.Y); this.chunk.Write(31, p.Z); }
            if (state.Auxiliary.Count != 0)
            {
                this.chunk.Write(100, "AcDbXrecord");
                foreach (DxfTag tag in state.Auxiliary) this.chunk.Write(tag.Code, tag.Value is string s ? this.EncodeNonAsciiCharacters(s) : tag.Value);
            }
            if (state.Content == null) return;
            AttributeMText text = state.Content;
            this.chunk.Write(101, "Embedded Object");
            foreach (DxfTag tag in text.Fields) this.chunk.Write(tag.Code, tag.Value is string s ? this.EncodeNonAsciiCharacters(s) : tag.Value);
            this.WriteMTextChunks(this.EncodeNonAsciiCharacters(text.Value));
            this.chunk.Write(7, this.EncodeNonAsciiCharacters(text.Style.Name));
        }
    }
}
