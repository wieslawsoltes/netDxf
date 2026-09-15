// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        // Native roundtrip records can carry private extended-code payload without
        // an APPID envelope. Retain those packets without widening authored XRECORD data.
        private bool TryReadPrivateXRecord(List<DxfTag> tags, out DatabaseRecord result)
        {
            result = null;
            int depth = 0, start = -1;
            for (int i = 0; i < tags.Count; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 102)
                {
                    string value = (string)tag.Value;
                    if (value.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (value == "}" && depth > 0) depth--;
                }
                else if (depth == 0 && (tag.Code == 100 || tag.Code == 1001))
                {
                    if (tag.Code == 100 && (string)tag.Value == "AcDbXrecord") start = i;
                    break;
                }
            }
            if (start < 0) return false;
            bool unsupported = false;
            int end = tags.Count;
            depth = 0;
            for (int i = start + 1; i < tags.Count; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 102)
                {
                    string value = (string)tag.Value;
                    if (value.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (value == "}" && depth > 0) depth--;
                }
                if (tag.Code == 1001 && depth == 0) { end = i; break; }
                if (tag.Code >= 1000) unsupported = true;
            }
            if (!unsupported) return false;
            result = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int payload, out string handle);
            opaque.AddRange(tags.Skip(payload).Take(end - payload));
            result.Object = new DxfOpaqueObject("XRECORD", opaque) { Handle = handle };
            if (end < tags.Count) this.ReadDatabaseXData(result.Object, tags, end);
            return true;
        }
    }
}
