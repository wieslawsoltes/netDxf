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
        private readonly List<DxfStoredCellStyleMap> storedCellStyleMaps = new List<DxfStoredCellStyleMap>();
        private DatabaseRecord ReadStoredCellStyleMapRecord(List<DxfTag> tags)
        {
            if (tags.Count > DxfStoredCellStyleMap.MaximumPayloadTags) throw new FormatException("CELLSTYLEMAP exceeds the supported storage packet limit.");
            DatabaseRecord record = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int end = tags.FindIndex(start, tag => tag.Code == 1001); if (end < 0) end = tags.Count;
            var body = tags.GetRange(start, end - start);
            bool known = this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2004 && opaque.Count == 0
                && body.Any(tag => tag.Code == 100 && (string)tag.Value == "AcDbCellStyleMap")
                && body.All(tag => tag.Code != 102 && (tag.Code != 100 || (string)tag.Value == "AcDbCellStyleMap"))
                && body.All(tag => (tag.Code != 1 || DxfStoredCellStyleMap.FrameNames.Any(frame => (string)tag.Value == frame + "_BEGIN"))
                    && (tag.Code != 309 || DxfStoredCellStyleMap.FrameNames.Any(frame => (string)tag.Value == frame + "_END")));
            if (known)
            {
                if (body.Any(tag => tag.Code >= 1000 && tag.Code <= 1071)) throw new FormatException("CELLSTYLEMAP extended data must begin with an application registry.");
                var map = new DxfStoredCellStyleMap(this.doc, body, this.DecodeEncodedNonAsciiCharacters) { Handle = handle };
                record.Object = map;
                if (end < tags.Count) this.ReadDatabaseXData(map, tags, end);
                this.storedCellStyleMaps.Add(map);
            }
            else
            {
                opaque.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject("CELLSTYLEMAP", opaque) { Handle = handle };
            }
            return record;
        }
        private void ResolveStoredCellStyleMapReferences()
        {
            foreach (DxfStoredCellStyleMap map in this.storedCellStyleMaps) map.Resolve(handle => this.GetObjectBySourceHandle(handle, true));
        }
    }
}
