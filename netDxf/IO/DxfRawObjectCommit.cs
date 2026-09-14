// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using netDxf.Header;

namespace netDxf.IO
{
    public sealed partial class DxfRawObjectTransaction
    {
        /// <summary>Validates and commits all staged edits to a new immutable document. A no-op returns the source instance.</summary>
        /// <remarks>
        /// Failure leaves both source and staging intact so a caller can correct a rejected reference.
        /// Successful commit closes this transaction. No filesystem writes are performed; SaveAtomic is a separate operation.
        /// Only edited schemas are validated; untouched private records remain opaque, not certified valid.
        /// </remarks>
        public DxfRawDocument Commit()
        {
            this.EnsureOpen();
            if (this.undo != null) throw new InvalidOperationException("An object transaction cannot be reentered.");
            if (this.changes.Count == 0 && !this.enableSortents) { this.closed = true; return this.store.Document; }
            DxfRawDocument result = this.Build(true);
            this.ValidateCommit(result);
            this.cancellation.ThrowIfCancellationRequested();
            this.closed = true;
            return result;
        }
        private DxfRawDocument Build(bool metadata)
        {
            DxfRawDocument source = this.store.Document;
            var byStart = new Dictionary<int, Change>();
            foreach (Change change in this.changes.Values) if (change.Source != null) byStart.Add(change.Source.StartTagIndex, change);
            DxfRawSection objects = UniqueSection(source, "OBJECTS");
            var newObjectTags = new List<DxfTag>();
            foreach (ulong key in this.added)
                if (!this.changes[key].Deleted) newObjectTags.AddRange(this.changes[key].Tags);
            var insertions = new Dictionary<int, List<DxfTag>>();
            Action<int, IEnumerable<DxfTag>> insert = (at, tags) =>
            {
                if (!insertions.TryGetValue(at, out List<DxfTag> list)) insertions.Add(at, list = new List<DxfTag>());
                list.AddRange(tags);
            };
            if (objects != null) insert(objects.EndTagIndex - 1, newObjectTags);
            else if (newObjectTags.Count != 0)
            {
                var complete = new List<DxfTag> { new DxfTag(0, "SECTION"), new DxfTag(2, "OBJECTS") };
                complete.AddRange(newObjectTags); complete.Add(new DxfTag(0, "ENDSEC"));
                DxfRawSection thumbnail = UniqueSection(source, "THUMBNAILIMAGE");
                int at = thumbnail == null ? FindEof(source) : thumbnail.StartTagIndex;
                insert(at, complete);
            }
            var tagReplacements = new Dictionary<int, DxfTag>();
            if (metadata)
            {
                this.UpdateHeader(insert, tagReplacements);
                this.UpdateClasses(insert, byStart);
            }
            var result = new List<DxfTag>();
            for (int i = 0; i < source.Tags.Count;)
            {
                if ((i & 1023) == 0) this.cancellation.ThrowIfCancellationRequested();
                if (insertions.TryGetValue(i, out List<DxfTag> insertion)) result.AddRange(insertion);
                if (byStart.TryGetValue(i, out Change replacement))
                {
                    if (!replacement.Deleted) result.AddRange(replacement.Tags);
                    i = replacement.Source.EndTagIndex;
                }
                else
                {
                    result.Add(tagReplacements.TryGetValue(i, out DxfTag replacementTag) ? replacementTag : source.Tags[i]); ++i;
                }
            }
            if (insertions.TryGetValue(source.Tags.Count, out List<DxfTag> trailing)) result.AddRange(trailing);
            this.cancellation.ThrowIfCancellationRequested();
            if (SameTags(source.Tags, result)) return source;
            return source.WithTags(result);
        }
        private static DxfRawSection UniqueSection(DxfRawDocument document, string name)
        {
            DxfRawSection result = null;
            foreach (DxfRawSection section in document.Sections)
                if (StringComparer.OrdinalIgnoreCase.Equals(section.Name, name))
                {
                    if (result != null) throw new InvalidDataException("Multiple " + name + " sections are ambiguous.");
                    result = section;
                }
            return result;
        }
        private static int FindEof(DxfRawDocument source)
        {
            for (int i = source.Tags.Count - 1; i >= 0; --i)
                if (source.Tags[i].Code == 0 && Equals(source.Tags[i].RawValue, "EOF")) return i;
            throw new InvalidDataException("Missing EOF marker.");
        }
        private void UpdateHeader(Action<int, IEnumerable<DxfTag>> insert, Dictionary<int, DxfTag> replacements)
        {
            DxfRawSection header = UniqueSection(this.store.Document, "HEADER");
            if (header == null) throw new NotSupportedException("Object editing requires an explicit HEADER profile.");
            var seeds = header.Records.Where(r => r.Name == "$HANDSEED").ToArray();
            if (seeds.Length > 1) throw new InvalidDataException("Multiple HANDSEED records.");
            if (this.added.Count != 0)
            {
                if (seeds.Length == 0) insert(header.EndTagIndex - 1, new[] { new DxfTag(9, "$HANDSEED"), new DxfTag(5, this.next.ToString("X", CultureInfo.InvariantCulture)) });
                else
                {
                    var seedTags = seeds[0].Tags.Select((t, i) => new { Tag = t, Index = i }).Where(p => p.Tag.Code != 999 && p.Tag.Code != 9).ToArray();
                    if (seedTags.Length != 1 || seedTags[0].Tag.Code != 5) throw new InvalidDataException("Malformed HANDSEED record.");
                    ulong current = DxfObjectText.Number((string)seedTags[0].Tag.RawValue);
                    if (current < this.next) replacements.Add(seeds[0].StartTagIndex + seedTags[0].Index, new DxfTag(5, this.next.ToString("X", CultureInfo.InvariantCulture)));
                }
            }
            if (!this.enableSortents) return;
            var sorting = header.Records.Where(r => r.Name == "$SORTENTS").ToArray();
            if (sorting.Length > 1) throw new InvalidDataException("Multiple SORTENTS variables.");
            if (sorting.Length == 0) insert(header.EndTagIndex - 1, new[] { new DxfTag(9, "$SORTENTS"), new DxfTag(280, (short)16) });
            else
            {
                var flags = sorting[0].Tags.Select((t, i) => new { Tag = t, Index = i }).Where(p => p.Tag.Code != 999 && p.Tag.Code != 9).ToArray();
                if (flags.Length != 1 || flags[0].Tag.Code != 280) throw new InvalidDataException("Malformed SORTENTS variable.");
                short value = (short)flags[0].Tag.RawValue;
                if ((value & 16) == 0) replacements.Add(sorting[0].StartTagIndex + flags[0].Index, new DxfTag(280, (short)(value | 16)));
            }
        }
        private void UpdateClasses(Action<int, IEnumerable<DxfTag>> insert, Dictionary<int, Change> byStart)
        {
            var before = this.store.Objects.GroupBy(o => o.TypeName).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var after = this.CurrentObjects().GroupBy(o => o.TypeName).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var touched = DxfObjectSchema.RegisteredClasses.Keys.Where(k =>
            { before.TryGetValue(k, out int oldCount); after.TryGetValue(k, out int newCount); return oldCount != newCount; }).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (touched.Length == 0) return;
            DxfRawSection classes = UniqueSection(this.store.Document, "CLASSES");
            var missing = new List<DxfTag>();
            foreach (string type in touched)
            {
                this.cancellation.ThrowIfCancellationRequested();
                var found = classes == null ? Array.Empty<DxfRawRecord>() : classes.Records.Where(r => r.Name == "CLASS" && r.Tags.Any(t => t.Code == 1 && Equals(t.RawValue, type))).ToArray();
                if (found.Length > 1) throw new InvalidDataException("Duplicate CLASS definition: " + type);
                after.TryGetValue(type, out int count);
                if (found.Length == 0)
                {
                    if (count == 0) continue;
                    missing.AddRange(new[] { new DxfTag(0, "CLASS"), new DxfTag(1, type),
                        new DxfTag(2, DxfObjectSchema.RegisteredClasses[type]), new DxfTag(3, "ObjectDBX Classes"), new DxfTag(90, 0) });
                    if (this.store.Document.Version >= DxfVersion.AutoCad2004) missing.Add(new DxfTag(91, count));
                    missing.Add(new DxfTag(280, (short)0)); missing.Add(new DxfTag(281, (short)0));
                }
                else
                {
                    DxfRawRecord record = found[0];
                    var names = record.Tags.Where(t => t.Code == 2).ToArray(); var entities = record.Tags.Where(t => t.Code == 281).ToArray();
                    if (names.Length != 1 || !Equals(names[0].RawValue, DxfObjectSchema.RegisteredClasses[type]) ||
                        entities.Length != 1 || (short)entities[0].RawValue != 0)
                        throw new InvalidDataException("Conflicting object CLASS definition: " + type);
                    var counts = record.Tags.Select((t, i) => new { Tag = t, Index = i }).Where(p => p.Tag.Code == 91).ToArray();
                    if (counts.Length > 1) throw new InvalidDataException("Duplicate CLASS instance count.");
                    if (this.store.Document.Version >= DxfVersion.AutoCad2004)
                    {
                        var tags = record.Tags.ToList();
                        if (counts.Length == 1) tags[counts[0].Index] = new DxfTag(91, count);
                        else tags.Insert(tags.FindIndex(t => t.Code == 280) is int position && position >= 0 ? position : tags.Count, new DxfTag(91, count));
                        byStart.Add(record.StartTagIndex, new Change { Source = record, Tags = tags });
                    }
                }
            }
            if (missing.Count == 0) return;
            if (classes != null) insert(classes.EndTagIndex - 1, missing);
            else
            {
                DxfRawSection header = UniqueSection(this.store.Document, "HEADER");
                if (header == null) throw new InvalidDataException("Missing HEADER for CLASSES insertion.");
                var tags = new List<DxfTag> { new DxfTag(0, "SECTION"), new DxfTag(2, "CLASSES") };
                tags.AddRange(missing); tags.Add(new DxfTag(0, "ENDSEC")); insert(header.EndTagIndex, tags);
            }
        }

        private void ValidateCommit(DxfRawDocument document)
        {
            DxfRawObjectStore result = DxfRawObjectStore.Open(document, this.store.Options, this.cancellation);
            foreach (var diagnostic in result.Index.Diagnostics)
                if (diagnostic.Kind == DxfRawHandleDiagnosticKind.DuplicateIdentity || diagnostic.Kind == DxfRawHandleDiagnosticKind.MultipleIdentities ||
                    diagnostic.Kind == DxfRawHandleDiagnosticKind.NullIdentity || diagnostic.Kind == DxfRawHandleDiagnosticKind.InvalidControlGroup ||
                    diagnostic.Kind == DxfRawHandleDiagnosticKind.OwnerCycle || diagnostic.Kind == DxfRawHandleDiagnosticKind.MultipleOwners)
                    throw new InvalidDataException("Committed common graph is invalid: " + diagnostic.Message);
            Action<string> pointer = handle =>
            {
                if (handle == null || handle == "0") return;
                if (result.Index.FindDefinitions(handle).Count != 1) throw new InvalidDataException("Unresolved or ambiguous edited reference: " + handle);
            };
            foreach (var pair in this.changes)
            {
                this.cancellation.ThrowIfCancellationRequested();
                Change change = pair.Value;
                if (change.Deleted || change.Object == null) continue;
                DxfRawStoredObject value = result.Get(change.Object.Handle);
                if (value == null || value is DxfRawOpaqueStoredObject) throw new InvalidDataException("Edited schema could not be read back.");
                pointer(value.OwnerHandle);
                // Schema payload checks below do not cover common controls or appended XData.
                // Validate every interpreted reference on this edited record as well, while
                // leaving opaque application fields and unrelated records untouched.
                foreach (DxfRawHandleOccurrence occurrence in result.Index.GetOccurrences(value.SourceRecord))
                    if (occurrence.IsReference) pointer(occurrence.CanonicalHandle);
                if (value is DxfRawDictionary dictionary)
                {
                    foreach (DxfRawDictionaryEntry entry in dictionary.Entries)
                    {
                        DxfRawStoredObject target = result.Get(entry.Handle) ?? throw new InvalidDataException("Dictionary target is not an OBJECTS record: " + entry.Handle);
                        if (target.OwnerHandle != dictionary.Handle || target.Handle == dictionary.Handle)
                            throw new InvalidDataException("Dictionary entry must target another object with that dictionary as its common owner.");
                    }
                    if (dictionary.HasDefault)
                    {
                        if (dictionary.DefaultHandle == null || dictionary.DefaultHandle == "0" || result.Get(dictionary.DefaultHandle) == null)
                            throw new InvalidDataException("An edited dictionary-with-default requires an existing nonzero default object.");
                    }
                }
                else if (value is DxfRawXRecord record)
                {
                    foreach (DxfTag tag in record.Data)
                        if (tag.Code >= 330 && tag.Code <= 369) pointer(DxfObjectText.Handle((string)tag.RawValue, true));
                }
                else if (value is DxfRawIdBuffer buffer) foreach (string handle in buffer.Handles) pointer(handle);
                else if (value is DxfRawSortentsTable sort)
                {
                    pointer(sort.BlockRecordHandle);
                    if (result.Index.FindDefinitions(sort.BlockRecordHandle)[0].Record.Name != "BLOCK_RECORD")
                        throw new InvalidDataException("Draw-order block reference does not identify a BLOCK_RECORD.");
                    foreach (DxfRawSortOrderEntry entry in sort.Entries) pointer(entry.EntityHandle);
                }
            }
        }
    }
}
