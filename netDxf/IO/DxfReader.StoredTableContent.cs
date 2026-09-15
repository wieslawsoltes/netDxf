// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<DxfStoredTableContent> storedTableContents = new List<DxfStoredTableContent>();
        private DatabaseRecord ReadStoredTableContentRecord(List<DxfTag> tags)
        {
            if (tags.Count > DxfStoredTableContent.MaximumPayloadTags) throw new FormatException("TABLECONTENT exceeds the supported storage packet limit.");
            DatabaseRecord record = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int end = tags.FindIndex(start, tag => tag.Code == 1001); if (end < 0) end = tags.Count;
            var body = tags.GetRange(start, end - start);
            if (body.Any(tag => tag.Code >= 1000 && tag.Code <= 1071)) throw new FormatException("TABLECONTENT extended data must begin with an application registry.");
            bool known = this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2004 && opaque.Count == 0
                && body.Any(tag => tag.Code == 100 && DxfStoredTableContent.SubclassNames.Contains((string)tag.Value))
                && body.All(tag => tag.Code != 102 && (tag.Code != 100 || DxfStoredTableContent.SubclassNames.Contains((string)tag.Value)));
            if (known)
            {
                var content = new DxfStoredTableContent(this.doc, body, this.DecodeEncodedNonAsciiCharacters) { Handle = handle };
                record.Object = content;
                if (end < tags.Count) this.ReadDatabaseXData(content, tags, end);
                this.storedTableContents.Add(content);
            }
            else
            {
                opaque.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject("TABLECONTENT", opaque) { Handle = handle };
            }
            return record;
        }
        private void ResolveStoredTableContentReferences()
        {
            foreach (DxfStoredTableContent content in this.storedTableContents) content.Resolve(handle => this.GetObjectBySourceHandle(handle, true));
        }
    }
}
