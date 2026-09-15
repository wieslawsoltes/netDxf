using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSeventhMixedModuleTests()
    {
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2004, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            Run($"seventh-mixed/lifecycle/{version}/{binary}", () => SeventhLifecycle(version, binary));
            Run($"seventh-mixed/source-identity/{version}/{binary}", () => SeventhSourceIdentity(version, binary));
        }
    }

    private static DxfDocument SeventhSeed(DxfVersion version, bool binary)
    {
        var raw = DxfRawDocument.Load(new MemoryStream(TableContentSourceBytes(version == DxfVersion.AutoCad2018 ?
            "acad_table_with_blk_ref.dxf" : "sample_AC1018_ascii.dxf")));
        return SeventhAttachGraph(SeventhLoad(raw, binary), binary);
    }

    private static void SeventhSave(DxfDocument doc, bool binary, string phase)
        => File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"seventh-mixed-{phase}-{doc.DrawingVariables.AcadVer}-{binary}.dxf"), SeventhBytes(doc, binary));

    private static void SeventhAtomic(DxfDocument doc, Action action, bool released = false, bool mustThrow = true)
    {
        // Saving can update HEADER and regenerate unregistered POLYLINE member
        // handles and the empty legacy ACAD_LAYERSTATES dictionary. Normalize
        // only these proven writer-generated identities, preserving their full
        // scalar packets and reference topology. Registered identities and the
        // operation's allocation seed remain exact.
        (short Code, string Value)[] Snapshot()
        {
            var records = DxfRawDocument.Load(new MemoryStream(SeventhBytes(doc, false))).Sections
                .Where(section => section.Name != "HEADER").SelectMany(section => section.Records).ToArray();
            var transient = new Dictionary<string, string>();
            foreach (var record in records)
            {
                var identity = record.Tags.FirstOrDefault(tag => tag.Code == 5);
                if (identity == null || doc.GetObjectByHandle((string)identity.Value) != null) continue;
                string handle = (string)identity.Value;
                bool layerStates = record.Name == "DICTIONARY" && !record.Tags.Any(tag => tag.Code == 3) &&
                    records.Any(parent => parent.Tags.Zip(parent.Tags.Skip(1)).Any(pair => pair.First.Code == 3 &&
                        Equals(pair.First.Value, "ACAD_LAYERSTATES") && pair.Second.Code == 360 && Equals(pair.Second.Value, handle)));
                if (record.Name is "VERTEX" or "SEQEND" || layerStates)
                    transient.Add(handle, (0xFFFF000000000000UL + (ulong)transient.Count).ToString("X"));
            }
            return records.SelectMany(record => OwnershipTagValues(record.Tags.Select(tag =>
                tag.ValueType == DxfTagValueType.Handle && tag.HandleKind != DxfHandleKind.Arbitrary && transient.TryGetValue((string)tag.Value, out string? mapped)
                    ? new DxfTag(tag.Code, mapped) : tag))).ToArray();
        }
        var before = Snapshot(); long seed = OwnershipSeed(doc);
        var registered = doc.Objects.Items.ToDictionary(item => item.Handle, item => item);
        var entities = doc.Entities.All.ToArray(); var p = SeventhParts(doc);
        var refs = p.Base.GetReferences().Select(item => item.Reference).ToArray();
        int callbacks = 0;
        void Removed(netDxf.Collections.EntityCollection sender, netDxf.Collections.EntityCollectionEventArgs e) { callbacks++; }
        foreach (var block in doc.Blocks.Items) block.Entities.RemoveItem += Removed;
        // Membership and serialized state include nested owners and aliases;
        // original native resources must survive every failed preflight.
        bool rejected = false;
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException) { rejected = true; }
        finally { foreach (var block in doc.Blocks.Items) block.Entities.RemoveItem -= Removed; }
        if (mustThrow) Check(rejected, "Seventh mixed operation should reject before mutation");
        Equal(seed, OwnershipSeed(doc), "Rejected mixed operation allocated handles");
        Check(registered.Count == doc.Objects.Items.Count && registered.All(pair => ReferenceEquals(doc.GetObjectByHandle(pair.Key), pair.Value)), "Rejected operation changed object identities");
        Check(entities.SequenceEqual(doc.Entities.All), "Rejected operation changed entity membership");
        Check(refs.SequenceEqual(p.Base.GetReferences().Select(item => item.Reference)), "Rejected operation changed shared UCS references");
        var after = Snapshot();
        int difference = Enumerable.Range(0, Math.Min(before.Length, after.Length)).FirstOrDefault(index => before[index] != after[index], -1);
        Check(before.SequenceEqual(after), "Rejected operation changed serialized graph at " + difference +
            (difference >= 0 ? $": {before[difference]} -> {after[difference]}" : $": count {before.Length} -> {after.Length}"));
        Equal(0, callbacks, "Rejected operation invoked removal callbacks");
        SeventhAssert(doc, released);
    }

    private static void SeventhLifecycle(DxfVersion version, bool binary)
    {
        var doc = SeventhSeed(version, binary); var p = SeventhParts(doc); SeventhAssert(doc);
        SeventhSave(doc, binary, "original");
        SeventhAtomic(doc, () => Check(!doc.Entities.Remove(p.Geometry), "Shared native geometry must remain protected"), mustThrow: false);
        SeventhAtomic(doc, () => Check(!doc.Entities.Remove(p.Association.Dimension), "Native association owner must remain protected"), mustThrow: false);
        SeventhAtomic(doc, () => Check(!doc.UCSs.Remove(p.Base), "FIELD and UCS protect shared base"), mustThrow: false);
        SeventhAtomic(doc, () => doc.Objects.Clone(p.Folder, doc.NamedObjects, "SEVENTH_REFUSED_COPY"));
        SeventhAtomic(doc, () => doc.Objects.EraseOwnedTree(p.Folder));
        SeventhAtomic(doc, () => doc.Objects.EraseOwnedTree(p.Content));
        SeventhAtomic(doc, () => doc.Objects.EraseOwnedTree(p.Association));
        var foreign = new DxfDocument(version); var foreignBase = foreign.UCSs.Add(new UCS(p.Base.Name));
        var foreignChild = (UCS)p.Child.Clone(); foreignChild.Name = "SEVENTH_FOREIGN_CHILD";
        int foreignObjects = foreign.Objects.Items.Count, foreignUcs = foreign.UCSs.Count; long foreignSeed = OwnershipSeed(foreign);
        bool refusedForeign = false;
        try { foreign.UCSs.Add(foreignChild); } catch (ArgumentException) { refusedForeign = true; }
        Check(refusedForeign && foreignChild.Owner == null && foreignChild.Handle == null, "UCS foreign adoption requires the actual destination base identity");
        Equal(foreignSeed, OwnershipSeed(foreign), "Foreign UCS preflight allocated handles"); Equal(foreignUcs, foreign.UCSs.Count, "Foreign UCS preflight changed membership");
        SeventhAtomic(doc, () => foreign.Objects.Clone(p.Folder, foreign.NamedObjects, "FOREIGN_MIXED_COPY"));
        Equal(foreignSeed, OwnershipSeed(foreign), "Refused mixed foreign graph copy allocated handles");
        Equal(foreignObjects, foreign.Objects.Items.Count, "Refused mixed foreign graph copy changed destination objects");
        Check(!foreign.NamedObjects.Contains("FOREIGN_MIXED_COPY"), "Refused mixed foreign graph copy left an entry");
        foreignChild.SetOrthographicBase(5, foreignBase); foreign.UCSs.Add(foreignChild);
        Check(ReferenceEquals(foreignChild.BaseUcs, foreignBase) && ReferenceEquals(p.Child.BaseUcs, p.Base), "Explicit UCS destination mapping changed the original shared target");
        if (p.Section != null)
        {
            SeventhAtomic(doc, () => doc.Objects.EraseSection(p.Section));
            SeventhAtomic(doc, () => doc.Objects.EraseOwnedTree(p.Sun!));
        }

        // Removing one editable consumer must not release a target retained
        // by the stored FIELD. Its separate group71 origin remains intact.
        p.Child.SetOrthographicBase(0); p.Base.Name = "SEVENTH_BASE_RENAMED";
        SeventhAtomic(doc, () => Check(!doc.UCSs.Remove(p.Base), "FIELD still protects released UCS base"), released: true, mustThrow: false);
        var immutable = OwnershipTagValues(p.Field.Payload).ToArray();
        SeventhAssert(doc, true); SeventhSave(doc, binary, "released");
        Check(immutable.SequenceEqual(OwnershipTagValues(p.Field.Payload)), "Releasing UCS cannot rewrite immutable FIELD packet");

        var childCopy = (UCS)p.Child.Clone(); childCopy.Name = "SEVENTH_UCS_COPY"; doc.UCSs.Add(childCopy);
        var copied = new List<DxfObject> { childCopy };
        Section? sectionCopy = null; DxfSun? sunCopy = null;
        if (p.Section != null)
        {
            sectionCopy = doc.Objects.CloneSection(p.Section, p.Section.Owner);
            var settingsCopy = (DxfSectionSettings)sectionCopy.GeometrySettings!;
            var sources = settingsCopy.TypeSettings.Single().SourceObjects;
            Check(ReferenceEquals(sources[0], sectionCopy) && ReferenceEquals(sources[1], settingsCopy), "SECTION owned identities remap");
            Check(ReferenceEquals(sources[2], p.Association.Dimension) && ReferenceEquals(sources[3], p.Geometry) && ReferenceEquals(sources[4], p.Content) && ReferenceEquals(sources[5], p.Base), "SECTION native external identities remain shared");
            sunCopy = doc.Objects.CloneSun(p.Sun!, doc.Views.Add(new View("SEVENTH_SUN_COPY")));
            var note = (DxfXRecord)sunCopy.ExtensionDictionary!["LINKS"];
            Check(ReferenceEquals(note, sunCopy.ExtensionDictionary["ALIAS"]), "SUN cloned aliases share exact record");
            Equal(sunCopy.Owner.Handle, (string)note.Data.Single(tag => tag.Code == 330).Value, "SUN clone host remap");
            Equal(sunCopy.Handle, (string)note.Data.Single(tag => tag.Code == 331).Value, "SUN clone own identity remap");
            Check(note.Data.Where(tag => tag.Code == 340).Select(tag => (string)tag.Value).SequenceEqual(new[] { p.Base.Handle, p.Content.Handle, p.Association.Handle }), "SUN external native identity map");
            copied.AddRange(new DxfObject[] { sectionCopy, settingsCopy, sunCopy, sunCopy.ExtensionDictionary, note });
        }
        string[] deletedHandles = copied.Select(item => item.Handle).ToArray();
        SeventhSave(doc, binary, "cloned");
        if (sectionCopy != null) doc.Objects.EraseSection(sectionCopy);
        if (sunCopy != null) doc.Objects.EraseOwnedTree(sunCopy);
        Check(doc.UCSs.Remove(childCopy), "Unreferenced UCS copy should be removable");
        Check(deletedHandles.All(handle => doc.GetObjectByHandle(handle) == null), "Clone teardown leaves no owned identities registered");
        SeventhAtomic(doc, () => Check(!doc.Entities.Remove(p.Geometry), "Original native consumers survive clone teardown"), released: true, mustThrow: false);
        SeventhSave(doc, binary, "erased");
        SeventhAssert(DxfDocument.Load(new MemoryStream(SeventhBytes(doc, !binary)))!, true);

        // A source-bound family must veto conversion before writing even a
        // prefix into an existing caller-owned stream or reserving handles.
        long seed = OwnershipSeed(doc); doc.DrawingVariables.AcadVer = version == DxfVersion.AutoCad2018 ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2007;
        using var output = new MemoryStream(); byte[] sentinel = { 7, 0, 255, 19 }; output.Write(sentinel); output.Position = 2;
        bool refused = false; try { refused = !doc.Save(output, binary); }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException) { refused = true; }
        Check(refused && output.Position == 2 && output.ToArray().SequenceEqual(sentinel), "Mixed profile preflight changed caller stream");
        Equal(seed, OwnershipSeed(doc), "Mixed profile preflight allocated handles"); doc.DrawingVariables.AcadVer = version;
    }

    private static void SeventhSourceIdentity(DxfVersion version, bool binary)
    {
        var doc = SeventhSeed(version, binary); var p = SeventhParts(doc);
        var raw = DxfRawDocument.Load(new MemoryStream(SeventhBytes(doc, binary)));
        // Independent source spellings must meet at the same physical UCS and
        // geometry records despite their consumers using different schemas.
        foreach (string handle in new[] { p.Field.Handle, p.Child.Handle, p.Association.Handle })
        {
            var record = SeventhRecord(raw, handle);
            raw = raw.WithRecord(record, record.Tags.Select(tag => tag.Code is 331 or 346 && (string)tag.Value != "0" ?
                new DxfTag(tag.Code, ((string)tag.Value).ToLowerInvariant().PadLeft(12, '0')) : tag));
        }
        var loaded = SeventhLoad(raw, binary); SeventhAssert(loaded); SeventhSave(loaded, binary, "context");
        // A discarded physical UCS source record cannot be replaced by a
        // newly generated resource with the same name or by constructor state.
        var target = SeventhRecord(raw, p.Base.Handle);
        raw = raw.WithRecord(target, target.Tags.Select(tag => tag.Code == 2 ? new DxfTag(2, "BAD/UCS") : tag));
        bool rejected = false; try { SeventhLoad(raw, binary); } catch (Exception error) when (error is FormatException or InvalidDataException or ArgumentException) { rejected = true; }
        Check(rejected, "Shared FIELD/UCS target must resolve the exact physical source record");
    }
}
