// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Entities
{
    public sealed partial class AttributeMText
    {
        /// <summary>Returns independent ordered scalar tags, retaining absent fields and orientation precedence.</summary>
        /// <remarks>Logical text and style are separate properties. Tags are immutable values.</remarks>
        public IReadOnlyList<DxfTag> GetStoredFields() => new List<DxfTag>(this.Fields).AsReadOnly();

        /// <summary>Constructs exact embedded content from ordered supported scalar tags, logical text and a style.</summary>
        /// <remarks>The content is detached. The style reference is retained until an attribute adopts a copy.</remarks>
        public static AttributeMText FromStoredFields(string value, TextStyle style, IEnumerable<DxfTag> fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var result = new AttributeMText(true) { Value = value, Style = style };
            foreach (DxfTag tag in fields)
            {
                if (tag == null || result.fields.ContainsKey(tag.Code)) throw new ArgumentException("Embedded scalar fields must be non-null and unique.", nameof(fields));
                if (result.fields.Count >= 128) throw new ArgumentException("Embedded scalar field budget exceeded.", nameof(fields));
                result.Set(tag.Code, tag.Value);
            }
            result.Validate(); return result;
        }
    }
}
