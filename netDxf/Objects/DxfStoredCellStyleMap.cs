// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>One immutable stored CELLSTYLEMAP entry.</summary>
    public sealed class DxfStoredCellStyleMapEntry
    {
        internal DxfStoredCellStyleMapEntry(int id, int type, string name, IList<DxfTag> format)
        {
            this.Id = id; this.StoredType = type; this.Name = name;
            this.FormatPayload = new List<DxfTag>(format).AsReadOnly();
        }
        /// <summary>Gets the stored group-90 entry identifier.</summary>
        public int Id { get; }
        /// <summary>Gets the uninterpreted group-91 entry type.</summary>
        public int StoredType { get; }
        /// <summary>Gets the decoded stored name without assigning a fixed cell role.</summary>
        public string Name { get; }
        /// <summary>Gets the complete immutable TABLEFORMAT packet, including its frame markers.</summary>
        /// <remarks>Formatting fields remain stored tags; this collection does not evaluate their meaning.</remarks>
        public IReadOnlyList<DxfTag> FormatPayload { get; }
    }

    /// <summary>A loaded CELLSTYLEMAP with immutable ordered entries and exact source dependencies.</summary>
    /// <remarks>
    /// Entry identifiers, types, names and formatting packets remain in their source document and
    /// DXF version. Editing, cross-document cloning, erasure and style regeneration require the
    /// complete application schema. Common metadata and XData retain their ordinary interfaces.
    /// </remarks>
    public sealed class DxfStoredCellStyleMap : DxfDatabaseObject
    {
        internal const int MaximumPayloadTags = 1048576;
        internal static readonly string[] FrameNames = { "TABLEFORMAT", "CONTENTFORMAT", "CELLMARGIN", "GRIDFORMAT", "CELLSTYLE" };
        private readonly DxfDocument source;
        private readonly List<DxfObject> references = new List<DxfObject>();
        private readonly Dictionary<string, DxfObject> handles = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
        private DxfObject sourceOwner;
        private bool resolved;

        internal DxfStoredCellStyleMap(DxfDocument source, IList<DxfTag> tags, Func<string, string> decode) : base("CELLSTYLEMAP")
        {
            this.source = source; this.SourceVersion = source.DrawingVariables.AcadVer;
            this.Payload = new List<DxfTag>(tags).AsReadOnly();
            int index = 0; Marker(tags, ref index, 100, "AcDbCellStyleMap");
            int count = (int)Read(tags, ref index, 90).Value;
            if (count < 0 || count > MaximumPayloadTags || count > (tags.Count - index) / 8)
                throw new FormatException("CELLSTYLEMAP count exceeds its stored payload or storage limit.");
            var entries = new List<DxfStoredCellStyleMapEntry>();
            for (int item = 0; item < count; item++)
            {
                Marker(tags, ref index, 300, "CELLSTYLE");
                int start = index;
                Marker(tags, ref index, 1, "TABLEFORMAT_BEGIN");
                var frames = new Stack<string>(); frames.Push("TABLEFORMAT");
                while (frames.Count != 0)
                {
                    if (index >= tags.Count) throw new FormatException("CELLSTYLEMAP has an unterminated format packet.");
                    DxfTag tag = tags[index++];
                    if (tag.Code == 1)
                    {
                        string value = (string)tag.Value;
                        string frame = value.Substring(0, value.Length - 6);
                        if (frame == "CELLSTYLE" || frames.Count >= 64)
                            throw new FormatException("CELLSTYLEMAP format nesting is invalid or exceeds its storage limit.");
                        frames.Push(frame);
                    }
                    else if (tag.Code == 309)
                    {
                        string value = (string)tag.Value;
                        if (frames.Pop() != value.Substring(0, value.Length - 4))
                            throw new FormatException("CELLSTYLEMAP has mismatched format framing.");
                    }
                    else if (tag.Code == 100) throw new FormatException("CELLSTYLEMAP has an unexpected subclass in its format packet.");
                }
                var format = tags.Skip(start).Take(index - start).ToList();
                Marker(tags, ref index, 1, "CELLSTYLE_BEGIN");
                int id = (int)Read(tags, ref index, 90).Value;
                int type = (int)Read(tags, ref index, 91).Value;
                string name = decode((string)Read(tags, ref index, 300).Value);
                Marker(tags, ref index, 309, "CELLSTYLE_END");
                entries.Add(new DxfStoredCellStyleMapEntry(id, type, name, format));
            }
            if (index != tags.Count) throw new FormatException("CELLSTYLEMAP contains unexpected data after its counted entries.");
            this.Entries = entries.AsReadOnly();
        }
        /// <summary>Gets the source DXF version. Conversion to another version is not supported.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the complete immutable subclass payload, excluding common metadata and XData.</summary>
        public IReadOnlyList<DxfTag> Payload { get; }
        /// <summary>Gets entries in stored order without imposing identifier uniqueness or fixed roles.</summary>
        public IReadOnlyList<DxfStoredCellStyleMapEntry> Entries { get; }
        /// <summary>Gets exact source identities for nonzero semantic handles, including repetitions.</summary>
        public IReadOnlyList<DxfObject> References { get { return this.references.AsReadOnly(); } }
        internal override IEnumerable<DxfObject> DatabaseReferences
        { get { return this.references.Where(item => ReferenceEquals(this.source.GetObjectByHandle(item.Handle), item)); } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Payload; } }
        internal override DxfDatabaseObject CloneShell() { throw new NotSupportedException("Stored CELLSTYLEMAP cloning requires its complete application schema."); }
        internal void Resolve(Func<string, DxfObject> resolve)
        {
            foreach (DxfTag tag in this.Payload)
            {
                if (!DxfObjectDatabase.IsReference(tag) || Convert.ToUInt64((string)tag.Value, 16) == 0) continue;
                string handle = (string)tag.Value;
                DxfObject target = resolve(handle);
                if (target == null) throw new FormatException("CELLSTYLEMAP requires an exact source reference identity: " + handle);
                this.handles[handle] = target;
                this.references.Add(target);
            }
            this.sourceOwner = this.Owner;
            if (this.sourceOwner == null || !ReferenceEquals(this.source.GetObjectByHandle(this.sourceOwner.Handle), this.sourceOwner))
                throw new FormatException("CELLSTYLEMAP requires a registered source owner.");
            var ancestry = new HashSet<DxfObject>();
            for (DxfObject ancestor = this.sourceOwner; ancestor != null && !ReferenceEquals(ancestor, this.source); ancestor = ancestor.Owner)
            {
                if (ReferenceEquals(ancestor, this) || !ancestry.Add(ancestor)) throw new FormatException("CELLSTYLEMAP source ownership contains a cycle.");
                if (!ReferenceEquals(this.source.GetObjectByHandle(ancestor.Handle), ancestor)) throw new FormatException("CELLSTYLEMAP source ancestry contains an unregistered object.");
            }
            this.resolved = true;
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.resolved || !ReferenceEquals(database.Document, this.source))
            { errors.Add("Stored CELLSTYLEMAP must remain in its source document."); return; }
            if (this.source.DrawingVariables.AcadVer != this.SourceVersion) errors.Add("Stored CELLSTYLEMAP conversion requires complete schema regeneration.");
            if (!ReferenceEquals(this.Owner, this.sourceOwner) || !ReferenceEquals(this.source.GetObjectByHandle(this.sourceOwner.Handle), this.sourceOwner))
                errors.Add("Stored CELLSTYLEMAP source ownership changed.");
            foreach (var pair in this.handles)
                if (!ReferenceEquals(this.source.StoredTableHandleTarget(pair.Key), pair.Value)) errors.Add("A stored CELLSTYLEMAP dependency is no longer registered: " + pair.Key);
            foreach (DxfTag tag in this.Payload)
                if (tag.Value is string text)
                    for (int i = 0; i < text.Length; i++)
                        if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[++i])))
                        { errors.Add("Stored CELLSTYLEMAP contains invalid UTF-16 text."); break; }
        }
        private static DxfTag Read(IList<DxfTag> tags, ref int index, short code)
        {
            if (index >= tags.Count || tags[index].Code != code)
                throw new FormatException("CELLSTYLEMAP requires ordered group " + code + ".");
            return tags[index++];
        }
        private static void Marker(IList<DxfTag> tags, ref int index, short code, string marker)
        {
            if ((string)Read(tags, ref index, code).Value != marker)
                throw new FormatException("CELLSTYLEMAP requires stored marker " + marker + ".");
        }
    }
}
