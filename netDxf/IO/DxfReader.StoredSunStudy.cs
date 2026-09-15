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
        private readonly List<DxfStoredSunStudy> storedSunStudies = new List<DxfStoredSunStudy>();
        private DatabaseRecord ReadStoredSunStudyRecord(List<DxfTag> tags)
        {
            DatabaseRecord result = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int depth = 0, end = tags.Count;
            for (int i = start; i < tags.Count; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 102) { string text = (string)tag.Value; if (text.StartsWith("{", StringComparison.Ordinal)) depth++; else if (text == "}" && depth > 0) depth--; }
                else if (depth == 0 && tag.Code == 1001) { end = i; break; }
            }
            List<DxfTag> body = tags.GetRange(start, end - start);
            DxfVersion profile = this.doc.DrawingVariables.AcadVer;
            bool known = (profile == DxfVersion.AutoCad2013 || profile == DxfVersion.AutoCad2018) && opaque.Count == 0 && IsStoredSunStudyShape(body);
            if (known)
            {
                var study = new DxfStoredSunStudy(this.doc, body, this.DecodeEncodedNonAsciiCharacters) { Handle = handle };
                result.Object = study; this.storedSunStudies.Add(study);
                if (end < tags.Count) this.ReadDatabaseXData(study, tags, end);
            }
            else { opaque.AddRange(tags.Skip(start)); result.Object = new DxfOpaqueObject("SUNSTUDY", opaque) { Handle = handle }; }
            return result;
        }
        private static bool IsStoredSunStudyShape(IList<DxfTag> body)
        {
            short[] prefix = { 100,90,1,2,70,3,290,4,291,91,292,93,94,95,73 };
            short[] suffix = { 340,341,342,74,75,76,77,40,293,294,343 };
            if (body.Count < prefix.Length || !body.Take(prefix.Length).Select(tag => tag.Code).SequenceEqual(prefix)
                || (string)body[0].Value != "AcDbSunStudy" || (int)body[1].Value != 0 || (int)body[9].Value != 0) return false;
            int hours = (short)body[14].Value, i = prefix.Length;
            while (i < body.Count && body[i].Code == 290) i++;
            // Private or unfamiliar tails remain complete opaque records, including any extended values.
            if (!body.Skip(i).Select(tag => tag.Code).SequenceEqual(suffix)) return false;
            if (hours < 0 || hours != i - prefix.Length) throw new FormatException("SUNSTUDY hour count differs from its ordered group-290 flags.");
            double spacing = (double)body[i + 7].Value;
            if (double.IsNaN(spacing) || double.IsInfinity(spacing)) throw new FormatException("SUNSTUDY spacing must be finite.");
            return true;
        }
        private void ResolveStoredSunStudyReferences()
        { foreach (DxfStoredSunStudy study in this.storedSunStudies) study.Resolve(handle => this.GetObjectBySourceHandle(handle)); }
    }
}
