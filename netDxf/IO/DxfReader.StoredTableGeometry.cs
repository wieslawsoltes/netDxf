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
        private readonly List<DxfStoredTableGeometry> storedTableGeometries = new List<DxfStoredTableGeometry>();
        private static readonly HashSet<short> TableGeometryCodes = new HashSet<short>(new short[] {
            100, 90, 91, 92, 93, 40, 41, 330, 94, 10, 20, 30, 11, 21, 31, 43, 44, 45, 46, 95
        });
        private DatabaseRecord ReadStoredTableGeometryRecord(List<DxfTag> tags)
        {
            if (tags.Count > DxfStoredTableGeometry.MaximumPayloadTags) throw new FormatException("TABLEGEOMETRY exceeds the supported storage packet limit.");
            DatabaseRecord record = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int end = tags.FindIndex(start, tag => tag.Code == 1001); if (end < 0) end = tags.Count;
            var body = tags.GetRange(start, end - start);
            bool known = this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2004 && opaque.Count == 0
                && body.Any(tag => tag.Code == 100 && (string)tag.Value == "AcDbTableGeometry")
                && body.All(tag => tag.Code != 100 || (string)tag.Value == "AcDbTableGeometry")
                && body.All(tag => TableGeometryCodes.Contains(tag.Code) || (tag.Code >= 1000 && tag.Code <= 1071));
            if (known)
            {
                if (body.Any(tag => tag.Code >= 1000 && tag.Code <= 1071)) throw new FormatException("TABLEGEOMETRY extended data must begin with an application registry.");
                var geometry = new DxfStoredTableGeometry(this.doc, body) { Handle = handle };
                record.Object = geometry;
                if (end < tags.Count) this.ReadDatabaseXData(geometry, tags, end);
                this.storedTableGeometries.Add(geometry);
            }
            else
            {
                opaque.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject("TABLEGEOMETRY", opaque) { Handle = handle };
            }
            return record;
        }
        private void ResolveStoredTableGeometryReferences()
        {
            foreach (DxfStoredTableGeometry geometry in this.storedTableGeometries) geometry.Resolve(handle => this.GetObjectBySourceHandle(handle, true));
        }
    }
}
