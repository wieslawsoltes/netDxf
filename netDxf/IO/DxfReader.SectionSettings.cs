using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Blocks;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private sealed class SectionSettingsPrivateVariant : Exception { }
        private sealed class SectionTypeInput
        {
            internal int Type, Options;
            internal string Destination, File;
            internal bool RepeatMarkers;
            internal readonly List<string> Sources = new List<string>();
            internal readonly List<DxfSectionGeometrySettings> Geometry = new List<DxfSectionGeometrySettings>();
        }
        private readonly Dictionary<DxfSectionSettings, List<SectionTypeInput>> pendingSectionSettings = new Dictionary<DxfSectionSettings, List<SectionTypeInput>>();

        private DatabaseRecord ReadSectionSettingsRecord(string codeName, List<DxfTag> tags)
        {
            DatabaseRecord record = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int end = tags.FindIndex(start, tag => tag.Code == 1001);
            if (end < 0) end = tags.Count;
            if (this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007 && (start >= end || tags[start].Code != 100))
                throw new FormatException("SECTIONSETTINGS requires a public subclass marker before its stored fields.");
            bool typed = false;
            if (opaque.Count == 0 && this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007)
            {
                try
                {
                    if (start >= end || tags[start].Code != 100) throw new FormatException("SECTIONSETTINGS requires a public subclass marker.");
                    if ((string)tags[start].Value != "AcDbSectionSettings") throw new SectionSettingsPrivateVariant();
                    int cursor = start + 1;
                    var header = new Dictionary<short, DxfTag>();
                    while (cursor < end && tags[cursor].Code != 1)
                    {
                        DxfTag tag = tags[cursor++];
                        if (tag.Code != 90 && tag.Code != 91) SectionUnexpected(tag);
                        SectionUnique(header, tag);
                    }
                    SectionRequired(header, 90, 91);
                    int count = SectionCount(header[91], DxfSectionSettings.MaximumTypeSettings, end - cursor);
                    var types = new List<SectionTypeInput>();
                    int sourcesRemaining = DxfSectionSettings.MaximumSourceReferences, geometryRemaining = DxfSectionSettings.MaximumGeometrySettings;
                    for (int i = 0; i < count; i++) types.Add(this.ReadSectionType(tags, ref cursor, end, ref sourcesRemaining, ref geometryRemaining));
                    if (cursor != end) SectionUnexpected(tags[cursor]);
                    var settings = new DxfSectionSettings(codeName) { SectionType = (int)header[90].Value };
                    if (end < tags.Count) this.ReadDatabaseXData(settings, tags, end);
                    record.Object = settings;
                    this.pendingSectionSettings.Add(settings, types);
                    typed = true;
                }
                catch (SectionSettingsPrivateVariant) { }
                catch (ArgumentException error) { throw new FormatException("Invalid SECTIONSETTINGS stored value.", error); }
            }
            if (!typed)
            {
                opaque.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject(codeName, opaque);
            }
            record.Object.Handle = handle;
            return record;
        }

        private SectionTypeInput ReadSectionType(List<DxfTag> tags, ref int cursor, int end, ref int sourcesRemaining, ref int geometryRemaining)
        {
            SectionMarker(tags, ref cursor, end, 1, "SectionTypeSettings");
            var fields = new Dictionary<short, DxfTag>();
            var input = new SectionTypeInput();
            while (cursor < end && tags[cursor].Code != 2 && tags[cursor].Code != 3)
            {
                DxfTag tag = tags[cursor++];
                if (tag.Code == 330)
                {
                    if (input.Sources.Count == sourcesRemaining) throw new FormatException("SECTIONSETTINGS exceeds its aggregate source-reference budget.");
                    input.Sources.Add(SectionHandle(tag));
                }
                else
                {
                    if (tag.Code != 90 && tag.Code != 91 && tag.Code != 92 && tag.Code != 331 && tag.Code != 1 && tag.Code != 93) SectionUnexpected(tag);
                    SectionUnique(fields, tag);
                }
            }
            SectionRequired(fields, 90, 91, 92, 331, 1, 93);
            if (SectionCount(fields[92], sourcesRemaining, input.Sources.Count) != input.Sources.Count) throw new FormatException("SECTIONSETTINGS source count differs from the complete source sequence.");
            sourcesRemaining -= input.Sources.Count;
            input.Type = (int)fields[90].Value; input.Options = (int)fields[91].Value;
            input.Destination = SectionHandle(fields[331]);
            input.File = DxfSectionSettings.StoredText(this.DecodeEncodedNonAsciiCharacters((string)fields[1].Value), "fileName");
            int count = SectionCount(fields[93], geometryRemaining, (end - cursor) / 17);
            geometryRemaining -= count;
            bool initialMarker = cursor < end && tags[cursor].Code == 2;
            if (initialMarker) SectionMarker(tags, ref cursor, end, 2, "SectionGeometrySettings");
            else if (count > 0) throw new FormatException("SECTIONSETTINGS geometry requires its initial marker.");
            bool? repeat = null;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    bool marker = cursor < end && tags[cursor].Code == 2;
                    if (repeat.HasValue && repeat.Value != marker) throw new FormatException("SECTIONSETTINGS mixes repeated and sequence geometry markers.");
                    repeat = marker;
                    if (marker) SectionMarker(tags, ref cursor, end, 2, "SectionGeometrySettings");
                }
                input.Geometry.Add(this.ReadSectionGeometry(tags, ref cursor, end));
            }
            input.RepeatMarkers = count == 0 ? !initialMarker : repeat ?? true;
            SectionMarker(tags, ref cursor, end, 3, "SectionTypeSettingsEnd");
            return input;
        }

        private DxfSectionGeometrySettings ReadSectionGeometry(List<DxfTag> tags, ref int cursor, int end)
        {
            var fields = new Dictionary<short, DxfTag>();
            var known = new HashSet<short> { 90, 91, 92, 62, 63, 8, 6, 40, 1, 370, 70, 71, 72, 2, 41, 42, 43 };
            while (cursor < end && tags[cursor].Code != 3)
            {
                DxfTag tag = tags[cursor++];
                if (!known.Contains(tag.Code)) SectionUnexpected(tag);
                SectionUnique(fields, tag);
            }
            SectionMarker(tags, ref cursor, end, 3, "SectionGeometrySettingsEnd");
            SectionRequired(fields, 90, 91, 92, 8, 6, 40, 1, 370, 70, 71, 72, 2, 41, 42, 43);
            if (fields.ContainsKey(62) && fields.ContainsKey(63)) throw new SectionSettingsPrivateVariant();
            short color = fields.ContainsKey(62) ? (short)62 : (short)63;
            SectionRequired(fields, color);
            return new DxfSectionGeometrySettings
            {
                SectionType = (int)fields[90].Value, GeometryValue = (int)fields[91].Value, Flags = (int)fields[92].Value,
                ColorCode = color, ColorIndex = (short)fields[color].Value,
                LayerName = this.DecodeEncodedNonAsciiCharacters((string)fields[8].Value),
                LinetypeName = this.DecodeEncodedNonAsciiCharacters((string)fields[6].Value),
                LinetypeScale = (double)fields[40].Value,
                PlotStyleName = this.DecodeEncodedNonAsciiCharacters((string)fields[1].Value),
                Lineweight = (short)fields[370].Value, FaceTransparency = (short)fields[70].Value, EdgeTransparency = (short)fields[71].Value,
                HatchPatternType = (short)fields[72].Value, HatchPatternName = this.DecodeEncodedNonAsciiCharacters((string)fields[2].Value),
                HatchAngle = (double)fields[41].Value, HatchScale = (double)fields[42].Value, HatchSpacing = (double)fields[43].Value
            };
        }
        private static void SectionUnique(Dictionary<short, DxfTag> fields, DxfTag tag)
        { if (fields.ContainsKey(tag.Code)) throw new FormatException("SECTIONSETTINGS repeats group " + tag.Code + " in one public bundle."); fields.Add(tag.Code, tag); }
        private static void SectionRequired(Dictionary<short, DxfTag> fields, params short[] codes)
        { if (codes.Any(code => !fields.ContainsKey(code))) throw new FormatException("SECTIONSETTINGS has an incomplete public bundle."); }
        private static int SectionCount(DxfTag tag, int maximum, int available)
        { int count = (int)tag.Value; if (count < 0 || count > maximum || count > available) throw new FormatException("SECTIONSETTINGS count exceeds its admission budget or available data."); return count; }
        private static string SectionHandle(DxfTag tag)
        {
            string handle = (string)tag.Value;
            if (!ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)) throw new FormatException("SECTIONSETTINGS contains an invalid reference handle.");
            return value == 0 ? null : handle;
        }
        private static void SectionMarker(List<DxfTag> tags, ref int cursor, int end, short code, string value)
        {
            if (cursor >= end) throw new FormatException("SECTIONSETTINGS is missing " + value + ".");
            DxfTag tag = tags[cursor];
            if (tag.Code != code || (string)tag.Value != value) SectionUnexpected(tag);
            cursor++;
        }
        private static void SectionUnexpected(DxfTag tag)
        {
            if (tag.Code == 100 && (string)tag.Value != "AcDbSectionSettings") throw new SectionSettingsPrivateVariant();
            if ((tag.Code == 1 || tag.Code == 2 || tag.Code == 3) && !((string)tag.Value).StartsWith("Section", StringComparison.Ordinal)) throw new SectionSettingsPrivateVariant();
            if (new short[] { 1, 2, 3, 90, 91, 92, 93, 330, 331, 100 }.Contains(tag.Code)) throw new FormatException("SECTIONSETTINGS has misplaced public data or a missing marker.");
            throw new SectionSettingsPrivateVariant();
        }
        private void ResolveSectionSettingsReferences()
        {
            foreach (var pending in this.pendingSectionSettings)
            {
                var values = new List<DxfSectionTypeSettings>();
                foreach (SectionTypeInput type in pending.Value)
                {
                    var sources = new List<DxfObject>();
                    foreach (string handle in type.Sources)
                    {
                        DxfObject source = handle == null ? null : this.GetObjectBySourceHandle(handle);
                        if (handle != null && source == null) throw new FormatException("SECTIONSETTINGS source object is absent from the source file: " + handle);
                        sources.Add(source);
                    }
                    BlockRecord destination = type.Destination == null ? null : this.GetObjectBySourceHandle(type.Destination) as BlockRecord;
                    if (type.Destination != null && destination == null) throw new FormatException("SECTIONSETTINGS destination must be an actual source BLOCK_RECORD: " + type.Destination);
                    values.Add(new DxfSectionTypeSettings(type.Type, type.Options, sources, destination, type.File, type.Geometry, type.RepeatMarkers));
                }
                pending.Key.SetTypeSettings(values);
            }
        }
    }
}
