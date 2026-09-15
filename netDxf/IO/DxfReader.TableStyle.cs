// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<DxfTableStyle> tableStyles = new List<DxfTableStyle>();
        private DatabaseRecord ReadTableStyleRecord(List<DxfTag> tags)
        {
            DatabaseRecord record = this.ReadStoredObjectHeader(tags, out List<DxfTag> retained, out int start, out string handle);
            int end = tags.FindIndex(start, t => t.Code == 1001);
            if (end < 0) end = tags.Count;
            if (start >= end || tags[start].Code != 100) throw new FormatException("TABLESTYLE requires a subclass payload.");
            if (tags.Skip(start).Take(end - start).Any(t => t.Code >= 1000 && t.Code <= 1071))
                throw new FormatException("TABLESTYLE extended data must begin with an application registry.");
            retained.AddRange(tags.Skip(start).Take(end - start));
            var style = new DxfTableStyle(this.doc, retained, this.DecodeEncodedNonAsciiCharacters) { Handle = handle };
            record.Object = style;
            if (end < tags.Count) this.ReadDatabaseXData(style, tags, end);
            this.tableStyles.Add(style);
            return record;
        }
        private void ResolveTableStyleReferences()
        {
            foreach (DxfTableStyle style in this.tableStyles)
                style.Resolve(handle => this.GetObjectBySourceHandle(handle, true), this.DecodeEncodedNonAsciiCharacters);
        }
    }
}
