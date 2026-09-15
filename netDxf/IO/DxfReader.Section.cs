// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<Section, string>> loadedSections = new List<Tuple<Section, string>>();
        private Section ReadSection(string codeName)
        {
            if (this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) throw new NotSupportedException("SECTION input requires R2007 or later.");
            if (this.chunk.Code != 100 || this.chunk.ReadString() != "AcDbSection") throw new InvalidDataException("SECTION requires AcDbSection subclass data.");
            var section = new Section(codeName) { PendingInputReferences = true, HasSettingsField = false };
            var tags = new List<DxfTag>(); bool xdataStarted = false; this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                if (this.chunk.Code == 1001)
                {
                    xdataStarted = true;
                    string app = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    section.XData.Add(this.ReadXDataRecord(this.GetApplicationRegistry(app))); continue;
                }
                if (xdataStarted) throw new InvalidDataException("SECTION XData must follow its complete payload.");
                if (tags.Count >= 6 * Section.MaximumVertices + 32) throw new InvalidDataException("SECTION payload exceeds the vertex admission limit.");
                tags.Add(new DxfTag(this.chunk.Code, this.chunk.Value)); this.chunk.Next();
            }
            var parser = new SectionParser(this, tags); string settings = parser.Read(section);
            this.loadedSections.Add(Tuple.Create(section, settings)); return section;
        }
        private void ResolveSections()
        {
            foreach (var pending in this.loadedSections)
            {
                Section section = pending.Item1; string handle = pending.Item2;
                if (handle != null && handle != "0")
                {
                    var settings = this.GetObjectBySourceHandle(handle) as DxfDatabaseObject;
                    if (settings == null || settings.CodeName != "SECTIONSETTINGS" && settings.CodeName != "SECTION_SETTINGS")
                        throw new InvalidDataException("SECTION geometry settings must resolve to a settings object.");
                    if (!ReferenceEquals(settings.Owner, section)) throw new InvalidDataException("SECTION geometry settings must have the reciprocal section owner.");
                    section.GeometrySettings = settings;
                }
                section.PendingInputReferences = false; section.Validate(this.doc);
            }
        }
        private sealed class SectionParser
        {
            private readonly DxfReader reader;
            private readonly List<DxfTag> tags;
            private int at;
            internal SectionParser(DxfReader reader, List<DxfTag> tags) { this.reader = reader; this.tags = tags; }
            private short Code { get { return this.at == this.tags.Count ? (short)-1 : this.tags[this.at].Code; } }
            private object Take(short code)
            {
                if (this.Code != code) throw new InvalidDataException("Invalid SECTION packet at tag " + this.at + ": expected " + code + ", found " + this.Code + ".");
                return this.tags[this.at++].Value;
            }
            private Vector3 Vector(short code)
            { return new Vector3((double)this.Take(code), (double)this.Take((short)(code + 10)), (double)this.Take((short)(code + 20))); }
            private void Vertices(Section section, bool back)
            {
                int count = (int)this.Take(back ? (short)93 : (short)92);
                if (count < 0 || count > Section.MaximumVertices || (long)count * 3 > this.tags.Count - this.at)
                    throw new InvalidDataException("SECTION vertex count is negative, excessive or exceeds the available packet.");
                var vertices = back ? section.BackLineVertices : section.Vertices;
                for (int i = 0; i < count; i++) vertices.Add(this.Vector(back ? (short)12 : (short)11));
            }
            internal string Read(Section section)
            {
                try
                {
                    section.State = (int)this.Take(90); section.Flags = (int)this.Take(91);
                    section.Name = this.reader.DecodeEncodedNonAsciiCharacters((string)this.Take(1));
                    section.VerticalDirection = this.Vector(10); section.TopHeight = (double)this.Take(40); section.BottomHeight = (double)this.Take(41);
                    section.IndicatorTransparency = (short)this.Take(70);
                    // These two documented/native ACI slots are independent; neither is common entity Color.
                    var colors = new HashSet<short>();
                    while (this.Code == 62 || this.Code == 63 || this.Code == 411)
                    {
                        short code = this.Code;
                        if (!colors.Add(code)) throw new InvalidDataException("Duplicate SECTION indicator color field.");
                        if (code == 62) section.StoredNativeIndicatorColor = (short)this.Take(code);
                        else if (code == 63) section.StoredIndicatorColor = (short)this.Take(code);
                        else section.IndicatorColorName = this.reader.DecodeEncodedNonAsciiCharacters((string)this.Take(code));
                    }
                    this.Vertices(section, false); this.Vertices(section, true);
                    string settings = null;
                    if (this.Code == 360) { section.HasSettingsField = true; settings = SectionHandle(new DxfTag(360, this.Take(360))); }
                    if (this.Code != -1) throw new InvalidDataException("Unsupported or duplicate SECTION payload field " + this.Code + ".");
                    return settings;
                }
                catch (ArgumentException error) { throw new InvalidDataException("Invalid SECTION stored value.", error); }
            }
        }
    }
}
