using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>Manages document registration, ownership, extension dictionaries, validation and graph cloning.</summary>
    public sealed partial class DxfObjectDatabase
    {
        private static readonly IEqualityComparer<DxfObject> ObjectIdentity = new ObjectIdentityComparer();
        private sealed class ObjectIdentityComparer : IEqualityComparer<DxfObject>
        {
            public bool Equals(DxfObject first, DxfObject second) { return ReferenceEquals(first, second); }
            public int GetHashCode(DxfObject value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); }
        }
        private readonly Dictionary<string, DxfDatabaseObject> objects = new Dictionary<string, DxfDatabaseObject>(StringComparer.OrdinalIgnoreCase);
        internal DxfObjectDatabase(DxfDocument document)
        {
            this.Document = document;
            this.Root = new DxfDictionary { Owner = document };
            this.Register(this.Root, false);
        }
        /// <summary>Gets the document containing this database.</summary>
        public DxfDocument Document { get; }
        /// <summary>Gets the named object dictionary's application entries.</summary>
        public DxfDictionary Root { get; private set; }
        /// <summary>Gets a snapshot of all registered typed database objects, including the root.</summary>
        public IReadOnlyList<DxfDatabaseObject> Items { get { return this.objects.Values.ToList().AsReadOnly(); } }
        /// <summary>Attaches a hard-owned extension dictionary to a registered entity, table record or object.</summary>
        public void SetExtensionDictionary(DxfObject owner, DxfDictionary dictionary)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));
            this.CheckRegistered(owner);
            if (owner == this.Document.Layers) throw new InvalidOperationException("The LAYER table extension dictionary is managed by the layer-state collection.");
            if (owner.ExtensionDictionary != null && owner.ExtensionDictionary != dictionary)
                throw new InvalidOperationException("An extension dictionary is already attached.");
            if (dictionary.Owner != null && dictionary.Owner != owner) throw new ArgumentException("The dictionary already has an owner.", nameof(dictionary));
            if (IsAncestor(dictionary, owner)) throw new ArgumentException("Extension dictionary ownership cannot form a cycle.", nameof(dictionary));
            this.PrepareTarget(dictionary);
            dictionary.Owner = owner;
            owner.ExtensionDictionary = dictionary;
        }
        /// <summary>Checks ownership, object identities and all typed references without modifying the document.</summary>
        /// <returns>A stable list of diagnostic descriptions; an empty list means validation succeeded.</returns>
        public IReadOnlyList<string> Validate()
        {
            List<string> errors = new List<string>();
            foreach (DxfDatabaseObject item in this.objects.Values)
            {
                item.ValidateDatabaseSchema(this, errors);
                this.ValidateDeclaredOwnership(item, this.objects.Values, errors, true);
                foreach (DxfObject reference in item.DatabaseReferences)
                    if (reference != null && !this.IsRegistered(reference)) errors.Add("Unregistered " + item.CodeName + " reference: " + item.Handle);
                if (item.Database != this || this.Document.GetObjectByHandle(item.Handle) != item) errors.Add("Object registration mismatch: " + item.Handle);
                if (item != this.Root && item.Owner == null) errors.Add("Object has no owner: " + item.Handle);
                if (item.Owner != null && !this.IsRegistered(item.Owner)) errors.Add("Owner is outside the document: " + item.Handle);
                if (item.Owner is DxfDictionary parent && !parent.Entries.Any(e => e.Target == item) && parent.ExtensionDictionary != item)
                    errors.Add("Owned object has no owning dictionary entry: " + item.Handle);
                HashSet<DxfObject> ancestors = new HashSet<DxfObject>(ObjectIdentity);
                for (DxfObject owner = item; owner != null; owner = owner.Owner)
                    if (!ancestors.Add(owner)) { errors.Add("Ownership cycle: " + item.Handle); break; }
                if (item is DxfDictionary dictionary)
                {
                    foreach (DxfDictionaryEntry entry in dictionary.Entries)
                    {
                        if (!this.IsRegistered(entry.Target)) errors.Add("Unregistered dictionary target: " + entry.Name);
                        if (entry.Target is netDxf.Entities.EntityObject) errors.Add("Graphical entity used as dictionary entry: " + entry.Name);
                        if (entry.Target.Owner != item) errors.Add("Dictionary ownership mismatch: " + entry.Name);
                    }
                }
                if (item is DxfDictionaryWithDefault fallback && fallback.Default != null && !this.IsRegistered(fallback.Default)) errors.Add("Unregistered dictionary default: " + item.Handle);
                if (item is DxfXRecord record)
                    foreach (DxfTag tag in record.Data)
                        if (IsReference(tag) && (string)tag.Value != "0" && this.Document.GetObjectByHandle((string)tag.Value) == null)
                            errors.Add("Unresolved XRECORD reference " + tag.Code + ": " + tag.Value);
            }
            foreach (DxfObject item in this.Document.AddedObjects.Values)
            {
                if (item.ExtensionDictionary != null && (!this.IsRegistered(item.ExtensionDictionary) || item.ExtensionDictionary.Owner != item)) errors.Add("Invalid extension dictionary: " + item.Handle);
                foreach (DxfObject reactor in item.PersistentReactors)
                    if (reactor == null || !this.IsRegistered(reactor)) errors.Add("Unregistered persistent reactor: " + item.Handle);
                if (item is DxfDatabaseObject)
                    foreach (XData data in item.XData.Values)
                        foreach (XDataRecord tag in data.XDataRecord)
                            if (tag.Code == XDataCode.DatabaseHandle && (string)tag.Value != "0" && this.Document.GetObjectByHandle((string)tag.Value) == null) errors.Add("Unresolved XData reference: " + tag.Value);
            }
            return errors.AsReadOnly();
        }
        /// <summary>Clones an ownership subtree with two-pass reference remapping and registers it under a new name.</summary>
        /// <param name="source">The registered dictionary whose complete ownership subtree will be copied.</param>
        /// <param name="destination">A dictionary in this database.</param>
        /// <param name="name">A new name in the destination.</param>
        /// <param name="externalReferences">Explicit replacements for references outside the subtree when copying across documents.</param>
        /// <returns>The new root dictionary, independent of the source graph.</returns>
        /// <remarks>Aliases and internal handle references are remapped. Group 320–329 arbitrary handles are retained verbatim. Cross-document external references require a mapping.</remarks>
        public DxfDictionary Clone(DxfDictionary source, DxfDictionary destination, string name,
            IReadOnlyDictionary<DxfObject, DxfObject> externalReferences = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (source.Database == null) throw new ArgumentException("The source must be registered.", nameof(source));
            if (destination.Database != this) throw new ArgumentException("The destination belongs to another database.", nameof(destination));
            DxfDictionary.ValidateName(name);
            if (destination.Contains(name) || destination == this.Root && IsReservedName(name)) throw new ArgumentException("The destination name already exists or is reserved.", nameof(name));
            return this.CloneDictionaryGraph(source, destination, name, false, externalReferences);
        }
        private DxfDictionary CloneDictionaryGraph(DxfDictionary source, DxfObject destination, string name, bool extension, IReadOnlyDictionary<DxfObject, DxfObject> externalReferences)
        { return (DxfDictionary)this.CloneOwnershipGraph(source, destination, name, extension, externalReferences); }
        private DxfDatabaseObject CloneOwnershipGraph(DxfDatabaseObject source, DxfObject destination, string name, bool extension, IReadOnlyDictionary<DxfObject, DxfObject> externalReferences)
        {
            // Enumerating caller mappings can run application code. Snapshot it before reading
            // graph state and recheck the destination slot after the final external callback.
            var externalMap = new Dictionary<DxfObject, DxfObject>(ObjectIdentity);
            if (externalReferences != null)
                foreach (KeyValuePair<DxfObject, DxfObject> pair in externalReferences) externalMap.Add(pair.Key, pair.Value);
            this.CheckRegistered(destination);
            if (extension)
            {
                if (destination == this.Document.Layers || destination.ExtensionDictionary != null) throw new InvalidOperationException("The destination extension-dictionary slot is occupied or reserved.");
            }
            else
            {
                DxfDictionary dictionary = (DxfDictionary)destination;
                if (dictionary.Database != this || dictionary.Contains(name) || dictionary == this.Root && IsReservedName(name)) throw new ArgumentException("The destination name already exists or is reserved.", nameof(name));
            }
            if (source.IsErased || source.Database == null) throw new InvalidOperationException("The clone source must remain registered and cannot be erased.");
            source.Database.CheckRegistered(source);
            IReadOnlyList<string> sourceErrors = source.Database.Validate();
            if (sourceErrors.Count > 0) throw new InvalidOperationException("Cannot clone an invalid source graph: " + string.Join("; ", sourceErrors));
            List<DxfDatabaseObject> originals = source.Database.objects.Values.Where(o => o == source || IsAncestor(source, o)).ToList();
            Dictionary<DxfObject, DxfObject> map = new Dictionary<DxfObject, DxfObject>(ObjectIdentity);
            foreach (DxfDatabaseObject original in originals) map.Add(original, original.CloneShell());
            Func<DxfObject, DxfObject> resolve = value =>
            {
                if (value == null) return null;
                if (map.TryGetValue(value, out DxfObject clone)) return clone;
                if (externalMap.TryGetValue(value, out DxfObject replacement)) { this.CheckRegistered(replacement); return replacement; }
                if (source.Database == this) return value;
                throw new InvalidOperationException("A reference outside the cloned graph needs an explicit destination mapping: " + value.Handle);
            };
            // Complete validation and reference linking before registering anything in the destination.
            foreach (DxfDatabaseObject original in originals)
            {
                DxfDatabaseObject clone = (DxfDatabaseObject)map[original];
                clone.Owner = original == source ? destination : resolve(original.Owner);
                if (original is DxfDictionary dictionary)
                    foreach (DxfDictionaryEntry entry in dictionary.Entries) ((DxfDictionary)clone).AddLoaded(entry.Name, resolve(entry.Target), entry.IsHardOwner);
                if (original is DxfDictionaryWithDefault fallback) ((DxfDictionaryWithDefault)clone).Default = resolve(fallback.Default);
                clone.ExtensionDictionary = (DxfDictionary)resolve(original.ExtensionDictionary);
                foreach (DxfObject reactor in original.PersistentReactors) clone.PersistentReactors.Add(resolve(reactor));
                original.CopyDatabaseReferencesTo(clone, resolve);
                foreach (XData data in original.XData.Values)
                {
                    clone.XData.Add((XData)data.Clone());
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && (string)tag.Value != "0")
                        {
                            DxfObject target = source.Database.Document.GetObjectByHandle((string)tag.Value);
                            if (target == null) throw new InvalidOperationException("Cannot clone an unresolved XData reference: " + tag.Value);
                            resolve(target);
                        }
                }
                if (original is DxfXRecord record)
                    foreach (DxfTag tag in record.Data)
                        if (IsReference(tag) && (string)tag.Value != "0")
                        {
                            DxfObject target = source.Database.Document.GetObjectByHandle((string)tag.Value);
                            if (target == null) throw new InvalidOperationException("Cannot clone an unresolved XRECORD reference: " + tag.Value);
                            resolve(target);
                        }
            }
            List<string> cloneErrors = new List<string>();
            foreach (DxfDatabaseObject original in originals) ((DxfDatabaseObject)map[original]).ValidateDatabaseSchema(this, cloneErrors);
            if (cloneErrors.Count > 0) throw new InvalidOperationException("Invalid cloned object schema: " + string.Join("; ", cloneErrors));
            this.PlanHandleAllocation(originals.Select(o => (DxfDatabaseObject)map[o]));
            foreach (DxfDatabaseObject original in originals) this.Register((DxfDatabaseObject)map[original], false);
            foreach (DxfDatabaseObject original in originals) ((DxfDatabaseObject)map[original]).MaterializeOwnedObjectReferences();
            foreach (DxfXRecord original in originals.OfType<DxfXRecord>())
            {
                DxfXRecord clone = (DxfXRecord)map[original];
                for (int i = 0; i < original.Data.Count; i++)
                {
                    DxfTag tag = original.Data[i];
                    if (IsReference(tag) && (string)tag.Value != "0") clone.ReplaceLoadedData(i, new DxfTag(tag.Code, resolve(source.Database.Document.GetObjectByHandle((string)tag.Value)).Handle));
                }
            }
            foreach (DxfDatabaseObject original in originals)
            {
                DxfDatabaseObject clone = (DxfDatabaseObject)map[original];
                foreach (XData data in original.XData.Values)
                    for (int i = 0; i < data.XDataRecord.Count; i++)
                    {
                        XDataRecord tag = data.XDataRecord[i];
                        if (tag.Code == XDataCode.DatabaseHandle && (string)tag.Value != "0")
                            clone.XData[data.ApplicationRegistry.Name].XDataRecord[i] = new XDataRecord(XDataCode.DatabaseHandle, resolve(source.Database.Document.GetObjectByHandle((string)tag.Value)).Handle);
                    }
            }
            DxfDatabaseObject result = (DxfDatabaseObject)map[source];
            if (extension) destination.ExtensionDictionary = (DxfDictionary)result;
            else ((DxfDictionary)destination).AddLoaded(name, result, true);
            return result;
        }
        internal static bool IsReference(DxfTag tag) { return tag.HandleKind == DxfHandleKind.SoftPointer || tag.HandleKind == DxfHandleKind.HardPointer || tag.HandleKind == DxfHandleKind.SoftOwner || tag.HandleKind == DxfHandleKind.HardOwner; }
        internal static bool IsAncestor(DxfObject possibleAncestor, DxfObject item)
        {
            HashSet<DxfObject> seen = new HashSet<DxfObject>(ObjectIdentity);
            for (DxfObject current = item; current != null && seen.Add(current); current = current.Owner)
                if (current == possibleAncestor) return true;
            return false;
        }
        internal bool IsRegistered(DxfObject item) { return item != null && item.Handle != null && this.Document.GetObjectByHandle(item.Handle) == item; }
        internal void CheckRegistered(DxfObject item)
        { if (!this.IsRegistered(item)) throw new ArgumentException("The referenced object must be registered in this document.", nameof(item)); }
        internal void PrepareTarget(DxfObject target)
        {
            if (!(target is DxfDatabaseObject databaseObject)) { this.CheckRegistered(target); return; }
            HashSet<DxfDatabaseObject> found = new HashSet<DxfDatabaseObject>();
            Stack<DxfDatabaseObject> pending = new Stack<DxfDatabaseObject>(); pending.Push(databaseObject);
            while (pending.Count > 0)
            {
                DxfDatabaseObject item = pending.Pop();
                if (item.IsErased) throw new InvalidOperationException("An erased object cannot be registered again.");
                if (!found.Add(item)) continue;
                if (item.Database != null && item.Database != this) throw new ArgumentException("Cannot link objects from different documents.", nameof(target));
                if (item is DxfDictionary dictionary)
                    foreach (DxfDictionaryEntry entry in dictionary.Entries)
                    {
                        if (entry.Target is DxfDatabaseObject child) pending.Push(child);
                        else this.CheckRegistered(entry.Target);
                    }
                foreach (DxfDatabaseObject child in item.DeclaredOwnedObjects)
                {
                    if (child == null) throw new ArgumentException("A declared ownership slot cannot be null.", nameof(target));
                    pending.Push(child);
                }
                if (item.ExtensionDictionary != null) pending.Push(item.ExtensionDictionary);
            }
            foreach (DxfDatabaseObject item in found)
            {
                if (item.Owner is DxfDatabaseObject owner && owner.Database == null && !found.Contains(owner)) throw new ArgumentException("The detached target is owned outside the adopted graph.", nameof(target));
                if (item is DxfDictionaryWithDefault fallback && fallback.Default != null && !this.IsRegistered(fallback.Default) && !(fallback.Default is DxfDatabaseObject f && found.Contains(f))) throw new ArgumentException("The default is outside the adopted graph.", nameof(target));
                foreach (DxfObject reference in item.DatabaseReferences)
                    if (reference != null && !this.IsRegistered(reference) && !(reference is DxfDatabaseObject owned && found.Contains(owned))) throw new ArgumentException("An object reference is outside the adopted graph.", nameof(target));
                foreach (DxfObject reactor in item.PersistentReactors)
                    if (!this.IsRegistered(reactor) && !(reactor is DxfDatabaseObject r && found.Contains(r))) throw new ArgumentException("A reactor is outside the adopted graph.", nameof(target));
            }
            List<string> ownershipErrors = new List<string>();
            foreach (DxfDatabaseObject item in found) this.ValidateDeclaredOwnership(item, found, ownershipErrors, false);
            if (ownershipErrors.Count != 0) throw new ArgumentException("Invalid declared ownership: " + string.Join("; ", ownershipErrors), nameof(target));
            this.PlanHandleAllocation(found.Where(o => o.Database == null));
            foreach (DxfDatabaseObject item in found) if (item.Database == null) this.Register(item, false);
            foreach (DxfDatabaseObject item in found) item.MaterializeOwnedObjectReferences();
        }
        private void PlanHandleAllocation(IEnumerable<DxfDatabaseObject> objectsToAdd)
        {
            List<DxfDatabaseObject> incoming = objectsToAdd.ToList();
            long candidate = this.Document.NumHandles;
            HashSet<string> registrations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DxfDatabaseObject item in incoming)
            {
                if (item is DxfXRecord record) foreach (DxfTag tag in record.Data) candidate = this.GetReservedSeed(tag, candidate);
                foreach (DxfTag tag in item.AllocationReservations) candidate = this.GetReservedSeed(tag, candidate);
                foreach (XData data in item.XData.Values)
                {
                    if (!this.Document.ApplicationRegistries.Contains(data.ApplicationRegistry.Name)) registrations.Add(data.ApplicationRegistry.Name);
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle) candidate = this.GetReservedSeed(new DxfTag(1005, tag.Value), candidate);
                }
            }
            if (candidate <= 0 || candidate > long.MaxValue - incoming.Count - registrations.Count)
                throw new InvalidOperationException("The document handle range is exhausted.");
            this.Document.NumHandles = candidate;
        }
        internal void ReserveUnresolvedReference(DxfTag tag)
        {
            this.Document.NumHandles = this.GetReservedSeed(tag, this.Document.NumHandles);
        }
        private long GetReservedSeed(DxfTag tag, long current)
        {
            if (tag.ValueType != DxfTagValueType.Handle || tag.HandleKind == DxfHandleKind.ObjectIdentity || (string)tag.Value == "0" || this.Document.GetObjectByHandle((string)tag.Value) != null) return current;
            if (!long.TryParse((string)tag.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long handle) || handle < 0) return current;
            if (handle >= long.MaxValue - 1) throw new ArgumentException("The typed database cannot allocate beyond the exposed reference handle.", nameof(tag));
            return handle >= current ? handle + 1 : current;
        }
        internal void Register(DxfDatabaseObject item, bool preserveHandle)
        {
            if (item.IsErased) throw new InvalidOperationException("An erased object cannot be registered again.");
            if (preserveHandle)
            {
                if (string.IsNullOrEmpty(item.Handle) || item.Handle == "0" || this.Document.GetObjectByHandle(item.Handle) != null) throw new FormatException("Duplicate or invalid database object handle: " + item.Handle);
                if (!long.TryParse(item.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long handle) || handle < 0 || handle == long.MaxValue) throw new FormatException("Unsupported object handle: " + item.Handle);
                if (handle >= this.Document.NumHandles) this.Document.NumHandles = handle + 1;
            }
            else
            {
                if (this.Document.NumHandles <= 0 || this.Document.NumHandles == long.MaxValue) throw new InvalidOperationException("The document handle range is exhausted.");
                while (this.Document.GetObjectByHandle(this.Document.NumHandles.ToString("X", CultureInfo.InvariantCulture)) != null) this.Document.NumHandles++;
                this.Document.NumHandles = item.AssignHandle(this.Document.NumHandles);
            }
            // XData may have been shared with a foreign document; never transfer its application registry.
            foreach (XData data in item.XData.Values.ToList()) item.XData.ReplaceForBinding(data.ApplicationRegistry.Name, (XData)data.Clone());
            item.Database = this;
            this.objects.Add(item.Handle, item);
            this.Document.AddedObjects.Add(item.Handle, item);
        }
        internal void ReplaceRoot(DxfDictionary root)
        {
            this.objects.Remove(this.Root.Handle);
            this.Document.AddedObjects.Remove(this.Root.Handle);
            this.Root.Database = null;
            this.Root = root; root.Owner = this.Document;
            this.Register(root, true);
        }
        internal static bool IsReservedName(string name)
        {
            switch (name.ToUpperInvariant())
            {
                case "ACAD_GROUP": case "ACAD_LAYOUT": case "ACAD_MLINESTYLE": case "ACAD_IMAGE_DICT":
                case "ACAD_IMAGE_VARS": case "ACAD_DGNDEFINITIONS": case "ACAD_DWFDEFINITIONS": case "ACAD_PDFDEFINITIONS": return true;
                default: return false;
            }
        }
    }
}
