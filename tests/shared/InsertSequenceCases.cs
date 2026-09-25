// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Qualification
{
    internal static partial class InsertGeometryCases
    {
        private static IEnumerable<Case> SequenceCases(string? directory)
        {
            foreach (var version in Versions) foreach (bool binary in new[] { false, true })
            {
                yield return new Case($"sequence/wire/{version}/{binary}", () => SequenceWire(version, binary, directory));
                for (int fault = 0; fault < 8; fault++)
                {
                    int scenario = fault;
                    yield return new Case($"sequence/malformed/{version}/{binary}/{fault}", () => SequenceMalformed(version, binary, scenario));
                }
                yield return new Case($"sequence/legacy-owner/{version}/{binary}", () => SequenceLegacyOwner(version, binary));
                yield return new Case($"sequence/lifecycle/{version}/{binary}", () => SequenceLifecycle(version, binary));
            }
        }
        private static Insert SequenceSubject(string name, bool multiple)
        {
            var block = new Block(name);
            block.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
            block.AttributeDefinitions.Add(new AttributeDefinition("A") { Value = "FIRST", Height = 1 });
            block.AttributeDefinitions.Add(new AttributeDefinition("B") { Value = "SECOND", Position = Vector3.UnitY, Height = 1 });
            return new Insert(block) { RowCount = (short)(multiple ? 2 : 1), ColumnCount = (short)(multiple ? 3 : 1), RowSpacing = 3, ColumnSpacing = 2 };
        }
        private static DxfRawDocument SequenceRaw(byte[] bytes)
        { using (var input = new MemoryStream(bytes)) return DxfRawDocument.Load(input); }
        private static string SequenceField(DxfRawRecord row, short code)
        { return (string)row.Tags.Single(tag => tag.Code == code).Value; }
        private static List<DxfTag> SequenceTags(byte[] bytes) { return SequenceRaw(bytes).Tags.ToList(); }
        private static int SequenceStart(List<DxfTag> tags)
        { return tags.FindIndex(tag => tag.Code == 0 && (string)tag.Value == "SEQEND"); }
        private static byte[] SequenceBytes(List<DxfTag> tags, bool binary)
        { using (var output = new MemoryStream()) { DxfRawDocument.Create(tags).Save(output, binary); return output.ToArray(); } }
        private static Dictionary<string, string> SequencePacketOwners(byte[] bytes)
        {
            var result = new Dictionary<string, string>();
            foreach (var row in SequenceRaw(bytes).Sections.Where(section => section.Name == "ENTITIES" || section.Name == "BLOCKS")
                .SelectMany(section => section.Records).Where(row => row.Name == "SEQEND"))
            {
                // Group 330 inside ACAD_REACTORS is not the common owner.
                int depth = 0; var owners = new List<string>();
                foreach (var tag in row.Tags)
                {
                    if (tag.Code == 100) break;
                    if (tag.Code == 102) { if ((string)tag.Value == "}") depth--; else if (((string)tag.Value).StartsWith("{", StringComparison.Ordinal)) depth++; }
                    else if (depth == 0 && tag.Code == 330) owners.Add((string)tag.Value);
                }
                Check(owners.Count == 1, "Exactly one common SEQEND owner is required");
                result.Add(SequenceField(row, 5), owners.Single());
            }
            return result;
        }
        private static void SequenceWire(DxfVersion version, bool binary, string? directory)
        {
            var doc = new DxfDocument(version); var inserts = new List<Insert>();
            for (int place = 0; place < 4; place++)
            {
                var items = new List<Insert>();
                foreach (bool array in new[] { false, true }) items.Add(SequenceSubject($"SQ_{place}_{(array ? 1 : 0)}", array));
                if (place == 0) foreach (var item in items) doc.Entities.Add(item);
                else if (place == 1)
                {
                    var paper = new Layout("SQ_PAPER"); doc.Layouts.Add(paper);
                    foreach (var item in items) paper.AssociatedBlock.Entities.Add(item);
                }
                else
                {
                    var block = new Block("SQ_CONTAINER_" + place); foreach (var item in items) block.Entities.Add(item);
                    if (place == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block);
                }
                inserts.AddRange(items);
            }
            var following = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); doc.Entities.Add(following);
            var refs = new DxfXRecord(); doc.NamedObjects.Add("SQ_REFERENCES", refs);
            foreach (var insert in inserts)
            {
                DxfObject end = insert.EndSequenceRecord ?? throw new InvalidOperationException("Missing sequence terminator");
                Check(end != null && ReferenceEquals(doc.GetObjectByHandle(end.Handle), end) && ReferenceEquals(end.Owner, insert), "Registered SEQEND identity/owner");
                var data = new XData(new ApplicationRegistry("SQ_DATA")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, insert.Block.Name)); end!.XData.Add(data);
                end.PersistentReactors.Add(following);
                var extension = new DxfDictionary(); doc.Objects.SetExtensionDictionary(end, extension);
                var payload = new DxfXRecord(); payload.Data.Add(new DxfTag(1, "sequence-metadata")); extension.Add("SQ_PAYLOAD", payload);
                refs.Data.Add(new DxfTag(330, end.Handle));
            }
            var ids = inserts.ToDictionary(item => item.Handle, item => item.EndSequenceRecord.Handle);
            string prefix = $"insert-sequence-{version}-{(binary ? "binary" : "text")}";
            byte[] source = Save(doc, binary); Store(directory, prefix, "source", source);
            foreach (var insert in inserts) Check(ReferenceEquals(doc.GetObjectByHandle(ids[insert.Handle]), insert.EndSequenceRecord), "Save replaced terminator object");
            byte[] repeat = Save(doc, binary);
            Check(SequencePacketOwners(source).OrderBy(pair => pair.Key).SequenceEqual(SequencePacketOwners(repeat).OrderBy(pair => pair.Key)), "Repeated save changed sequence identities");
            var loaded = Load(source); VerifySequenceDocument(loaded, ids, following.Handle);
            byte[] output = Save(loaded, !binary); Store(directory, prefix, "output", output);
            var reloaded = Load(output); VerifySequenceDocument(reloaded, ids, following.Handle);
            byte[] resave = Save(reloaded, binary); Store(directory, prefix, "resave", resave);
            VerifySequenceDocument(Load(resave), ids, following.Handle);
            foreach (byte[] bytes in new[] { source, output, resave })
            {
                var owners = SequencePacketOwners(bytes); Check(owners.Count == ids.Count, "SEQEND physical inventory");
                foreach (var pair in ids) Check(owners[pair.Value] == pair.Key, "SEQEND physical owner or identity changed");
            }
        }
        private static void VerifySequenceDocument(DxfDocument doc, Dictionary<string, string> ids, string following)
        {
            foreach (var pair in ids)
            {
                var insert = (Insert)doc.GetObjectByHandle(pair.Key); var end = insert.EndSequenceRecord;
                Check(end.Handle == pair.Value && ReferenceEquals(doc.GetObjectByHandle(pair.Value), end) && ReferenceEquals(end.Owner, insert), "Loaded terminator graph");
                Check(insert.Attributes.Count == 2 && insert.Attributes[0].Value == "FIRST" && insert.Attributes[1].Value == "SECOND", "Attribute order/values");
                Check(end.XData["SQ_DATA"].XDataRecord.Single().Value.Equals(insert.Block.Name), "Terminator XData");
                Check(end.PersistentReactors.Count == 1 && ReferenceEquals(end.PersistentReactors[0], doc.GetObjectByHandle(following)), "Terminator reactor");
                Check(end.ExtensionDictionary != null && ReferenceEquals(end.ExtensionDictionary.Owner, end), "Terminator extension owner");
                bool rejected = false; try { insert.Clone(); } catch (NotSupportedException) { rejected = true; }
                Check(rejected, "Referenced terminator cloned without graph mapping");
            }
            var line = (Line)doc.GetObjectByHandle(following); Same(new Vector3(101, 102, 103), line.StartPoint, "Following geometry");
            Check(doc.Objects.Validate().Count == 0, "Terminator database validation");
        }
        private static void SequenceMalformed(DxfVersion version, bool binary, int fault)
        {
            var doc = new DxfDocument(version); var insert = SequenceSubject("SQ_BAD", false); doc.Entities.Add(insert);
            var following = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(following);
            var tags = SequenceTags(Save(doc, binary)); int first = SequenceStart(tags), end = first + 1;
            while (end < tags.Count && tags[end].Code != 0) end++;
            int identity = tags.FindIndex(first + 1, end - first - 1, tag => tag.Code == 5);
            int owner = tags.FindIndex(first + 1, end - first - 1, tag => tag.Code == 330);
            switch (fault)
            {
                case 0: tags.RemoveRange(first, end - first); break;
                case 1: tags[first] = new DxfTag(0, "LINE"); break;
                case 2: tags.Insert(identity, tags[identity]); break;
                case 3: tags[identity] = new DxfTag(5, following.Handle); break;
                case 4: tags[owner] = new DxfTag(330, insert.Owner.Record.Handle); break;
                case 5: tags[owner] = new DxfTag(330, "0"); break;
                case 6: tags.Insert(owner, tags[owner]); break;
                case 7: tags[owner] = new DxfTag(330, following.Handle); break;
            }
            byte[] bytes = SequenceBytes(tags, binary);
            bool rejected = false;
            using (var stream = new MemoryStream(bytes))
            {
                try { rejected = DxfDocument.Load(stream) == null; }
                catch (FormatException) { rejected = true; }
                catch (InvalidDataException) { rejected = true; }
                Check(stream.CanRead && bytes.SequenceEqual(stream.ToArray()), "Malformed source bytes/stream changed");
            }
            Check(rejected, "Malformed attribute sequence accepted: " + fault);
        }
        private static void SequenceLegacyOwner(DxfVersion version, bool binary)
        {
            var doc = new DxfDocument(version); var insert = SequenceSubject("SQ_LEGACY", false); doc.Entities.Add(insert);
            var tags = SequenceTags(Save(doc, binary)); int first = SequenceStart(tags);
            int index = tags.FindIndex(first + 1, tag => tag.Code == 330); tags.RemoveAt(index);
            var loaded = Load(SequenceBytes(tags, binary)); var restored = (Insert)loaded.GetObjectByHandle(insert.Handle);
            Check(restored.EndSequenceRecord.Handle == insert.EndSequenceRecord.Handle, "Legacy source identity changed");
            Check(SequencePacketOwners(Save(loaded, binary))[restored.EndSequenceRecord.Handle] == insert.Handle, "Missing legacy owner not materialized");
        }
        private static void SequenceLifecycle(DxfVersion version, bool binary)
        {
            var doc = new DxfDocument(version); var insert = new Insert(new Block("SQ_LIFECYCLE")); doc.Entities.Add(insert);
            Check(insert.EndSequenceRecord == null && SequencePacketOwners(Save(doc, binary)).Count == 0, "Attribute-free INSERT acquired a terminator");
            insert.Block.AttributeDefinitions.Add(new AttributeDefinition("A") { Value = "FIRST", Height = 1 }); insert.Sync();
            var end = insert.EndSequenceRecord; Check(end != null && ReferenceEquals(doc.GetObjectByHandle(end.Handle), end), "New attribute sequence not registered");
            string old = end!.Handle;
            insert.Block.AttributeDefinitions.Remove("A"); insert.Sync();
            Check(insert.Attributes.Count == 0 && ReferenceEquals(insert.EndSequenceRecord, end), "Empty sequence discarded");
            var loaded = Load(Save(doc, binary)); var empty = (Insert)loaded.GetObjectByHandle(insert.Handle);
            Check(empty.Attributes.Count == 0 && empty.EndSequenceRecord.Handle == old, "Empty sequence did not round trip");
            var clone = (Insert)empty.Clone();
            var cloneEnd = clone.EndSequenceRecord ?? throw new InvalidDataException("Clone lost sequence terminator");
            Check(cloneEnd.Handle == null, "Clone retained source identity");
            loaded.Entities.Add(clone);
            Check(ReferenceEquals(clone.EndSequenceRecord, cloneEnd) && cloneEnd.Handle != old
                && ReferenceEquals(cloneEnd.Owner, clone), "Clone terminator alias");
            var external = new DxfXRecord(); loaded.NamedObjects.Add("SQ_REFERENCE", external); external.Data.Add(new DxfTag(330, old));
            bool refused = false; try { refused = !loaded.Entities.Remove(empty); } catch (InvalidOperationException) { refused = true; }
            Check(refused && ReferenceEquals(loaded.GetObjectByHandle(old), empty.EndSequenceRecord), "Referenced terminator removed");
            external.Data.Clear(); Check(loaded.Entities.Remove(empty), "Unreferenced sequence removal failed");
            Check(loaded.GetObjectByHandle(old) == null && empty.EndSequenceRecord.Handle == null, "Detached terminator stayed registered");
            loaded.Entities.Add(empty); Check(empty.EndSequenceRecord.Handle != old, "Re-add reused source identity");
            Check(loaded.Objects.Validate().Count == 0, "Lifecycle graph invalid");
        }
    }
}
