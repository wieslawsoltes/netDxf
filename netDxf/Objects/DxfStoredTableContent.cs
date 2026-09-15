// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>An immutable TABLECONTENT subclass packet.</summary>
    public sealed class DxfStoredTableContentSubclass
    {
        internal DxfStoredTableContentSubclass(string name, IList<DxfTag> tags)
        { this.Name = name; this.Tags = new List<DxfTag>(tags).AsReadOnly(); }
        /// <summary>Gets the stored subclass name.</summary>
        public string Name { get; }
        /// <summary>Gets the complete ordered packet, including its group-100 marker.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
    }

    /// <summary>A loaded TABLECONTENT with immutable stored data and exact source dependencies.</summary>
    /// <remarks>
    /// The object remains in its source document and DXF version. Nested cell values, formatting,
    /// formulas and data links remain stored packets; editing, cloning, erasure and regeneration
    /// require the complete application schema. Common metadata and XData retain their ordinary interfaces.
    /// </remarks>
    public sealed class DxfStoredTableContent : DxfDatabaseObject
    {
        internal const int MaximumPayloadTags = 1048576;
        internal static readonly string[] SubclassNames = { "AcDbLinkedData", "AcDbLinkedTableData", "AcDbFormattedTableData", "AcDbTableContent" };
        private readonly DxfDocument source;
        private readonly List<DxfObject> references = new List<DxfObject>();
        private readonly Dictionary<string, DxfObject> handles = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
        private readonly string styleHandle;
        private DxfObject sourceOwner;
        private bool resolved;

        internal DxfStoredTableContent(DxfDocument source, IList<DxfTag> tags, Func<string, string> decode) : base("TABLECONTENT")
        {
            this.source = source;
            this.SourceVersion = source.DrawingVariables.AcadVer;
            this.Payload = new List<DxfTag>(tags).AsReadOnly();
            var starts = tags.Select((tag, index) => new { tag, index }).Where(value => value.tag.Code == 100).Select(value => value.index).ToArray();
            if (starts.Length != 4 || starts[0] != 0) throw new FormatException("TABLECONTENT requires its four ordered subclass packets.");
            var packets = new List<DxfStoredTableContentSubclass>();
            for (int i = 0; i < starts.Length; i++)
            {
                if ((string)tags[starts[i]].Value != SubclassNames[i]) throw new FormatException("TABLECONTENT subclass order is invalid.");
                packets.Add(new DxfStoredTableContentSubclass(SubclassNames[i], tags.Skip(starts[i]).Take((i + 1 < starts.Length ? starts[i + 1] : tags.Count) - starts[i]).ToList()));
            }
            this.Subclasses = packets.AsReadOnly();
            var linked = packets[0].Tags;
            if (linked.Count == 3 && linked[1].Code == 1 && linked[2].Code == 300)
            { this.Name = decode((string)linked[1].Value); this.Description = decode((string)linked[2].Value); }
            var terminal = packets[3].Tags;
            if (terminal.Count != 2 || terminal[1].Code != 340) throw new FormatException("TABLECONTENT requires one terminal table-style handle.");
            this.styleHandle = (string)terminal[1].Value;
            this.ReadOuterCounts(packets[1].Tags);
            ValidateFrames(packets[2].Tags);
        }
        /// <summary>Gets the source DXF version. Conversion to another version is not supported.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the complete immutable subclass payload, excluding common metadata and XData.</summary>
        public IReadOnlyList<DxfTag> Payload { get; }
        /// <summary>Gets the four ordered stored subclass packets.</summary>
        public IReadOnlyList<DxfStoredTableContentSubclass> Subclasses { get; }
        /// <summary>Gets the decoded linked-data name, or null when its small header is not recognized.</summary>
        public string Name { get; }
        /// <summary>Gets the decoded linked-data description, or null when its small header is not recognized.</summary>
        public string Description { get; }
        /// <summary>Gets the outer stored column count, or null for an unrecognized outer packet.</summary>
        public int? ColumnCount { get; private set; }
        /// <summary>Gets the outer stored row count, or null for an unrecognized outer packet.</summary>
        public int? RowCount { get; private set; }
        /// <summary>Gets the exact TABLESTYLE target of the final subclass, or null for a stored null handle.</summary>
        /// <remarks>The target may remain opaque when its style variant is not publicly modeled.</remarks>
        public DxfDatabaseObject TableStyle { get; private set; }
        /// <summary>Gets exact source identities for exposed semantic handles, in packet order, including repetitions.</summary>
        public IReadOnlyList<DxfObject> References { get { return this.references.AsReadOnly(); } }
        internal override IEnumerable<DxfObject> DatabaseReferences
        { get { return this.references.Where(item => ReferenceEquals(this.source.GetObjectByHandle(item.Handle), item)); } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Payload; } }
        internal override DxfDatabaseObject CloneShell() { throw new NotSupportedException("Stored TABLECONTENT cloning requires its complete application schema."); }

        internal void Resolve(Func<string, DxfObject> resolve)
        {
            foreach (DxfTag tag in this.Payload)
            {
                if (!DxfObjectDatabase.IsReference(tag) || Convert.ToUInt64((string)tag.Value, 16) == 0) continue;
                string handle = (string)tag.Value;
                DxfObject target = resolve(handle);
                if (target == null) throw new FormatException("TABLECONTENT requires an exact source reference identity: " + handle);
                this.handles[handle] = target;
                this.references.Add(target);
            }
            if (Convert.ToUInt64(this.styleHandle, 16) != 0)
            {
                this.TableStyle = resolve(this.styleHandle) as DxfDatabaseObject;
                if (this.TableStyle == null || this.TableStyle.CodeName != "TABLESTYLE") throw new FormatException("TABLECONTENT terminal group 340 must identify a TABLESTYLE.");
            }
            this.sourceOwner = this.Owner;
            if (this.sourceOwner == null || !ReferenceEquals(this.source.GetObjectByHandle(this.sourceOwner.Handle), this.sourceOwner))
                throw new FormatException("TABLECONTENT requires a registered source owner.");
            var ancestry = new HashSet<DxfObject>();
            for (DxfObject ancestor = this.sourceOwner; ancestor != null && !ReferenceEquals(ancestor, this.source); ancestor = ancestor.Owner)
            {
                if (ReferenceEquals(ancestor, this) || !ancestry.Add(ancestor)) throw new FormatException("TABLECONTENT source ownership contains a cycle.");
                if (!ReferenceEquals(this.source.GetObjectByHandle(ancestor.Handle), ancestor)) throw new FormatException("TABLECONTENT source ancestry contains an unregistered object.");
            }
            this.resolved = true;
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.resolved || !ReferenceEquals(database.Document, this.source))
            { errors.Add("Stored TABLECONTENT must remain in its source document."); return; }
            if (this.source.DrawingVariables.AcadVer != this.SourceVersion) errors.Add("Stored TABLECONTENT conversion requires complete schema regeneration.");
            if (!ReferenceEquals(this.Owner, this.sourceOwner) || !ReferenceEquals(this.source.GetObjectByHandle(this.sourceOwner.Handle), this.sourceOwner))
                errors.Add("Stored TABLECONTENT source ownership changed.");
            foreach (var pair in this.handles)
                if (!ReferenceEquals(this.source.StoredTableHandleTarget(pair.Key), pair.Value)) errors.Add("A stored TABLECONTENT dependency is no longer registered: " + pair.Key);
            foreach (DxfTag tag in this.Payload)
                if (tag.Value is string text)
                    for (int i = 0; i < text.Length; i++)
                        if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[++i])))
                        { errors.Add("Stored TABLECONTENT contains invalid UTF-16 text."); break; }
        }
        private void ReadOuterCounts(IReadOnlyList<DxfTag> tags)
        {
            List<DxfTag> outer = ValidateFrames(tags);
            if (outer == null || outer.Count < 3 || outer[0].Code != 90) return;
            int index = 1, columns = 0, rows = 0;
            while (index < outer.Count && outer[index].Code == 300 && (string)outer[index].Value == "COLUMN") { columns++; index++; }
            if (index >= outer.Count || outer[index].Code != 91) return;
            int rowCount = (int)outer[index++].Value;
            while (index < outer.Count && outer[index].Code == 301 && (string)outer[index].Value == "ROW") { rows++; index++; }
            if (index != outer.Count - 1 || outer[index].Code != 92) return;
            int columnCount = (int)outer[0].Value;
            if (columnCount < 0 || rowCount < 0 || columnCount != columns || rowCount != rows)
                throw new FormatException("TABLECONTENT outer row or column count does not match its stored packets.");
            this.ColumnCount = columnCount; this.RowCount = rowCount;
        }
        private static readonly HashSet<string> FrameNames = new HashSet<string>(new[] {
            "CELLCONTENT", "CELLMARGIN", "CONTENTFORMAT", "DATAMAP", "FORMATTEDCELLCONTENT",
            "FORMATTEDTABLEDATACELL", "FORMATTEDTABLEDATACOLUMN", "FORMATTEDTABLEDATAROW", "GRIDFORMAT",
            "LINKEDTABLEDATACELL", "LINKEDTABLEDATACOLUMN", "LINKEDTABLEDATAROW", "TABLECELL", "TABLECOLUMN", "TABLEFORMAT", "TABLEROW"
        }, StringComparer.Ordinal);
        private static List<DxfTag> ValidateFrames(IReadOnlyList<DxfTag> tags)
        {
            var stack = new Stack<string>(); var outer = new List<DxfTag>();
            for (int index = 1; index < tags.Count; index++)
            {
                DxfTag tag = tags[index];
                string text = tag.Value as string;
                // AcValue group 1 is user data, including strings equal to structural marker names.
                if (tag.Code == 300 && text == "VALUE" && stack.Count > 0 && stack.Peek() == "CELLCONTENT"
                    && index + 2 < tags.Count && tags[index + 1].Code == 93 && tags[index + 2].Code == 90)
                {
                    while (++index < tags.Count && !(tags[index].Code == 304 && (string)tags[index].Value == "ACVALUE_END")) { }
                    if (index == tags.Count) throw new FormatException("TABLECONTENT has an unterminated stored AcValue packet.");
                    continue;
                }
                if (tag.Code == 1 && text.EndsWith("_BEGIN", StringComparison.Ordinal) && FrameNames.Contains(text.Substring(0, text.Length - 6)))
                {
                    if (text == "DATAMAP_BEGIN")
                    {
                        // Native maps are empty or contain one named stored value. Skip value text
                        // as data; unfamiliar map shapes retain their packet without projection.
                        if (!TryReadDataMapEnd(tags, index, out int end)) return null;
                        index = end; continue;
                    }
                    if (stack.Count >= 64) throw new FormatException("TABLECONTENT packet nesting exceeds the supported storage limit.");
                    stack.Push(text.Substring(0, text.Length - 6));
                }
                else if (tag.Code == 309 && text.EndsWith("_END", StringComparison.Ordinal) && FrameNames.Contains(text.Substring(0, text.Length - 4)))
                {
                    if (stack.Count == 0 || stack.Pop() != text.Substring(0, text.Length - 4)) throw new FormatException("TABLECONTENT has mismatched stored packet framing.");
                }
                else if (stack.Count == 0) outer.Add(tag);
            }
            if (stack.Count != 0) throw new FormatException("TABLECONTENT has an unterminated stored packet.");
            return outer;
        }
        private static bool TryReadDataMapEnd(IReadOnlyList<DxfTag> tags, int start, out int end)
        {
            end = start;
            if (start + 1 >= tags.Count || tags[start + 1].Code != 90) return false;
            int count = (int)tags[start + 1].Value;
            if (count == 0)
            {
                if (start + 2 >= tags.Count || tags[start + 2].Code != 309 || (string)tags[start + 2].Value != "DATAMAP_END")
                    throw new FormatException("TABLECONTENT empty DATAMAP framing is invalid.");
                end = start + 2; return true;
            }
            if (count != 1 || start + 5 >= tags.Count || tags[start + 2].Code != 300
                || tags[start + 3].Code != 301 || (string)tags[start + 3].Value != "DATAMAP_VALUE") return false;
            // The pinned R2004 maps use the earlier exact numeric-value envelope.
            if (start + 6 < tags.Count && tags[start + 4].Code == 90 && (int)tags[start + 4].Value == 2
                && tags[start + 5].Code == 140 && tags[start + 6].Code == 309 && (string)tags[start + 6].Value == "DATAMAP_END")
            { end = start + 6; return true; }
            if (tags[start + 4].Code != 93 || tags[start + 5].Code != 90) return false;
            int index = start + 6;
            while (index < tags.Count && !(tags[index].Code == 304 && (string)tags[index].Value == "ACVALUE_END")
                && !(tags[index].Code == 309 && (string)tags[index].Value == "DATAMAP_END")) index++;
            if (index == tags.Count || tags[index].Code != 304) throw new FormatException("TABLECONTENT has an unterminated DATAMAP AcValue packet.");
            index++;
            if (index >= tags.Count || tags[index].Code != 309 || (string)tags[index].Value != "DATAMAP_END") return false;
            end = index; return true;
        }
    }
}
