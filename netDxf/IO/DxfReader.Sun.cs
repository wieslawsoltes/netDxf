using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private sealed class SunOwnerContext
        {
            private readonly string marker;
            private bool publicSubclass = true;
            private int controls;
            private bool xdata;
            internal SunOwnerContext(string marker) { this.marker = marker; }
            internal void Observe(short code, object value)
            {
                if (code == 1001) this.xdata = true;
                if (code == 102)
                {
                    string text = (string)value;
                    if (text.StartsWith("{", StringComparison.Ordinal)) this.controls++;
                    else if (text == "}" && this.controls > 0) this.controls--;
                }
                else if (code == 100 && this.controls == 0) this.publicSubclass = (string)value == this.marker;
            }
            internal bool IsPublic { get { return this.publicSubclass && this.controls == 0 && !this.xdata; } }
        }
        private readonly List<Tuple<DxfObject, string>> sunReferences = new List<Tuple<DxfObject, string>>();
        private void AddSunReference(DxfObject owner, string handle)
        {
            if (this.sunReferences.Any(item => ReferenceEquals(item.Item1, owner))) throw new FormatException("Repeated SUN ownership group 361.");
            this.sunReferences.Add(Tuple.Create(owner, handle));
        }
        private DatabaseRecord ReadSunRecord(List<DxfTag> tags)
        {
            var record = new DatabaseRecord();
            string handle = null; bool reactors = false, extension = false;
            var privateHeader = new List<DxfTag>();
            int start = 0;
            for (; start < tags.Count; start++)
            {
                DxfTag tag = tags[start];
                if (tag.Code == 100 || tag.Code == 1001) break;
                if (tag.Code == 5)
                {
                    if (handle != null) throw new FormatException("SUN repeats its identity.");
                    handle = (string)tag.Value;
                }
                else if (tag.Code == 330)
                {
                    if (record.Metadata.Owner != null) throw new FormatException("SUN repeats its owner.");
                    record.Metadata.Owner = (string)tag.Value;
                }
                else if (tag.Code == 102)
                {
                    string group = (string)tag.Value; int begin = start;
                    if (!group.StartsWith("{", StringComparison.Ordinal)) throw new FormatException("Invalid SUN control group.");
                    while (++start < tags.Count && !(tags[start].Code == 102 && (string)tags[start].Value == "}"))
                        if (tags[start].Code == 102 || tags[start].Code == 100 || tags[start].Code == 1001) throw new FormatException("Invalid SUN control framing.");
                    if (start == tags.Count) throw new FormatException("Unterminated SUN control group.");
                    var content = tags.GetRange(begin + 1, start - begin - 1);
                    if (group == "{ACAD_REACTORS")
                    {
                        if (reactors || content.Any(value => value.Code != 330)) throw new FormatException("Invalid SUN reactor group.");
                        reactors = true; record.Metadata.Reactors.AddRange(content.Select(value => (string)value.Value));
                    }
                    else if (group == "{ACAD_XDICTIONARY")
                    {
                        if (extension || content.Count != 1 || content[0].Code != 360) throw new FormatException("Invalid SUN extension group.");
                        extension = true; record.Metadata.Extension = (string)content[0].Value;
                    }
                    else privateHeader.AddRange(tags.GetRange(begin, start - begin + 1));
                }
                else privateHeader.Add(tag);
            }
            if (handle == null) throw new FormatException("SUN requires an identity.");
            if (privateHeader.Count != 0 || !this.ReadSunPayload(record, tags, start))
            {
                privateHeader.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject("SUN", privateHeader);
            }
            record.Object.Handle = handle;
            return record;
        }

        private bool ReadSunPayload(DatabaseRecord record, List<DxfTag> tags, int start)
        {
            if (this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) return false;
            int end = tags.FindIndex(start, tag => tag.Code == 1001);
            if (end < 0) end = tags.Count;
            if (start == end || tags[start].Code != 100 || (string)tags[start].Value != "AcDbSun") return false;
            var codes = new HashSet<short> { 90, 290, 63, 421, 40, 291, 91, 92, 292, 70, 71, 280 };
            for (int j = start + 1; j < end; j++)
            {
                if (tags[j].Code == 100 && (string)tags[j].Value == "AcDbSun") throw new FormatException("SUN repeats its public subclass marker.");
                if (!codes.Contains(tags[j].Code) || tags[j].Code == 90 && (int)tags[j].Value != 1) return false;
            }
            int i = start + 1;
            Func<short, object> take = code =>
            {
                if (i >= end || tags[i].Code != code) throw new FormatException("SUN requires ordered group " + code + ".");
                return tags[i++].Value;
            };
            take(90);
            var sun = new DxfSun();
            try
            {
                sun.Enabled = (bool)take(290);
                sun.ColorIndex = (short)take(63);
                sun.TrueColor = i < end && tags[i].Code == 421 ? (int?)take(421) : null;
                sun.Intensity = (double)take(40);
                sun.ShadowsEnabled = (bool)take(291);
                sun.JulianDay = (int)take(91);
                sun.StoredTime = (int)take(92);
                sun.DaylightSavingTime = (bool)take(292);
                sun.ShadowType = (DxfSunShadowType)(short)take(70);
                sun.ShadowMapSize = (short)take(71);
                short softness = (short)take(280);
                if (softness < 0 || softness > 255) throw new FormatException("SUN shadow softness must fit an unsigned byte.");
                sun.ShadowSoftness = (byte)softness;
            }
            catch (ArgumentException error) { throw new FormatException("Invalid SUN stored value.", error); }
            if (i != end) throw new FormatException("SUN contains trailing or repeated public fields.");
            if (end < tags.Count) this.ReadDatabaseXData(sun, tags, end);
            record.Object = sun;
            return true;
        }
        private void ResolveSunReferences()
        {
            foreach (var pending in this.sunReferences)
            {
                DxfObject owner = pending.Item1;
                if (!ReferenceEquals(this.GetObjectBySourceHandle(owner.Handle), owner)) throw new FormatException("SUN owner was not retained from its source record.");
                SunReferences.CheckProfile(owner, this.doc.DrawingVariables.AcadVer);
                string handle = pending.Item2;
                bool empty = IsNullSourceHandle(handle);
                DxfDatabaseObject sun = empty ? null : this.GetObjectBySourceHandle(handle) as DxfDatabaseObject;
                if (!empty && (sun == null || !(sun is DxfSun || sun is DxfOpaqueObject && sun.CodeName == "SUN") || !ReferenceEquals(sun.Owner, owner)))
                    throw new FormatException("Unresolved, incompatible or nonreciprocal SUN ownership: " + handle);
                SunReferences.Set(owner, sun);
            }
            foreach (DxfSun sun in this.doc.Objects.Items.OfType<DxfSun>())
                if (!ReferenceEquals(SunReferences.Get(sun.Owner), sun)) throw new FormatException("SUN has no reciprocal owner group 361: " + sun.Handle);
        }
        private static bool IsNullSourceHandle(string handle)
        { return ulong.TryParse(handle, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out ulong value) && value == 0; }
    }
}
