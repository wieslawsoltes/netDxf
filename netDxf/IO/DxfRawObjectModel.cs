// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace netDxf.IO
{
    /// <summary>Duplicate-name policy stored by dictionaries and XRECORD objects.</summary>
    public enum DxfDuplicateRecordCloning : short
    {
        /// <summary>No policy applies.</summary>
        NotApplicable = 0,
        /// <summary>Keep the existing record.</summary>
        KeepExisting = 1,
        /// <summary>Replace with the incoming clone.</summary>
        UseClone = 2,
        /// <summary>Use the xref-prefixed name.</summary>
        XrefMangleName = 3,
        /// <summary>Mangle the incoming name.</summary>
        MangleName = 4,
        /// <summary>Unmangle the incoming name.</summary>
        UnmangleName = 5
    }

    /// <summary>An immutable named dictionary link; link kind and entry order are retained.</summary>
    public sealed class DxfRawDictionaryEntry
    {
        /// <summary>Creates an entry using a Unicode name and a nonzero hexadecimal handle.</summary>
        public DxfRawDictionaryEntry(string name, string handle, bool hardOwner = false)
        {
            DxfObjectText.ValidateName(name);
            this.Name = name; this.Handle = DxfObjectText.Handle(handle, false); this.IsHardOwner = hardOwner;
        }
        /// <summary>Gets the decoded Unicode name. Lookups use ordinal case-insensitive comparison.</summary>
        public string Name { get; }
        /// <summary>Gets the referenced object's canonical handle.</summary>
        public string Handle { get; }
        /// <summary>Gets whether the link uses group 360 instead of group 350.</summary>
        public bool IsHardOwner { get; }
    }

    /// <summary>An immutable redraw association; the sort key is not an object identity.</summary>
    public sealed class DxfRawSortOrderEntry
    {
        /// <summary>Creates a group-331 entity pointer and group-5 sort key. Zero sort keys are allowed.</summary>
        public DxfRawSortOrderEntry(string entityHandle, string sortHandle)
        { this.EntityHandle = DxfObjectText.Handle(entityHandle, false); this.SortHandle = DxfObjectText.Handle(sortHandle, true); }
        /// <summary>Gets the referenced entity.</summary>
        public string EntityHandle { get; }
        /// <summary>Gets the unsigned hexadecimal ordering key.</summary>
        public string SortHandle { get; }
    }

    /// <summary>A schema view over one immutable OBJECTS record. The original tags remain authoritative.</summary>
    public abstract class DxfRawStoredObject
    {
        internal DxfRawStoredObject(string type, string handle, string owner, DxfRawRecord source,
            IReadOnlyList<DxfTag> tags, int bodyStart, int bodyEnd)
        {
            this.TypeName = type; this.Handle = handle; this.OwnerHandle = owner;
            this.SourceRecord = source; this.Tags = tags; this.BodyStart = bodyStart; this.BodyEnd = bodyEnd;
        }
        /// <summary>Gets the DXF record type.</summary>
        public string TypeName { get; }
        /// <summary>Gets the canonical object identity.</summary>
        public string Handle { get; }
        /// <summary>Gets the common owner, or null if its tag was absent.</summary>
        public string OwnerHandle { get; }
        /// <summary>Gets the original record, or null for transaction-created objects.</summary>
        public DxfRawRecord SourceRecord { get; }
        /// <summary>Gets all exact stored tags, including common controls and XData.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
        internal int BodyStart { get; }
        internal int BodyEnd { get; }
    }

    /// <summary>An unknown or unsupported record retained without guessing its private schema.</summary>
    public sealed class DxfRawOpaqueStoredObject : DxfRawStoredObject
    {
        internal DxfRawOpaqueStoredObject(string type, string handle, string owner, DxfRawRecord source,
            IReadOnlyList<DxfTag> tags, string reason) : base(type, handle, owner, source, tags, 0, 0) { this.Reason = reason; }
        /// <summary>Gets why schema-aware editing is unavailable. Raw preservation remains available.</summary>
        public string Reason { get; }
    }

    /// <summary>A DICTIONARY or ACDBDICTIONARYWDFLT, including independent optional flags.</summary>
    public sealed class DxfRawDictionary : DxfRawStoredObject
    {
        internal DxfRawDictionary(string type, string handle, string owner, DxfRawRecord source, IReadOnlyList<DxfTag> tags,
            int start, int end, bool? hardOwner, DxfDuplicateRecordCloning? cloning,
            string defaultHandle, List<DxfRawDictionaryEntry> entries) : base(type, handle, owner, source, tags, start, end)
        { this.HardOwnerFlag = hardOwner; this.CloningFlag = cloning; this.DefaultHandle = defaultHandle; this.Entries = entries.AsReadOnly(); }
        /// <summary>Gets the group-280 flag, or null when absent.</summary>
        public bool? HardOwnerFlag { get; }
        /// <summary>Gets the group-281 flag, or null when absent.</summary>
        public DxfDuplicateRecordCloning? CloningFlag { get; }
        /// <summary>Gets whether this is a dictionary-with-default object.</summary>
        public bool HasDefault { get { return this.TypeName == "ACDBDICTIONARYWDFLT"; } }
        /// <summary>Gets the default pointer, or null when absent. Zero remains a null pointer.</summary>
        public string DefaultHandle { get; }
        /// <summary>Gets the ordered entries; aliases to the same target are not collapsed.</summary>
        public IReadOnlyList<DxfRawDictionaryEntry> Entries { get; }
        /// <summary>Returns the explicit entry, or null. Does not silently substitute the dictionary default.</summary>
        public DxfRawDictionaryEntry Find(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            foreach (DxfRawDictionaryEntry entry in this.Entries)
                if (StringComparer.OrdinalIgnoreCase.Equals(entry.Name, name)) return entry;
            return null;
        }
    }

    /// <summary>An XRECORD with ordered typed raw data, separate from common controls and XData.</summary>
    public sealed class DxfRawXRecord : DxfRawStoredObject
    {
        internal DxfRawXRecord(string handle, string owner, DxfRawRecord source, IReadOnlyList<DxfTag> tags,
            int start, int end, DxfDuplicateRecordCloning? cloning, List<DxfTag> data)
            : base("XRECORD", handle, owner, source, tags, start, end) { this.CloningFlag = cloning; this.Data = data.AsReadOnly(); }
        /// <summary>Gets the optional initial group-280 cloning flag.</summary>
        public DxfDuplicateRecordCloning? CloningFlag { get; }
        /// <summary>Gets payload tags in exact order, without converting them into XData or geometry.</summary>
        public IReadOnlyList<DxfTag> Data { get; }
    }

    /// <summary>An inert ACDBPLACEHOLDER object.</summary>
    public sealed class DxfRawPlaceholder : DxfRawStoredObject
    {
        internal DxfRawPlaceholder(string handle, string owner, DxfRawRecord source, IReadOnlyList<DxfTag> tags, int end)
            : base("ACDBPLACEHOLDER", handle, owner, source, tags, end, end) { }
    }

    /// <summary>A DICTIONARYVAR with Unicode text and optional schema/value presence.</summary>
    public sealed class DxfRawDictionaryVariable : DxfRawStoredObject
    {
        internal DxfRawDictionaryVariable(string handle, string owner, DxfRawRecord source, IReadOnlyList<DxfTag> tags,
            int start, int end, short? schema, string value) : base("DICTIONARYVAR", handle, owner, source, tags, start, end)
        { this.SchemaNumber = schema; this.Value = value; }
        /// <summary>Gets the stored schema number, or null when absent.</summary>
        public short? SchemaNumber { get; }
        /// <summary>Gets decoded text, or null when absent; empty text is distinct.</summary>
        public string Value { get; }
    }

    /// <summary>An ordered IDBUFFER of soft pointers. Duplicate and null pointers remain distinct entries.</summary>
    public sealed class DxfRawIdBuffer : DxfRawStoredObject
    {
        internal DxfRawIdBuffer(string handle, string owner, DxfRawRecord source, IReadOnlyList<DxfTag> tags,
            int start, int end, List<string> handles) : base("IDBUFFER", handle, owner, source, tags, start, end)
        { this.Handles = handles.AsReadOnly(); }
        /// <summary>Gets the ordered canonical handles.</summary>
        public IReadOnlyList<string> Handles { get; }
    }

    /// <summary>A SORTENTSTABLE with its layout block and ordered redraw associations.</summary>
    public sealed class DxfRawSortentsTable : DxfRawStoredObject
    {
        internal DxfRawSortentsTable(string handle, string owner, DxfRawRecord source, IReadOnlyList<DxfTag> tags,
            int start, int end, string block, List<DxfRawSortOrderEntry> entries)
            : base("SORTENTSTABLE", handle, owner, source, tags, start, end)
        { this.BlockRecordHandle = block; this.Entries = entries.AsReadOnly(); }
        /// <summary>Gets the block-record soft pointer, distinct from the common owner.</summary>
        public string BlockRecordHandle { get; }
        /// <summary>Gets ordered entity/sort-key associations.</summary>
        public IReadOnlyList<DxfRawSortOrderEntry> Entries { get; }
    }

    /// <summary>Resource limits for schema views and transactional object edits.</summary>
    public sealed class DxfRawObjectStoreOptions
    {
        /// <summary>Creates explicit positive object, per-payload tag and staged-change budgets.</summary>
        public DxfRawObjectStoreOptions(int maximumObjects = 100000, int maximumPayloadTags = 1000000, int maximumChanges = 100000)
        {
            if (maximumObjects < 1) throw new ArgumentOutOfRangeException(nameof(maximumObjects));
            if (maximumPayloadTags < 1) throw new ArgumentOutOfRangeException(nameof(maximumPayloadTags));
            if (maximumChanges < 1) throw new ArgumentOutOfRangeException(nameof(maximumChanges));
            this.MaximumObjects = maximumObjects; this.MaximumPayloadTags = maximumPayloadTags; this.MaximumChanges = maximumChanges;
        }
        /// <summary>Gets the maximum OBJECTS records exposed in a store.</summary>
        public int MaximumObjects { get; }
        /// <summary>Gets the maximum tags enumerated from one supplied payload.</summary>
        public int MaximumPayloadTags { get; }
        /// <summary>Gets the maximum distinct records staged in one transaction.</summary>
        public int MaximumChanges { get; }
    }
}
