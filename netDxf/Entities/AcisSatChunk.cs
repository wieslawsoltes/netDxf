// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    /// <summary>An immutable encoded SAT text chunk: group 1 starts a line and group 3 continues it.</summary>
    /// <remarks>This represents DXF framing, not an interpreted ACIS record.</remarks>
    public sealed class AcisSatChunk
    {
        /// <summary>Creates a chunk containing at most 255 printable ASCII characters.</summary>
        public AcisSatChunk(short groupCode, string text)
        {
            if (groupCode != 1 && groupCode != 3) throw new ArgumentOutOfRangeException(nameof(groupCode));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > 255) throw new ArgumentOutOfRangeException(nameof(text), "SAT chunks contain at most 255 characters.");
            AcisEntity.ValidateAscii(text, nameof(text));
            this.GroupCode = groupCode;
            this.Text = text;
        }
        /// <summary>Gets the framing group, 1 or 3.</summary>
        public short GroupCode { get; }
        /// <summary>Gets the exact encoded text, including leading and trailing spaces.</summary>
        public string Text { get; }
    }
}
