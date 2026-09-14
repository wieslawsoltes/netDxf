// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using netDxf.Header;

namespace netDxf.IO
{
    /// <summary>A single-threaded, disposable batch of schema-aware OBJECTS edits.</summary>
    /// <remarks>
    /// Each operation rolls back its own staging on failure. Commit returns a new raw snapshot and
    /// never changes the source document. No native object evaluation or implicit typed load/save is performed.
    /// </remarks>
    public sealed partial class DxfRawObjectTransaction : IDisposable
    {
        private sealed class Change
        {
            internal DxfRawRecord Source;
            internal List<DxfTag> Tags;
            internal DxfRawStoredObject Object;
            internal bool Deleted;
        }
        private readonly DxfRawObjectStore store;
        private readonly CancellationToken cancellation;
        private readonly Dictionary<ulong, Change> changes = new Dictionary<ulong, Change>();
        private readonly List<ulong> added = new List<ulong>();
        private readonly HashSet<ulong> reserved;
        private Dictionary<ulong, Change> undo;
        private List<ulong> allocations;
        private ulong next;
        private string root;
        private bool enableSortents;
        private bool closed;

        internal DxfRawObjectTransaction(DxfRawObjectStore store, CancellationToken cancellation)
        {
            this.store = store; this.cancellation = cancellation; cancellation.ThrowIfCancellationRequested();
            if (store.Document.Version < DxfVersion.AutoCad2000)
                throw new NotSupportedException("Schema-aware object editing currently requires AutoCAD 2000 or later; raw historical preservation remains separate.");
            foreach (DxfRawHandleDiagnostic diagnostic in store.Index.Diagnostics)
                if (diagnostic.Kind == DxfRawHandleDiagnosticKind.DuplicateIdentity ||
                    diagnostic.Kind == DxfRawHandleDiagnosticKind.MultipleIdentities || diagnostic.Kind == DxfRawHandleDiagnosticKind.NullIdentity ||
                    diagnostic.Kind == DxfRawHandleDiagnosticKind.MultipleOwners || diagnostic.Kind == DxfRawHandleDiagnosticKind.InvalidControlGroup ||
                    diagnostic.Kind == DxfRawHandleDiagnosticKind.OwnerCycle)
                    throw new InvalidDataException("Object editing requires unambiguous common identities and ownership: " + diagnostic.Message);
            // Reserve pointers and exposed opaque/arbitrary slots too: an allocation must not capture
            // a previously unresolved reference, even if no object currently defines that value.
            this.reserved = new HashSet<ulong>(store.Index.Occurrences.Where(o => o.Role != DxfRawHandleRole.HeaderSeed).Select(o => o.NumericHandle));
            var seeds = store.Index.Occurrences.Where(o => o.Role == DxfRawHandleRole.HeaderSeed).ToArray();
            if (seeds.Length > 1) throw new InvalidDataException("Multiple HANDSEED values are ambiguous.");
            this.next = seeds.Length == 0 || seeds[0].NumericHandle == 0 ? 1 : seeds[0].NumericHandle;
            this.root = store.RootDictionary == null ? null : store.RootDictionary.Handle;
        }

        /// <summary>Gets a staged schema object by handle, or the unchanged source object.</summary>
        public DxfRawStoredObject Get(string handle)
        {
            this.EnsureOpen();
            string canonical = DxfObjectText.Handle(handle, false);
            if (this.changes.TryGetValue(DxfObjectText.Number(canonical), out Change change))
            {
                if (change.Deleted) return null;
                return change.Object;
            }
            return this.store.Get(canonical);
        }
        private T Require<T>(string handle) where T : DxfRawStoredObject
        {
            DxfRawStoredObject value = this.Get(handle);
            if (value is DxfRawOpaqueStoredObject opaque) throw new NotSupportedException(opaque.Reason);
            return value as T ?? throw new ArgumentException("Handle is not an editable " + typeof(T).Name + ": " + handle, nameof(handle));
        }
        private void EnsureOpen()
        {
            if (this.closed) throw new ObjectDisposedException(nameof(DxfRawObjectTransaction));
            this.cancellation.ThrowIfCancellationRequested();
        }
        private T Apply<T>(Func<T> action)
        {
            this.EnsureOpen();
            if (this.undo != null) throw new InvalidOperationException("An object transaction cannot be reentered.");
            this.undo = new Dictionary<ulong, Change>(); this.allocations = new List<ulong>();
            ulong oldNext = this.next; int count = this.added.Count; string oldRoot = this.root; bool oldSortents = this.enableSortents;
            try { T result = action(); this.cancellation.ThrowIfCancellationRequested(); return result; }
            catch
            {
                foreach (KeyValuePair<ulong, Change> item in this.undo)
                    if (item.Value == null) this.changes.Remove(item.Key); else this.changes[item.Key] = item.Value;
                foreach (ulong handle in this.allocations) this.reserved.Remove(handle);
                this.added.RemoveRange(count, this.added.Count - count);
                this.next = oldNext; this.root = oldRoot; this.enableSortents = oldSortents;
                throw;
            }
            finally { this.undo = null; this.allocations = null; }
        }
        private void Stage(ulong key, Change value)
        {
            if (!this.changes.ContainsKey(key) && this.changes.Count == this.store.Options.MaximumChanges)
                throw new InvalidDataException("Object transaction change budget exceeded.");
            if (!this.undo.ContainsKey(key))
            { this.changes.TryGetValue(key, out Change prior); this.undo.Add(key, prior); }
            this.changes[key] = value;
        }
        private void Reserve(string handle)
        {
            ulong value = DxfObjectText.Number(DxfObjectText.Handle(handle, true));
            if (this.reserved.Add(value)) this.allocations.Add(value);
        }
        private void ReserveTags(IEnumerable<DxfTag> data)
        {
            foreach (DxfTag tag in data)
                if (tag.Code >= 320 && tag.Code <= 369) this.Reserve((string)tag.RawValue);
        }
        private string Allocate()
        {
            while (this.reserved.Contains(this.next))
            {
                this.cancellation.ThrowIfCancellationRequested();
                if (this.next == ulong.MaxValue) throw new InvalidOperationException("No representable handle/next-handle pair remains.");
                ++this.next;
            }
            if (this.next == ulong.MaxValue) throw new InvalidOperationException("A new object must leave a representable HANDSEED.");
            ulong value = this.next++; this.reserved.Add(value); this.allocations.Add(value);
            return value.ToString("X", CultureInfo.InvariantCulture);
        }
        private DxfRawStoredObject AddObject(string type, string owner, IEnumerable<DxfTag> body)
        {
            if (this.store.Objects.Count + this.added.Count >= this.store.Options.MaximumObjects)
                throw new InvalidDataException("OBJECTS record budget exceeded.");
            string handle = this.Allocate();
            var tags = new List<DxfTag> { new DxfTag(0, type), new DxfTag(5, handle), new DxfTag(330, owner) };
            tags.AddRange(body);
            DxfRawStoredObject value = DxfObjectSchema.Read(tags.AsReadOnly(), null, this.store.Options.MaximumPayloadTags);
            if (value is DxfRawOpaqueStoredObject opaque) throw new InvalidDataException(opaque.Reason);
            ulong key = DxfObjectText.Number(handle); this.Stage(key, new Change { Tags = tags, Object = value }); this.added.Add(key);
            return value;
        }
        private void Replace(DxfRawStoredObject original, List<DxfTag> tags)
        {
            if (SameTags(original.Tags, tags)) return;
            DxfRawStoredObject value = DxfObjectSchema.Read(tags.AsReadOnly(), original.SourceRecord, this.store.Options.MaximumPayloadTags);
            if (value is DxfRawOpaqueStoredObject opaque) throw new InvalidDataException(opaque.Reason);
            this.Stage(DxfObjectText.Number(original.Handle), new Change { Source = original.SourceRecord, Tags = tags, Object = value });
        }
        private static bool SameTags(IReadOnlyList<DxfTag> a, IReadOnlyList<DxfTag> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; ++i)
            {
                if (a[i].Code != b[i].Code) return false;
                if (a[i].RawValue is byte[] bytes)
                { if (!(b[i].RawValue is byte[] other) || !bytes.SequenceEqual(other)) return false; }
                else if (a[i].RawValue is double number)
                { if (BitConverter.DoubleToInt64Bits(number) != BitConverter.DoubleToInt64Bits((double)b[i].RawValue)) return false; }
                else if (!Equals(a[i].RawValue, b[i].RawValue)) return false;
            }
            return true;
        }
        private List<T> Collect<T>(IEnumerable<T> input, Action<T> validate)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var items = new List<T>();
            foreach (T item in input)
            {
                this.cancellation.ThrowIfCancellationRequested();
                if (items.Count == this.store.Options.MaximumPayloadTags) throw new InvalidDataException("Object payload budget exceeded.");
                validate(item); items.Add(item);
            }
            return items;
        }
        private void CheckNewKey(DxfRawDictionary parent, string name)
        {
            DxfObjectText.ValidateName(name);
            if (parent.Find(name) != null) throw new ArgumentException("A dictionary entry already exists: " + name, nameof(name));
        }
        private void WriteDictionary(DxfRawDictionary value, IEnumerable<DxfRawDictionaryEntry> entries,
            bool? hard, DxfDuplicateRecordCloning? cloning, string defaultHandle)
        { this.Replace(value, DxfObjectSchema.Wrap(value, DxfObjectSchema.DictionaryBody(value, entries, hard, cloning, defaultHandle))); }
        private void LinkNew(DxfRawDictionary parent, string name, DxfRawStoredObject child)
        {
            var entries = parent.Entries.ToList(); entries.Add(new DxfRawDictionaryEntry(name, child.Handle));
            this.WriteDictionary(parent, entries, parent.HardOwnerFlag, parent.CloningFlag, parent.DefaultHandle);
        }

        /// <summary>Returns the existing root dictionary or creates the first OBJECTS record.</summary>
        public string EnsureRootDictionary() { return this.Apply(this.EnsureRoot); }
        private string EnsureRoot()
        {
            if (this.root == null)
                this.root = this.AddObject("DICTIONARY", "0", new[] { new DxfTag(100, "AcDbDictionary"), new DxfTag(281, (short)1) }).Handle;
            return this.root;
        }

        /// <summary>Creates a named child dictionary. With-default dictionaries also receive an owned Default placeholder.</summary>
        public string CreateDictionary(string parentHandle, string name, bool hardOwner = true, bool withDefault = false)
        {
            return this.Apply(() =>
            {
                DxfRawDictionary parent = this.Require<DxfRawDictionary>(parentHandle); this.CheckNewKey(parent, name);
                var body = new List<DxfTag> { new DxfTag(100, "AcDbDictionary"), new DxfTag(280, (short)(hardOwner ? 1 : 0)), new DxfTag(281, (short)1) };
                if (withDefault) body.Add(new DxfTag(100, "AcDbDictionaryWithDefault"));
                var child = (DxfRawDictionary)this.AddObject(withDefault ? "ACDBDICTIONARYWDFLT" : "DICTIONARY", parent.Handle, body);
                if (withDefault)
                {
                    DxfRawStoredObject placeholder = this.AddObject("ACDBPLACEHOLDER", child.Handle, Array.Empty<DxfTag>());
                    this.WriteDictionary(child, new[] { new DxfRawDictionaryEntry("Default", placeholder.Handle) }, child.HardOwnerFlag, child.CloningFlag, placeholder.Handle);
                }
                this.LinkNew(parent, name, child); return child.Handle;
            });
        }
        /// <summary>Creates a named XRECORD. Payload tags are already-encoded application data, not XData.</summary>
        public string CreateXRecord(string parentHandle, string name, IEnumerable<DxfTag> data,
            DxfDuplicateRecordCloning cloning = DxfDuplicateRecordCloning.KeepExisting)
        {
            return this.Apply(() =>
            {
                var parent = this.Require<DxfRawDictionary>(parentHandle); this.CheckNewKey(parent, name);
                DxfObjectText.Cloning((short)cloning); var payload = this.Collect(data, DxfObjectSchema.ValidateXRecordTag); this.ReserveTags(payload);
                var body = new List<DxfTag> { new DxfTag(100, "AcDbXrecord"), new DxfTag(280, (short)cloning) }; body.AddRange(payload);
                DxfRawStoredObject child = this.AddObject("XRECORD", parent.Handle, body); this.LinkNew(parent, name, child); return child.Handle;
            });
        }
        /// <summary>Creates a named variable. Unicode text is escaped portably; schema zero is the published default.</summary>
        public string CreateVariable(string parentHandle, string name, string value, short schema = 0)
        {
            return this.Apply(() =>
            {
                var parent = this.Require<DxfRawDictionary>(parentHandle); this.CheckNewKey(parent, name);
                string text = DxfObjectText.Encode(value);
                var child = this.AddObject("DICTIONARYVAR", parent.Handle, new[] { new DxfTag(100, "DictionaryVariables"), new DxfTag(280, schema), new DxfTag(1, text) });
                this.LinkNew(parent, name, child); return child.Handle;
            });
        }
        /// <summary>Creates a named inert placeholder.</summary>
        public string CreatePlaceholder(string parentHandle, string name)
        {
            return this.Apply(() =>
            {
                var parent = this.Require<DxfRawDictionary>(parentHandle); this.CheckNewKey(parent, name);
                var child = this.AddObject("ACDBPLACEHOLDER", parent.Handle, Array.Empty<DxfTag>());
                this.LinkNew(parent, name, child); return child.Handle;
            });
        }
        /// <summary>Creates a named ordered soft-pointer IDBUFFER. Duplicates and nulls are retained.</summary>
        public string CreateIdBuffer(string parentHandle, string name, IEnumerable<string> handles)
        {
            return this.Apply(() =>
            {
                var parent = this.Require<DxfRawDictionary>(parentHandle); this.CheckNewKey(parent, name);
                List<string> data = this.Collect(handles, h => DxfObjectText.Handle(h, true));
                foreach (string item in data) this.Reserve(item);
                var body = new List<DxfTag> { new DxfTag(100, "AcDbIdBuffer") };
                body.AddRange(data.Select(h => new DxfTag(330, DxfObjectText.Handle(h, true))));
                var child = this.AddObject("IDBUFFER", parent.Handle, body); this.LinkNew(parent, name, child); return child.Handle;
            });
        }
        /// <summary>Replaces XRECORD payload and cloning policy while preserving common controls and following XData.</summary>
        public void SetXRecord(string handle, IEnumerable<DxfTag> data, DxfDuplicateRecordCloning cloning = DxfDuplicateRecordCloning.KeepExisting)
        {
            this.Apply(() =>
            {
                var value = this.Require<DxfRawXRecord>(handle); DxfObjectText.Cloning((short)cloning);
                var body = new List<DxfTag> { new DxfTag(100, "AcDbXrecord"), new DxfTag(280, (short)cloning) };
                var payload = this.Collect(data, DxfObjectSchema.ValidateXRecordTag);
                if (value.CloningFlag == cloning && SameTags(value.Data, payload)) return true;
                this.ReserveTags(payload); body.AddRange(payload);
                this.Replace(value, DxfObjectSchema.Wrap(value, body)); return true;
            });
        }
        /// <summary>Replaces variable value and schema presence; null omits the selected field rather than inventing a default.</summary>
        public void SetVariable(string handle, string value, short? schema = 0)
        {
            this.Apply(() =>
            {
                var original = this.Require<DxfRawDictionaryVariable>(handle);
                if (original.Value == value && original.SchemaNumber == schema) return true;
                var body = new List<DxfTag> { new DxfTag(100, "DictionaryVariables") };
                if (schema.HasValue) body.Add(new DxfTag(280, schema.Value));
                if (value != null) body.Add(new DxfTag(1, DxfObjectText.Encode(value)));
                this.Replace(original, DxfObjectSchema.Wrap(original, body)); return true;
            });
        }
        /// <summary>Replaces an ordered IDBUFFER without sorting or deduplicating pointers.</summary>
        public void SetIdBuffer(string handle, IEnumerable<string> handles)
        {
            this.Apply(() =>
            {
                var original = this.Require<DxfRawIdBuffer>(handle);
                var data = this.Collect(handles, h => DxfObjectText.Handle(h, true));
                if (original.Handles.SequenceEqual(data.Select(h => DxfObjectText.Handle(h, true)))) return true;
                foreach (string item in data) this.Reserve(item);
                var body = new List<DxfTag> { new DxfTag(100, "AcDbIdBuffer") };
                body.AddRange(data.Select(h => new DxfTag(330, DxfObjectText.Handle(h, true))));
                this.Replace(original, DxfObjectSchema.Wrap(original, body)); return true;
            });
        }
        /// <summary>Sets an explicit entry, retaining its position on replacement. The target must already belong to that dictionary.</summary>
        public void SetDictionaryEntry(string dictionaryHandle, string name, string targetHandle, bool hardOwner = false)
        {
            this.Apply(() =>
            {
                var dictionary = this.Require<DxfRawDictionary>(dictionaryHandle);
                var entry = new DxfRawDictionaryEntry(name, targetHandle, hardOwner);
                var target = this.Get(entry.Handle) ?? throw new ArgumentException("Dictionary target must be an existing OBJECTS record.");
                if (target.OwnerHandle != dictionary.Handle || target.Handle == dictionary.Handle)
                    throw new InvalidOperationException("A dictionary entry must target another object already owned by that dictionary.");
                var entries = dictionary.Entries.ToList(); int index = entries.FindIndex(e => StringComparer.OrdinalIgnoreCase.Equals(e.Name, name));
                if (index >= 0 && entries[index].Name == entry.Name && entries[index].Handle == entry.Handle && entries[index].IsHardOwner == entry.IsHardOwner) return true;
                if (index < 0) entries.Add(entry); else entries[index] = entry;
                this.WriteDictionary(dictionary, entries, dictionary.HardOwnerFlag, dictionary.CloningFlag, dictionary.DefaultHandle); return true;
            });
        }
        /// <summary>Renames an entry in place. Case-only renaming is allowed; a distinct conflicting name is not.</summary>
        public void RenameEntry(string dictionaryHandle, string name, string newName)
        {
            this.Apply(() =>
            {
                var value = this.Require<DxfRawDictionary>(dictionaryHandle); DxfObjectText.ValidateName(newName);
                var entry = value.Find(name) ?? throw new KeyNotFoundException("Dictionary name not found: " + name);
                if (entry.Name == newName) return true;
                var conflict = value.Find(newName);
                if (conflict != null && !ReferenceEquals(conflict, entry)) throw new ArgumentException("The new dictionary name already exists.");
                var entries = value.Entries.Select(e => ReferenceEquals(e, entry) ? new DxfRawDictionaryEntry(newName, e.Handle, e.IsHardOwner) : e).ToList();
                this.WriteDictionary(value, entries, value.HardOwnerFlag, value.CloningFlag, value.DefaultHandle); return true;
            });
        }
        /// <summary>Sets dictionary policy flags, including deliberate omission; never changes any child ownership.</summary>
        public void SetDictionaryFlags(string handle, bool? hardOwner, DxfDuplicateRecordCloning? cloning)
        {
            this.Apply(() =>
            {
                var value = this.Require<DxfRawDictionary>(handle);
                if (cloning.HasValue) DxfObjectText.Cloning((short)cloning.Value);
                if (value.HardOwnerFlag == hardOwner && value.CloningFlag == cloning) return true;
                this.WriteDictionary(value, value.Entries, hardOwner, cloning, value.DefaultHandle); return true;
            });
        }
        /// <summary>Sets a dictionary-with-default's explicit nonzero pointer to a stored object.</summary>
        public void SetDefault(string dictionaryHandle, string targetHandle)
        {
            this.Apply(() =>
            {
                var value = this.Require<DxfRawDictionary>(dictionaryHandle);
                if (!value.HasDefault) throw new ArgumentException("The object is not a dictionary with default.");
                string target = DxfObjectText.Handle(targetHandle, false);
                if (this.Get(target) == null) throw new ArgumentException("Default target must be an existing OBJECTS record.");
                if (value.DefaultHandle == target) return true;
                this.WriteDictionary(value, value.Entries, value.HardOwnerFlag, value.CloningFlag, target); return true;
            });
        }
        /// <summary>Unlinks an entry. Optional deletion requires exclusive, schema-known ownership and no incoming external references.</summary>
        public bool RemoveEntry(string dictionaryHandle, string name, bool deleteOwnedTree = false)
        {
            return this.Apply(() =>
            {
                var value = this.Require<DxfRawDictionary>(dictionaryHandle); var entry = value.Find(name);
                if (entry == null) return false;
                var target = this.Get(entry.Handle);
                if (deleteOwnedTree && (target == null || target.OwnerHandle != value.Handle))
                    throw new InvalidOperationException("The unlinked target is not owned by this dictionary.");
                this.WriteDictionary(value, value.Entries.Where(e => !ReferenceEquals(e, entry)), value.HardOwnerFlag, value.CloningFlag, value.DefaultHandle);
                if (deleteOwnedTree) this.DeleteTree(entry.Handle); return true;
            });
        }
        /// <summary>Discards all staging. The source document is never modified.</summary>
        public void Dispose()
        {
            if (this.undo != null) throw new InvalidOperationException("An object transaction cannot be disposed inside an operation.");
            this.closed = true; this.changes.Clear(); this.added.Clear();
        }
    }
}
