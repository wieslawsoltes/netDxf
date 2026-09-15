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
        private readonly List<DxfStoredSectionManager> storedSectionManagers = new List<DxfStoredSectionManager>();

        private DatabaseRecord ReadSectionManagerRecord(string codeName, List<DxfTag> tags)
        {
            if (this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007 && tags.All(tag => tag.Code != 100))
                throw new FormatException("SECTION_MANAGER requires its subclass marker.");
            DatabaseRecord record = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int end = tags.FindIndex(start, tag => tag.Code == 1001);
            if (end < 0) end = tags.Count;
            List<DxfTag> body = tags.GetRange(start, end - start);
            bool unknown = this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2007 || opaque.Count != 0
                || body.Any(tag => tag.Code == 102 || tag.Code == 100 && (string)tag.Value != "AcDbSectionManager"
                    || tag.Code != 100 && tag.Code != 70 && tag.Code != 90 && tag.Code != 330 && tag.Code < 1000)
                || body.Any(tag => tag.Code == 70 && ((short)tag.Value < 0 || (short)tag.Value > 1));
            if (unknown)
            {
                opaque.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject(codeName, opaque) { Handle = handle };
                return record;
            }
            if (body.Count < 3 || body[0].Code != 100 || (string)body[0].Value != "AcDbSectionManager"
                || body[1].Code != 70 || body[2].Code != 90 || body.Skip(3).Any(tag => tag.Code != 330))
                throw new FormatException("SECTION_MANAGER requires one ordered subclass, update flag, count and section-pointer list.");
            int count = (int)body[2].Value;
            if (count < 0 || count > DxfStoredSectionManager.MaximumSections || body.Count - 3 != count)
                throw new FormatException("SECTION_MANAGER section count does not match its bounded pointer list.");
            if (record.Metadata.Reactors.Select(value => Convert.ToUInt64(value, 16)).Distinct().Count() != record.Metadata.Reactors.Count)
                throw new FormatException("SECTION_MANAGER repeats a persistent-reactor identity.");
            var manager = new DxfStoredSectionManager(this.doc, codeName, body, (short)body[1].Value != 0,
                body.Skip(3).Select(tag => (string)tag.Value)) { Handle = handle };
            record.Object = manager;
            if (end < tags.Count) this.ReadDatabaseXData(manager, tags, end);
            this.storedSectionManagers.Add(manager);
            return record;
        }

        private void ResolveSectionManagerReferences()
        {
            foreach (DxfStoredSectionManager manager in this.storedSectionManagers)
                manager.Resolve(handle => this.GetObjectBySourceHandle(handle));
        }
    }
}
