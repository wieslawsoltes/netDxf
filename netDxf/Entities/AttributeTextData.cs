// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.IO;

namespace netDxf.Entities
{
    /// <summary>Detached stored attribute text data, including absent fields and auxiliary tags.</summary>
    /// <remarks>Edit a snapshot and publish it through Attribute.SetTextData or AttributeDefinition.SetTextData.
    /// Publication validates and copies the complete value. Fallback text and geometry are independent.</remarks>
    public sealed class AttributeTextData
    {
        /// <summary>Gets or sets the optional stored version marker; only version zero is supported.</summary>
        public short? Version { get; set; }
        /// <summary>Gets or sets the optional stored type; embedded ATTRIB content uses 2, ATTDEF uses 4.</summary>
        public short? Kind { get; set; }
        /// <summary>Gets or sets the optional secondary group 72, independently of fallback alignment.</summary>
        public short? SecondaryFlag { get; set; }
        /// <summary>Gets or sets the optional legacy field length.</summary>
        public short? FieldLength { get; set; }
        /// <summary>Gets or sets the optional attribute position lock.</summary>
        public bool? Locked { get; set; }
        /// <summary>Gets or sets the optional secondary alignment point.</summary>
        public Vector3? SecondaryAlignment { get; set; }
        /// <summary>Gets or sets embedded text, including ordered scalar layout fields and its independent style.</summary>
        public AttributeMText Content { get; set; }
        /// <summary>Gets ordered auxiliary subclass tags. Boundaries and unmapped handles are unsupported.</summary>
        public IList<DxfTag> Auxiliary { get; } = new List<DxfTag>();

        internal static AttributeTextData FromState(AttributeTextState state, bool cloneStyles)
        {
            if (state == null || !HasData(state)) return null;
            var data = new AttributeTextData { Version = state.Version, Kind = state.Kind,
                SecondaryFlag = state.SecondaryFlag, FieldLength = state.FieldLength, Locked = state.Locked,
                SecondaryAlignment = state.SecondaryAlignment, Content = state.Content?.Copy(cloneStyles) };
            foreach (DxfTag tag in state.Auxiliary) data.Auxiliary.Add(tag);
            return data;
        }

        internal AttributeTextState ToState(bool definition)
        {
            if (this.Version.HasValue && this.Version != 0) throw new ArgumentException("Unknown stored attribute version.");
            if (this.Kind.HasValue && this.Kind != 0 && this.Kind != 1 && this.Kind != 2 && this.Kind != 4)
                throw new ArgumentException("Unknown stored attribute type.");
            if (this.Content != null && this.Kind != (definition ? (short)4 : (short)2)
                || this.Content == null && (this.Kind == 2 || this.Kind == 4))
                throw new ArgumentException("Embedded content and stored attribute type disagree.");
            if ((this.SecondaryFlag.HasValue || this.SecondaryAlignment.HasValue) && !this.Kind.HasValue)
                throw new ArgumentException("Secondary attribute fields require a stored type.");
            if (this.SecondaryAlignment.HasValue) Insert.FiniteInsert(this.SecondaryAlignment.Value);
            this.Content?.Validate();
            var state = new AttributeTextState { Version = this.Version, Kind = this.Kind,
                SecondaryFlag = this.SecondaryFlag, FieldLength = this.FieldLength, Locked = this.Locked,
                SecondaryAlignment = this.SecondaryAlignment, Content = this.Content?.Copy(true) };
            foreach (DxfTag tag in this.Auxiliary)
            {
                if (tag == null || tag.Code == 0 || tag.Code == 5 || tag.Code == 100 || tag.Code == 101 || tag.Code == 105
                    || tag.Code == 1001 || tag.Code == 1005 || (tag.Code >= 320 && tag.Code <= 369) || (tag.Code >= 390 && tag.Code <= 399)
                    || tag.Code == 480 || tag.Code == 481)
                    throw new ArgumentException("Attribute auxiliary data cannot contain record boundaries or mapped handles.");
                if (state.Auxiliary.Count >= 4096) throw new ArgumentException("Attribute auxiliary tag budget exceeded.");
                state.Auxiliary.Add(tag);
            }
            return HasData(state) ? state : null;
        }

        private static bool HasData(AttributeTextState state) => state.Version.HasValue || state.Kind.HasValue
            || state.SecondaryFlag.HasValue || state.FieldLength.HasValue || state.Locked.HasValue
            || state.SecondaryAlignment.HasValue || state.Content != null || state.Auxiliary.Count != 0;
    }
}
