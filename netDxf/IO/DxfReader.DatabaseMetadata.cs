using System;
using System.Collections.Generic;
using System.Globalization;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        // Observes only the common header before the first subclass marker. In particular,
        // XRECORD payload groups 100/102/330/360 are never mistaken for object metadata.
        private sealed class DatabaseMetadataReader : ICodeValueReader
        {
            private readonly ICodeValueReader inner;
            private readonly Dictionary<string, DatabaseMetadata> records;
            private readonly HashSet<ulong> sourceIdentities;
            private DatabaseMetadata current = new DatabaseMetadata();
            private string handle;
            private string group;
            private bool common;
            private string recordType;
            private string section;
            private bool sectionHeader;
            internal SourceRecordIdentity SourceRecord { get; private set; } = new SourceRecordIdentity();
            internal DatabaseMetadataReader(ICodeValueReader inner, Dictionary<string, DatabaseMetadata> records, HashSet<ulong> sourceIdentities)
            { this.inner = inner; this.records = records; this.sourceIdentities = sourceIdentities; }
            public short Code { get { return this.inner.Code; } }
            public object Value { get { return this.inner.Value; } }
            public long CurrentPosition { get { return this.inner.CurrentPosition; } }
            public bool Code5IsString { get { return this.inner.Code5IsString; } set { this.inner.Code5IsString = value; } }
            internal void SkipComments() { if (this.inner is TextCodeValueReader text) text.SkipComments = true; }
            public void Next()
            {
                this.inner.Next();
                if (this.Code == 0)
                {
                    if (this.group != null) throw new FormatException("Unterminated common object control group.");
                    this.Flush(); this.current = new DatabaseMetadata(); this.handle = null; this.common = true; this.recordType = this.ReadString();
                    this.SourceRecord = new SourceRecordIdentity();
                    // SECTION is also a documented entity name. Only the file-level
                    // record outside an existing section introduces a section name.
                    this.sectionHeader = this.recordType == "SECTION" && this.section == null;
                    if (this.recordType == "ENDSEC") this.section = null;
                    return;
                }
                if (this.sectionHeader && this.section == null && this.Code == 2) this.section = this.ReadString();
                if (!this.common) return;
                if (this.Code == 100 || this.Code == 1001) { this.Flush(); this.common = false; return; }
                if (this.Code == 102)
                {
                    string value = this.ReadString();
                    if (value == "}") this.group = null;
                    else if (value.StartsWith("{", StringComparison.Ordinal)) this.group = value;
                    return;
                }
                if (this.group != null)
                {
                    if (this.group == "{ACAD_XDICTIONARY" && this.Code == 360) this.current.Extension = this.ReadHex();
                    else if (this.group == "{ACAD_REACTORS" && this.Code == 330) this.current.Reactors.Add(this.ReadHex());
                    return;
                }
                if (this.Code == 5 && this.recordType != "DIMSTYLE" || this.Code == 105 && this.recordType == "DIMSTYLE") this.handle = this.ReadHex();
            }
            private void Flush()
            {
                if ((this.section == "TABLES" || this.section == "BLOCKS" || this.section == "ENTITIES" || this.section == "OBJECTS")
                    && this.recordType != null && !this.sectionHeader && this.recordType != "ENDSEC"
                    && this.recordType != "EOF" && this.recordType != "ENDTAB" && this.recordType != "CLASS"
                    && ulong.TryParse(this.handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong identity)
                    && identity != 0)
                {
                    this.sourceIdentities.Add(identity);
                    this.SourceRecord.Handle = identity;
                }
                if (this.handle != null && (this.current.Extension != null || this.current.Reactors.Count > 0)) this.records[this.handle] = this.current;
            }
            public byte ReadByte() { return this.inner.ReadByte(); }
            public byte[] ReadBytes() { return this.inner.ReadBytes(); }
            public short ReadShort() { return this.inner.ReadShort(); }
            public int ReadInt() { return this.inner.ReadInt(); }
            public long ReadLong() { return this.inner.ReadLong(); }
            public bool ReadBool() { return this.inner.ReadBool(); }
            public double ReadDouble() { return this.inner.ReadDouble(); }
            public string ReadString() { return this.inner.ReadString(); }
            public string ReadHex() { return this.inner.ReadHex(); }
        }
    }
}
