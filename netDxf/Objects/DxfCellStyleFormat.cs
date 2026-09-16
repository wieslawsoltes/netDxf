// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Objects
{
    /// <summary>An immutable projection of a qualified stored TABLEFORMAT packet.</summary>
    /// <remarks>Structural flags, frame presence and border order stay fixed. Unknown or extended packet shapes remain available only through the entry's FormatPayload.</remarks>
    public sealed class DxfCellStyleFormat
    {
        private DxfCellStyleFormat() { }
        internal List<DxfTag> HeaderTags { get; private set; }
        internal List<DxfTag> MarginTags { get; private set; }
        /// <summary>Gets the retained table-format type code.</summary>
        public int StoredType { get; private set; }
        /// <summary>Gets the flags controlling the presence of format data.</summary>
        public short StoredDataFlags { get; private set; }
        /// <summary>Gets non-structural table-format values, or null when no data is present.</summary>
        public DxfCellStyleFormatValues Values { get; private set; }
        /// <summary>Gets content formatting, or null when no data is present.</summary>
        public DxfCellContentFormat Content { get; private set; }
        /// <summary>Gets retained margin-presence flags, or null when no data is present.</summary>
        public short? StoredMarginFlags { get; private set; }
        /// <summary>Gets the six stored margins, or null when the margin frame is absent.</summary>
        public DxfCellMargins Margins { get; private set; }
        /// <summary>Gets complete grid-format packets in source order, without assigning edge roles.</summary>
        public IReadOnlyList<DxfCellGridFormat> Borders { get; private set; }
        /// <summary>Creates an immutable edit bound to this format snapshot.</summary>
        public DxfCellStyleFormatEdit Edit() { return new DxfCellStyleFormatEdit(this); }

        internal static DxfCellStyleFormat TryRead(IList<DxfTag> tags, Func<string, string> decode)
        {
            try
            {
                var read = new Cursor(tags);
                read.Marker(1, "TABLEFORMAT_BEGIN");
                var result = new DxfCellStyleFormat { StoredType = (int)read.Next(90).Value,
                    StoredDataFlags = (short)read.Next(170).Value, Borders = new List<DxfCellGridFormat>().AsReadOnly() };
                if (result.StoredDataFlags != 0)
                {
                    result.HeaderTags = read.Fields(91, 92, 62, 93);
                    var h = result.HeaderTags;
                    result.Values = new DxfCellStyleFormatValues((int)h[0].Value, (int)h[1].Value, (short)h[2].Value, (int)h[3].Value);
                    read.Marker(300, "CONTENTFORMAT"); read.Marker(1, "CONTENTFORMAT_BEGIN");
                    var c = read.Fields(90, 91, 92, 93, 300, 40, 140, 94, 62, 340, 144);
                    result.Content = new DxfCellContentFormat(c, new DxfCellContentFormatValues(
                        (int)c[0].Value, (int)c[1].Value, (int)c[2].Value, (int)c[3].Value,
                        decode((string)c[4].Value), (double)c[5].Value, (double)c[6].Value,
                        (int)c[7].Value, (short)c[8].Value, (double)c[10].Value));
                    read.Marker(309, "CONTENTFORMAT_END");
                    result.StoredMarginFlags = (short)read.Next(171).Value;
                    if (result.StoredMarginFlags != 0)
                    {
                        read.Marker(301, "MARGIN"); read.Marker(1, "CELLMARGIN_BEGIN");
                        var m = read.Fields(40, 40, 40, 40, 40, 40); result.MarginTags = m;
                        result.Margins = new DxfCellMargins((double)m[0].Value, (double)m[1].Value, (double)m[2].Value,
                            (double)m[3].Value, (double)m[4].Value, (double)m[5].Value);
                        read.Marker(309, "CELLMARGIN_END");
                    }
                    int count = (int)read.Next(94).Value;
                    if (count < 0 || count > 6) return null;
                    var borders = new List<DxfCellGridFormat>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int mask = (int)read.Next(95).Value;
                        if (mask == 0) return null;
                        read.Marker(302, "GRIDFORMAT"); read.Marker(1, "GRIDFORMAT_BEGIN");
                        var b = read.Fields(90, 91, 62, 92, 340, 93, 40);
                        borders.Add(new DxfCellGridFormat(mask, b, new DxfCellGridFormatValues(
                            (int)b[0].Value, (int)b[1].Value, (short)b[2].Value,
                            (int)b[3].Value, (int)b[5].Value, (double)b[6].Value)));
                        read.Marker(309, "GRIDFORMAT_END");
                    }
                    result.Borders = borders.AsReadOnly();
                }
                read.Marker(309, "TABLEFORMAT_END");
                return read.AtEnd ? result : null;
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }
        }

        internal void Bind(IReadOnlyDictionary<string, DxfObject> handles)
        {
            if (this.Content != null) this.Content.Bind(handles);
            foreach (var border in this.Borders) border.Bind(handles);
        }

        private sealed class Cursor
        {
            private readonly IList<DxfTag> tags;
            private int index;
            internal Cursor(IList<DxfTag> tags) { this.tags = tags; }
            internal bool AtEnd { get { return this.index == this.tags.Count; } }
            internal DxfTag Next(short code)
            {
                if (this.index >= this.tags.Count || this.tags[this.index].Code != code) throw new FormatException("Unqualified cell format framing.");
                return this.tags[this.index++];
            }
            internal void Marker(short code, string value)
            { if ((string)this.Next(code).Value != value) throw new FormatException("Unqualified cell format marker."); }
            internal List<DxfTag> Fields(params short[] codes) { return codes.Select(this.Next).ToList(); }
        }
    }

    /// <summary>One immutable stored CONTENTFORMAT projection with its actual resource identity.</summary>
    public sealed class DxfCellContentFormat
    {
        internal DxfCellContentFormat(List<DxfTag> tags, DxfCellContentFormatValues values) { this.Tags = tags; this.Values = values; }
        internal List<DxfTag> Tags { get; }
        /// <summary>Gets stored content values; the format expression is decoded but not evaluated.</summary>
        public DxfCellContentFormatValues Values { get; }
        /// <summary>Gets the original stored STYLE handle, including null handle spelling.</summary>
        public string StoredTextStyleHandle { get { return (string)this.Tags[9].Value; } }
        /// <summary>Gets the exact bound TextStyle, or null for a null or differently typed target.</summary>
        public TextStyle TextStyle { get; private set; }
        internal void Bind(IReadOnlyDictionary<string, DxfObject> handles)
        { this.TextStyle = handles.TryGetValue(this.StoredTextStyleHandle, out DxfObject target) ? target as TextStyle : null; }
    }

    /// <summary>One immutable GRIDFORMAT projection with its stored index mask and resource identity.</summary>
    public sealed class DxfCellGridFormat
    {
        internal DxfCellGridFormat(int mask, List<DxfTag> tags, DxfCellGridFormatValues values)
        { this.StoredIndexMask = mask; this.Tags = tags; this.Values = values; }
        internal List<DxfTag> Tags { get; }
        /// <summary>Gets the retained index mask without assigning edge roles.</summary>
        public int StoredIndexMask { get; }
        /// <summary>Gets stored grid values; visibility and lineweight codes are not normalized.</summary>
        public DxfCellGridFormatValues Values { get; }
        /// <summary>Gets the original stored LTYPE handle, including null handle spelling.</summary>
        public string StoredLinetypeHandle { get { return (string)this.Tags[4].Value; } }
        /// <summary>Gets the exact bound Linetype, or null for a null or differently typed target.</summary>
        public Linetype Linetype { get; private set; }
        /// <summary>Creates an immutable edit bound to this grid snapshot.</summary>
        public DxfCellGridFormatEdit Edit() { return new DxfCellGridFormatEdit(this); }
        internal void Bind(IReadOnlyDictionary<string, DxfObject> handles)
        { this.Linetype = handles.TryGetValue(this.StoredLinetypeHandle, out DxfObject target) ? target as Linetype : null; }
    }
}
