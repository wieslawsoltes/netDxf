using System;
using System.Collections.Generic;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<DxfLightList, List<Tuple<string, string>>>> lightListReferences = new List<Tuple<DxfLightList, List<Tuple<string, string>>>>();
        private bool ReadLightListPayload(DatabaseRecord record, string type, List<DxfTag> tags, int start)
        {
            if (type != "LIGHTLIST" || this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) return false;
            int end = tags.FindIndex(start, t => t.Code == 1001);
            if (end < 0) end = tags.Count;
            if (start == end || tags[start].Code != 100 || (string)tags[start].Value != "AcDbLightList") return false;
            // Preserve private extensions as a complete opaque record. Repeated public markers and
            // malformed public fields still belong to the recognized grammar and are rejected below.
            for (int i = start + 1; i < end; i++)
            {
                short code = tags[i].Code;
                if (code == 100 && (string)tags[i].Value != "AcDbLightList") return false;
                if (code != 100 && code != 90 && code != 5 && code != 1) return false;
            }
            if (end - start < 3 || tags[start + 1].Code != 90 || tags[start + 2].Code != 90)
                throw new FormatException("LIGHTLIST requires version and count group-90 fields.");
            int count = (int)tags[start + 2].Value;
            int available = end - start - 3;
            if (count < 0 || available % 2 != 0 || count != available / 2)
                throw new FormatException("LIGHTLIST count does not match complete LIGHT/name pairs.");
            var value = new DxfLightList((int)tags[start + 1].Value);
            var references = new List<Tuple<string, string>>();
            for (int i = start + 3; i < end; i += 2)
            {
                if (tags[i].Code != 5 || tags[i + 1].Code != 1)
                    throw new FormatException("LIGHTLIST requires ordered group-5 LIGHT/group-1 name pairs.");
                references.Add(Tuple.Create((string)tags[i].Value, this.DecodeEncodedNonAsciiCharacters((string)tags[i + 1].Value)));
            }
            if (end < tags.Count) this.ReadDatabaseXData(value, tags, end);
            record.Object = value;
            this.lightListReferences.Add(Tuple.Create(value, references));
            return true;
        }
        private void ResolveLightListReferences()
        {
            foreach (var pending in this.lightListReferences)
                foreach (var entry in pending.Item2)
                {
                    Light light = this.doc.GetObjectByHandle(entry.Item1) as Light;
                    if (light == null) throw new FormatException("LIGHTLIST reference does not identify a LIGHT: " + entry.Item1);
                    try { pending.Item1.Entries.Add(new DxfLightListEntry(light, entry.Item2)); }
                    catch (ArgumentException error) { throw new FormatException("Invalid LIGHTLIST stored name.", error); }
                }
        }
    }
}
