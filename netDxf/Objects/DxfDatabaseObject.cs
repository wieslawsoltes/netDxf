using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Objects
{
    /// <summary>A nongraphical OBJECTS record registered in a document database.</summary>
    public abstract class DxfDatabaseObject : DxfObject
    {
        internal DxfDatabaseObject(string codeName) : base(codeName) { }
        /// <summary>Gets the database containing this object, or null while detached or permanently erased.</summary>
        public DxfObjectDatabase Database { get; internal set; }
        /// <summary>Gets whether this object was permanently erased from its database.</summary>
        /// <remarks>Erased objects retain their original handles and payload for inspection. They cannot be registered, attached or cloned again.</remarks>
        public bool IsErased { get; internal set; }
        internal abstract DxfDatabaseObject CloneShell();
        internal virtual IEnumerable<DxfDatabaseObject> DeclaredOwnedObjects { get { yield break; } }
        internal virtual void MaterializeOwnedObjectReferences() { }
        internal virtual IEnumerable<DxfObject> DatabaseReferences { get { yield break; } }
        internal virtual IEnumerable<DxfTag> AllocationReservations { get { yield break; } }
        internal virtual void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve) { }
        internal virtual void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors) { }
        /// <summary>Rebinds added XData to a private copy before document registration can mutate an external registry.</summary>
        protected override void OnXDataAddAppRegEvent(ApplicationRegistry item)
        {
            if (this.Database != null)
            {
                this.XData.ReplaceForBinding(item.Name, (XData)this.XData[item.Name].Clone());
                foreach (XDataRecord tag in this.XData[item.Name].XDataRecord)
                    if (tag.Code == XDataCode.DatabaseHandle) this.Database.ReserveUnresolvedReference(new DxfTag(1005, tag.Value));
            }
            base.OnXDataAddAppRegEvent(item);
        }
    }

    /// <summary>A named link. Multiple names may link to the same target.</summary>
    public sealed class DxfDictionaryEntry
    {
        internal DxfDictionaryEntry(string name, DxfObject target, bool hardOwner)
        { this.Name = name; this.Target = target; this.IsHardOwner = hardOwner; }
        /// <summary>Gets the entry name.</summary>
        public string Name { get; }
        /// <summary>Gets the referenced object.</summary>
        public DxfObject Target { get; }
        /// <summary>Gets whether the link is encoded as hard ownership (360), rather than soft ownership (350).</summary>
        public bool IsHardOwner { get; }
    }

    /// <summary>A named, ordered dictionary of object references.</summary>
    /// <remarks>Adding a previously unowned object establishes its owner. Multiple aliases retain that same dictionary owner.</remarks>
    public class DxfDictionary : DxfDatabaseObject
    {
        private readonly List<DxfDictionaryEntry> entries = new List<DxfDictionaryEntry>();
        private readonly Dictionary<string, DxfDictionaryEntry> index = new Dictionary<string, DxfDictionaryEntry>(StringComparer.OrdinalIgnoreCase);
        private DictionaryCloningFlags cloning = DictionaryCloningFlags.KeepExisting;
        /// <summary>Creates a detached dictionary.</summary>
        public DxfDictionary() : this("DICTIONARY") { }
        internal DxfDictionary(string code) : base(code) { }
        /// <summary>Gets the ordered read-only entry list.</summary>
        public IReadOnlyList<DxfDictionaryEntry> Entries { get { return this.entries.AsReadOnly(); } }
        /// <summary>Gets the number of names, including aliases.</summary>
        public int Count { get { return this.entries.Count; } }
        /// <summary>Gets or sets group 280, the dictionary's hard-owner flag.</summary>
        /// <remarks>This flag is retained independently of each entry's 350/360 code.</remarks>
        public bool IsHardOwner { get; set; } = true;
        /// <summary>Gets or sets the duplicate-record cloning policy saved in group 281.</summary>
        public DictionaryCloningFlags Cloning
        {
            get { return this.cloning; }
            set { if ((int)value < 0 || (int)value > 5) throw new ArgumentOutOfRangeException(nameof(value)); this.cloning = value; }
        }
        /// <summary>Gets a target by name, or the configured default for dictionaries with a default.</summary>
        public DxfObject this[string name] { get { return this.TryGetValue(name, out DxfObject value) ? value : throw new KeyNotFoundException(name); } }
        /// <summary>Looks up a name, applying a configured default when the name is absent.</summary>
        public bool TryGetValue(string name, out DxfObject value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (this.index.TryGetValue(name, out DxfDictionaryEntry entry)) { value = entry.Target; return true; }
            value = (this as DxfDictionaryWithDefault)?.Default;
            return value != null;
        }
        /// <summary>Tests whether an actual entry exists; ignores any default.</summary>
        public bool Contains(string name) { return this.index.ContainsKey(name); }
        /// <summary>Adds a named link and registers any detached owned graph in this database.</summary>
        public void Add(string name, DxfObject target, bool hardOwner = true)
        {
            if (this.IsErased) throw new InvalidOperationException("An erased dictionary cannot adopt objects.");
            ValidateName(name);
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target is DxfDatabaseObject erased && erased.IsErased) throw new InvalidOperationException("An erased object cannot be attached again.");
            if (target is netDxf.Entities.EntityObject) throw new ArgumentException("Graphical entities cannot be dictionary entries; use XRECORD pointer data.", nameof(target));
            if (this.index.ContainsKey(name)) throw new ArgumentException("The dictionary already contains this name.", nameof(name));
            if (this.Database != null && this == this.Database.Root && DxfObjectDatabase.IsReservedName(name))
                throw new ArgumentException("This root entry is managed by an existing document collection.", nameof(name));
            if (DxfObjectDatabase.IsAncestor(target, this)) throw new ArgumentException("Dictionary ownership cannot form a cycle.", nameof(target));
            if (target.Owner != null && target.Owner != this) throw new ArgumentException("Dictionary aliases must retain one common owner, for both soft and hard ownership links.", nameof(target));
            if (!(target is DxfDatabaseObject) && (hardOwner || this.IsHardOwner)) throw new ArgumentException("Existing document objects can only be referenced by soft aliases.", nameof(target));
            if (this.Database != null) this.Database.PrepareTarget(target);
            else if (target is DxfDatabaseObject db && db.Database != null) throw new ArgumentException("A detached dictionary cannot adopt a registered object.", nameof(target));
            if (target is DxfDatabaseObject && target.Owner == null && target != this) target.Owner = this;
            this.AddLoaded(name, target, hardOwner);
        }
        /// <summary>Removes a name; does not erase the referenced object or change its database identity.</summary>
        /// <remarks>Use database validation to identify owned objects no longer reachable by name.</remarks>
        public bool Remove(string name)
        {
            if (!this.index.TryGetValue(name, out DxfDictionaryEntry entry)) return false;
            this.index.Remove(name); this.entries.Remove(entry); return true;
        }
        internal void AddLoaded(string name, DxfObject target, bool hardOwner)
        {
            ValidateName(name);
            if (this.index.ContainsKey(name)) throw new FormatException("Duplicate dictionary name: " + name);
            DxfDictionaryEntry entry = new DxfDictionaryEntry(name, target, hardOwner);
            this.index.Add(name, entry); this.entries.Add(entry);
        }
        internal static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Dictionary names must be nonempty single-line strings.", nameof(name));
        }
        internal override DxfDatabaseObject CloneShell() { return new DxfDictionary { IsHardOwner = this.IsHardOwner, Cloning = this.Cloning }; }
    }

    /// <summary>A dictionary with a group-340 fallback target (ACDBDICTIONARYWDFLT).</summary>
    public sealed class DxfDictionaryWithDefault : DxfDictionary
    {
        private DxfObject defaultObject;
        /// <summary>Creates a dictionary with no default.</summary>
        public DxfDictionaryWithDefault() : base("ACDBDICTIONARYWDFLT") { }
        /// <summary>Gets or sets the fallback object. A fallback is a hard pointer, not a second owner.</summary>
        public DxfObject Default
        {
            get { return this.defaultObject; }
            set
            {
                if (value is netDxf.Entities.EntityObject) throw new ArgumentException("A dictionary default must be a nongraphical object.", nameof(value));
                if (value != null && this.Database != null) this.Database.CheckRegistered(value);
                this.defaultObject = value;
            }
        }
        internal override DxfDatabaseObject CloneShell() { return new DxfDictionaryWithDefault { IsHardOwner = this.IsHardOwner, Cloning = this.Cloning }; }
    }

    /// <summary>Validated arbitrary application data carried in an XRECORD.</summary>
    public sealed partial class DxfXRecord : DxfDatabaseObject
    {
        private DictionaryCloningFlags cloning = DictionaryCloningFlags.KeepExisting;
        private readonly DxfXRecordData data;
        /// <summary>Creates an empty detached record.</summary>
        public DxfXRecord() : base("XRECORD") { this.data = new DxfXRecordData(this); }
        /// <summary>Gets ordered payload tags; duplicates and binary chunks are preserved.</summary>
        /// <remarks>Ordinary payloads are editable. Schema-managed ownership records reject generic payload edits; edit their typed owner instead.</remarks>
        public Collection<DxfTag> Data { get { return this.data; } }
        /// <summary>Gets or sets the group-280 duplicate-record cloning policy.</summary>
        public DictionaryCloningFlags Cloning
        {
            get { return this.cloning; }
            set { if ((int)value < 0 || (int)value > 5) throw new ArgumentOutOfRangeException(nameof(value)); this.cloning = value; }
        }
        internal override DxfDatabaseObject CloneShell()
        {
            DxfXRecord copy = new DxfXRecord { Cloning = this.Cloning };
            foreach (DxfTag tag in this.Data) copy.AddLoadedData(tag);
            return copy;
        }
        internal void AddLoadedData(DxfTag tag) { this.data.AddPreserved(tag); }
        internal void ReplaceLoadedData(int index, DxfTag tag) { this.data.SetPreserved(index, tag); }
        private sealed class DxfXRecordData : Collection<DxfTag>
        {
            private readonly DxfXRecord owner;
            internal DxfXRecordData(DxfXRecord owner) { this.owner = owner; }
            private void Reserve(DxfTag tag) { this.owner.Database?.ReserveUnresolvedReference(tag); }
            internal void AddPreserved(DxfTag item) { Check(item, true); base.InsertItem(this.Count, item); }
            internal void SetPreserved(int index, DxfTag item) { Check(item, true); base.SetItem(index, item); }
            protected override void InsertItem(int index, DxfTag item) { if (index < 0 || index > this.Count) throw new ArgumentOutOfRangeException(nameof(index)); this.owner.CheckPayloadEditable(); Check(item, false); this.Reserve(item); base.InsertItem(index, item); }
            protected override void SetItem(int index, DxfTag item) { if (index < 0 || index >= this.Count) throw new ArgumentOutOfRangeException(nameof(index)); this.owner.CheckPayloadEditable(); Check(item, false); this.Reserve(item); base.SetItem(index, item); }
            protected override void RemoveItem(int index) { this.owner.CheckPayloadEditable(); base.RemoveItem(index); }
            protected override void ClearItems() { this.owner.CheckPayloadEditable(); base.ClearItems(); }
            private static void Check(DxfTag tag, bool preserved)
            {
                if (tag == null) throw new ArgumentNullException(nameof(tag));
                if (!preserved && tag.ValueType == DxfTagValueType.BinaryData && ((byte[])tag.Value).Length > 127)
                    throw new ArgumentException("An authored XRECORD binary chunk cannot exceed 127 bytes.", nameof(tag));
                if (tag.Code <= 0 || tag.Code >= 1000 || tag.Code == 5 || tag.Code == 105 || tag.Code == 999 || !preserved && tag.Code > 369)
                    throw new ArgumentException("Authored XRECORD data uses group codes 1 through 369, excluding 5 and 105.", nameof(tag));
            }
        }
    }

    /// <summary>A DICTIONARYVAR containing a schema number and string value.</summary>
    public sealed class DxfDictionaryVariable : DxfDatabaseObject
    {
        private string value = string.Empty;
        private short schema;
        /// <summary>Creates an empty variable.</summary>
        public DxfDictionaryVariable() : base("DICTIONARYVAR") { }
        /// <summary>Gets or sets the group-280 schema number.</summary>
        public short Schema
        {
            get { return this.schema; }
            set { if (value < 0 || value > 255) throw new ArgumentOutOfRangeException(nameof(value)); this.schema = value; }
        }
        /// <summary>Gets or sets the group-1 string value.</summary>
        public string Value
        {
            get { return this.value; }
            set { if (value == null) throw new ArgumentNullException(nameof(value)); if (value.IndexOf('\0') >= 0) throw new ArgumentException("NUL is not permitted.", nameof(value)); this.value = value; }
        }
        internal override DxfDatabaseObject CloneShell() { return new DxfDictionaryVariable { Schema = this.Schema, Value = this.Value }; }
    }

    /// <summary>An ACDBPLACEHOLDER object suitable for a dictionary default entry.</summary>
    public sealed class DxfPlaceholder : DxfDatabaseObject
    {
        /// <summary>Creates a detached placeholder.</summary>
        public DxfPlaceholder() : base("ACDBPLACEHOLDER") { }
        internal override DxfDatabaseObject CloneShell() { return new DxfPlaceholder(); }
    }
}
