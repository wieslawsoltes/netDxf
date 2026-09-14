// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using netDxf.Header;

namespace netDxf.IO
{
    public sealed partial class DxfRawObjectTransaction
    {
        private Change Record(string handle)
        {
            string canonical = DxfObjectText.Handle(handle, false); ulong key = DxfObjectText.Number(canonical);
            if (this.changes.TryGetValue(key, out Change change))
            { if (change.Deleted) throw new KeyNotFoundException("Record was deleted: " + handle); return change; }
            IReadOnlyList<DxfRawHandleOccurrence> definitions = this.store.Index.FindDefinitions(canonical);
            if (definitions.Count != 1 || definitions[0].Record == null) throw new ArgumentException("A unique record identity is required: " + handle);
            DxfRawRecord source = definitions[0].Record;
            return new Change { Source = source, Tags = source.Tags.ToList(), Object = source.SectionName == "OBJECTS" ? this.Get(canonical) : null };
        }
        private static int CommonEnd(IReadOnlyList<DxfTag> tags)
        {
            int depth = 0;
            for (int i = 1; i < tags.Count; ++i)
            {
                DxfTag t = tags[i];
                if (t.Code == 102)
                {
                    string text = (string)t.RawValue;
                    if (text.StartsWith("{", StringComparison.Ordinal)) ++depth;
                    else if (text == "}" && depth > 0) --depth;
                    else throw new InvalidDataException("Malformed common control group.");
                }
                else if (depth == 0 && (t.Code == 100 || t.Code == 101 || t.Code == 1001)) return i;
            }
            if (depth != 0) throw new InvalidDataException("Unterminated common control group.");
            return tags.Count;
        }
        private static Tuple<int, int, string> Extension(IReadOnlyList<DxfTag> tags)
        {
            int end = CommonEnd(tags), depth = 0, start = -1;
            string target = null; Tuple<int, int, string> result = null;
            for (int i = 1; i < end; ++i)
            {
                DxfTag t = tags[i];
                if (t.Code == 102)
                {
                    string text = (string)t.RawValue;
                    if (text.StartsWith("{", StringComparison.Ordinal))
                    {
                        if (depth == 0 && text == "{ACAD_XDICTIONARY") { start = i; target = null; }
                        else if (start >= 0) throw new NotSupportedException("Nested extension dictionary controls are not inferred.");
                        ++depth;
                    }
                    else
                    {
                        --depth;
                        if (depth == 0 && start >= 0)
                        {
                            if (result != null || target == null) throw new InvalidDataException("An extension dictionary requires one group-360 reference.");
                            result = Tuple.Create(start, i + 1, target); start = -1;
                        }
                    }
                }
                else if (start >= 0 && t.Code != 999)
                {
                    if (t.Code != 360 || target != null) throw new NotSupportedException("Unsupported extension dictionary slot.");
                    target = DxfObjectText.Handle((string)t.RawValue, false);
                }
            }
            return result;
        }
        private void ReplaceRecord(string handle, Change original, List<DxfTag> tags)
        {
            if (SameTags(original.Tags, tags)) return;
            if (original.Object != null) { this.Replace(original.Object, tags); return; }
            this.Stage(DxfObjectText.Number(handle), new Change { Source = original.Source, Tags = tags });
        }

        /// <summary>Gets or creates a hard-owned extension dictionary on an existing unique record.</summary>
        /// <remarks>Only the recognized common control group is edited; unrelated entity, reactor and private tags are retained.</remarks>
        public string EnsureExtensionDictionary(string ownerHandle)
        { return this.Apply(() => this.EnsureExtension(ownerHandle)); }
        private string EnsureExtension(string ownerHandle)
        {
            string owner = DxfObjectText.Handle(ownerHandle, false); Change original = this.Record(owner);
            if (original.Source != null && original.Source.SectionName != "OBJECTS" && original.Source.SectionName != "ENTITIES" &&
                original.Source.SectionName != "BLOCKS" && original.Source.SectionName != "TABLES")
                throw new NotSupportedException("Extension dictionaries require an object, table record or entity.");
            Tuple<int, int, string> existing = Extension(original.Tags);
            if (existing != null)
            {
                var dictionary = this.Require<DxfRawDictionary>(existing.Item3);
                if (dictionary.OwnerHandle != owner) throw new InvalidDataException("Extension dictionary owner mismatch.");
                return dictionary.Handle;
            }
            this.EnsureRoot();
            var child = this.AddObject("DICTIONARY", owner, new[] { new DxfTag(100, "AcDbDictionary"), new DxfTag(280, (short)1), new DxfTag(281, (short)1) });
            // EnsureRoot may have changed the very same root record.
            original = this.Record(owner);
            var tags = original.Tags.ToList();
            tags.InsertRange(CommonEnd(tags), new[] { new DxfTag(102, "{ACAD_XDICTIONARY"), new DxfTag(360, child.Handle), new DxfTag(102, "}") });
            this.ReplaceRecord(owner, original, tags); return child.Handle;
        }
        /// <summary>Detaches a common extension dictionary and optionally deletes its exclusively owned known subtree.</summary>
        public bool RemoveExtensionDictionary(string ownerHandle, bool deleteOwnedTree = false)
        {
            return this.Apply(() =>
            {
                string owner = DxfObjectText.Handle(ownerHandle, false); Change original = this.Record(owner);
                var extension = Extension(original.Tags); if (extension == null) return false;
                var dictionary = this.Require<DxfRawDictionary>(extension.Item3);
                if (dictionary.OwnerHandle != owner) throw new InvalidDataException("Extension dictionary owner mismatch.");
                var tags = original.Tags.ToList(); tags.RemoveRange(extension.Item1, extension.Item2 - extension.Item1);
                this.ReplaceRecord(owner, original, tags);
                if (deleteOwnedTree) this.DeleteTree(extension.Item3); return true;
            });
        }

        /// <summary>Creates or replaces the block's ACAD_SORTENTS table and enables the HEADER regeneration sort flag.</summary>
        /// <remarks>The conservative export profile starts at AutoCAD 2004. No geometry or visible rendering is evaluated.</remarks>
        public string SetDrawOrder(string blockRecordHandle, IEnumerable<DxfRawSortOrderEntry> entries, bool enableRegeneration = true)
        {
            return this.Apply(() =>
            {
                if (this.store.Document.Version < DxfVersion.AutoCad2004)
                    throw new NotSupportedException("SORTENTSTABLE authoring requires the conservative AutoCAD 2004+ profile.");
                string block = DxfObjectText.Handle(blockRecordHandle, false); Change blockRecord = this.Record(block);
                if (!Equals(blockRecord.Tags[0].RawValue, "BLOCK_RECORD")) throw new ArgumentException("Draw order requires a BLOCK_RECORD handle.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var order = this.Collect(entries, e =>
                {
                    if (e == null) throw new ArgumentException("A redraw entry cannot be null.");
                    if (!seen.Add(e.EntityHandle)) throw new ArgumentException("Duplicate redraw entity handle.");
                    this.Reserve(e.SortHandle);
                    Change entity = this.Record(e.EntityHandle);
                    if (entity.Source == null || (entity.Source.SectionName != "ENTITIES" && entity.Source.SectionName != "BLOCKS"))
                        throw new ArgumentException("A redraw entry must identify a graphic entity.");
                    var owners = this.store.Index.GetOccurrences(entity.Source).Where(o => o.Role == DxfRawHandleRole.Owner).ToArray();
                    if (owners.Length != 1 || owners[0].CanonicalHandle != block) throw new ArgumentException("A redraw entity belongs to another block.");
                });
                string ext = this.EnsureExtension(block); var dictionary = this.Require<DxfRawDictionary>(ext);
                var body = new List<DxfTag> { new DxfTag(100, "AcDbSortentsTable"), new DxfTag(330, block) };
                foreach (var entry in order) { body.Add(new DxfTag(331, entry.EntityHandle)); body.Add(new DxfTag(5, entry.SortHandle)); }
                var link = dictionary.Find("ACAD_SORTENTS"); string result;
                if (link == null)
                {
                    var child = this.AddObject("SORTENTSTABLE", ext, body); this.LinkNew(dictionary, "ACAD_SORTENTS", child); result = child.Handle;
                }
                else
                {
                    var value = this.Require<DxfRawSortentsTable>(link.Handle);
                    if (value.OwnerHandle != ext) throw new InvalidDataException("SORTENTSTABLE owner mismatch.");
                    this.Replace(value, DxfObjectSchema.Wrap(value, body)); result = value.Handle;
                }
                if (enableRegeneration) this.enableSortents = true;
                return result;
            });
        }

        private List<DxfRawStoredObject> CurrentObjects()
        {
            var all = new List<DxfRawStoredObject>();
            foreach (DxfRawStoredObject value in this.store.Objects)
            {
                this.cancellation.ThrowIfCancellationRequested();
                if (value.Handle != null && this.changes.TryGetValue(DxfObjectText.Number(value.Handle), out Change changed))
                { if (!changed.Deleted) all.Add(changed.Object); }
                else all.Add(value);
            }
            foreach (ulong key in this.added)
                if (this.changes.TryGetValue(key, out Change addedObject) && !addedObject.Deleted) all.Add(addedObject.Object);
            return all;
        }
        private List<DxfRawStoredObject> OwnedTree(string handle)
        {
            var all = this.CurrentObjects(); var children = new Dictionary<string, List<DxfRawStoredObject>>(StringComparer.Ordinal);
            foreach (DxfRawStoredObject value in all)
                if (value.OwnerHandle != null && value.Handle != null)
                {
                    if (!children.TryGetValue(value.OwnerHandle, out List<DxfRawStoredObject> list)) children.Add(value.OwnerHandle, list = new List<DxfRawStoredObject>());
                    list.Add(value);
                }
            var queue = new Queue<DxfRawStoredObject>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            queue.Enqueue(this.Get(handle) ?? throw new ArgumentException("Unknown stored object.")); var result = new List<DxfRawStoredObject>();
            while (queue.Count != 0)
            {
                this.cancellation.ThrowIfCancellationRequested(); var current = queue.Dequeue();
                if (!seen.Add(current.Handle)) throw new InvalidDataException("Ownership cycle or duplicate object in subtree.");
                if (current is DxfRawOpaqueStoredObject opaque) throw new NotSupportedException("Owned subtree contains an unsupported schema: " + opaque.Reason);
                result.Add(current);
                if (result.Count > this.store.Options.MaximumChanges) throw new InvalidDataException("Owned subtree exceeds the change budget.");
                if (children.TryGetValue(current.Handle, out List<DxfRawStoredObject> nextChildren)) foreach (var child in nextChildren) queue.Enqueue(child);
            }
            return result;
        }

        /// <summary>Clones a schema-known owned dictionary tree into this same drawing with fresh identities and remapped exposed links.</summary>
        /// <remarks>
        /// Common reactors are not copied. Unknown owned classes and private common controls reject.
        /// Arbitrary handles, sort keys and references hidden in application strings/binary data are not translated.
        /// External links remain external; this is not cross-document import or a private-schema evaluator.
        /// </remarks>
        public string CloneDictionaryTree(string sourceHandle, string parentHandle, string name)
        {
            return this.Apply(() =>
            {
                var source = this.Require<DxfRawDictionary>(sourceHandle); var parent = this.Require<DxfRawDictionary>(parentHandle); this.CheckNewKey(parent, name);
                var tree = this.OwnedTree(source.Handle); var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var value in tree)
                {
                    int end = CommonEnd(value.Tags);
                    foreach (DxfTag tag in value.Tags.Take(end))
                        if (tag.Code == 102 && ((string)tag.RawValue).StartsWith("{", StringComparison.Ordinal) &&
                            !Equals(tag.RawValue, "{ACAD_REACTORS") && !Equals(tag.RawValue, "{ACAD_XDICTIONARY"))
                            throw new NotSupportedException("Private common controls require an application-specific cloning policy.");
                    mapping.Add(value.Handle, this.Allocate());
                }
                foreach (var value in tree)
                {
                    int commonEnd = CommonEnd(value.Tags); var tags = new List<DxfTag>(); int skipDepth = 0;
                    for (int i = 0; i < value.Tags.Count; ++i)
                    {
                        DxfTag tag = value.Tags[i];
                        if (i < commonEnd && tag.Code == 102)
                        {
                            string text = (string)tag.RawValue;
                            if (text == "{ACAD_REACTORS" || skipDepth != 0)
                            {
                                if (text.StartsWith("{", StringComparison.Ordinal)) ++skipDepth;
                                else if (text == "}") --skipDepth;
                                continue;
                            }
                        }
                        if (skipDepth != 0) continue;
                        if (i < commonEnd && tag.Code == 5) tags.Add(new DxfTag(5, mapping[value.Handle]));
                        else if (i < commonEnd && tag.Code == 330 && value.Handle == source.Handle)
                            tags.Add(new DxfTag(330, parent.Handle));
                        else if ((tag.Code >= 330 && tag.Code <= 369 || tag.Code == 1005) &&
                            mapping.TryGetValue(DxfObjectText.Handle((string)tag.RawValue, true), out string replacement))
                            tags.Add(new DxfTag(tag.Code, replacement));
                        else tags.Add(tag);
                    }
                    if (value.OwnerHandle == null)
                        tags.Insert(2, new DxfTag(330, value.Handle == source.Handle ? parent.Handle : mapping[value.OwnerHandle]));
                    DxfRawStoredObject clone = DxfObjectSchema.Read(tags.AsReadOnly(), null, this.store.Options.MaximumPayloadTags);
                    if (clone is DxfRawOpaqueStoredObject opaque) throw new InvalidDataException(opaque.Reason);
                    if (this.store.Objects.Count + this.added.Count >= this.store.Options.MaximumObjects) throw new InvalidDataException("OBJECTS budget exceeded.");
                    ulong key = DxfObjectText.Number(clone.Handle);
                    this.Stage(key, new Change { Tags = tags, Object = clone }); this.added.Add(key);
                }
                var copiedRoot = this.Get(mapping[source.Handle]);
                // The parent might lie in the source tree, but source snapshots were read before editing.
                this.LinkNew(this.Require<DxfRawDictionary>(parent.Handle), name, copiedRoot); return copiedRoot.Handle;
            });
        }

        /// <summary>Deletes an unreferenced, schema-known owned subtree. Incoming external or opaque exposed references reject the operation.</summary>
        /// <remarks>Unlink its parent entry or extension dictionary first, or use the combined removal methods.</remarks>
        public void DeleteOwnedTree(string handle) { this.Apply(() => { this.DeleteTree(handle); return true; }); }
        private void DeleteTree(string handle)
        {
            string canonical = DxfObjectText.Handle(handle, false);
            if (canonical == this.root) throw new InvalidOperationException("The root dictionary cannot be deleted.");
            List<DxfRawStoredObject> tree = this.OwnedTree(canonical);
            var deleted = new HashSet<ulong>(tree.Select(o => DxfObjectText.Number(o.Handle)));
            DxfRawDocument current = this.Build(false);
            DxfRawHandleIndex index = DxfRawHandleIndex.Create(current, cancellationToken: this.cancellation);
            var sourceIds = index.Occurrences.Where(o => o.Role == DxfRawHandleRole.Identity && o.Record != null)
                .ToDictionary(o => o.Record, o => o.NumericHandle);
            foreach (DxfRawHandleOccurrence item in index.Occurrences)
            {
                this.cancellation.ThrowIfCancellationRequested();
                if (!deleted.Contains(item.NumericHandle) || item.Role == DxfRawHandleRole.Identity ||
                    item.Role == DxfRawHandleRole.HeaderSeed || item.Role == DxfRawHandleRole.Arbitrary) continue;
                if (item.Record != null && sourceIds.TryGetValue(item.Record, out ulong source) && deleted.Contains(source)) continue;
                throw new InvalidOperationException("Deleting the subtree would invalidate an external or opaque exposed reference to " + item.Handle + ".");
            }
            foreach (var value in tree)
                this.Stage(DxfObjectText.Number(value.Handle), new Change { Source = value.SourceRecord, Deleted = true, Object = value });
        }
    }
}
