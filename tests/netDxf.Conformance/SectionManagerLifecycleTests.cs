using System.Collections;
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSectionManagerLifecycleTests()
    {
        foreach (var version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            Run($"manager-lifecycle/created/{v}/{b}", () => ManagerLifecycleCreated(v, b));
            Run($"manager-lifecycle/empty/{v}/{b}", () => ManagerLifecycleEmpty(v, b));
        }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            foreach (string fault in new[] { "null-list", "null-entry", "detached", "foreign", "erased", "excessive", "move-throws", "get-throws", "dispose-throws", "reentry", "caught-reentry", "anchor", "lower-anchor", "existing", "orphan", "opaque", "class", "profile", "seed", "get-anchor", "dispose-anchor", "dispose-class", "dispose-profile", "dispose-target", "dispose-seed" })
            { string f = fault; Run($"manager-lifecycle/create-reject/{b}/{f}", () => ManagerLifecycleCreateRejected(b, f)); }
            foreach (string field in new[] { "xrecord330", "xrecord340", "xdata", "reactor", "default", "settings", "header", "attribute-xdata", "owned-incoming" })
            { string f = field; Run($"manager-lifecycle/incoming/{b}/{f}", () => ManagerLifecycleIncoming(b, f)); }
            foreach (string fault in new[] { "foreign", "erased", "null", "profile", "root", "reactors", "opaque-owned" })
            { string f = fault; Run($"manager-lifecycle/erase-reject/{b}/{f}", () => ManagerLifecycleEraseRejected(b, f)); }
            foreach (string declaration in new[] { "absent", "count-absent", "stale", "edited" })
            { string d = declaration; Run($"manager-lifecycle/class/{b}/{d}", () => ManagerLifecycleClass(b, d)); }
            foreach (string fault in new[] { "section-reactor", "root-reactor", "section-xdata", "root-xdata", "unrelated-line" })
            { string f = fault; Run($"manager-lifecycle/required-metadata/{b}/{f}", () => ManagerLifecycleRequiredMetadata(b, f)); }
            Run($"manager-lifecycle/maximum/{b}", () => ManagerLifecycleMaximum(b));
            Run($"manager-lifecycle/retained-allocation/{b}", () => ManagerLifecycleRetainedAllocation(b));
            Run($"manager-lifecycle/owned-aliases/{b}", () => ManagerLifecycleOwnedAliases(b));
            Run($"manager-lifecycle/profile-save/{b}", () => ManagerLifecycleProfileSave(b));
        }
        foreach (bool input in new[] { false, true }) foreach (bool output in new[] { false, true })
        { bool i = input, o = output; Run($"manager-lifecycle/native/{i}/{o}", () => ManagerLifecycleNative(i, o)); }
    }

    private static (DxfDocument Doc, Section First, Section Second) ManagerLifecycleDocument(DxfVersion version = DxfVersion.AutoCad2018)
    {
        var doc = new DxfDocument(version); var first = SectionExample("SECTIONOBJECT"); first.Name = "Lifecycle first";
        var second = SectionExample("SECTIONOBJECT"); second.Name = "Lifecycle second"; doc.Entities.Add(first); doc.Entities.Add(second);
        return (doc, first, second);
    }
    private static DxfClass ManagerLifecycleClassDefinition(int? count) => new("SECTION_MANAGER", "AcDbSectionManager", "ObjectDBX Classes") { ProxyFlags = 1024, InstanceCount = count };
    private static void ManagerLifecycleSetSeed(DxfDocument doc, long value) => typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(doc, value);
    private static void ManagerLifecycleReject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid manager lifecycle operation was accepted");
    }
    private static void ManagerLifecycleCreated(DxfVersion version, bool binary)
    {
        var (doc, first, second) = ManagerLifecycleDocument(version); var root = doc.Objects.Root;
        root.IsHardOwner = false; root.Cloning = DictionaryCloningFlags.UseClone;
        var marker = new DxfXRecord(); marker.Data.Add(new DxfTag(1, "unrelated root entry")); root.Add("LIFECYCLE_KEEP", marker, true);
        var requested = new List<Section> { first, second, first }; long seed = OwnershipSeed(doc);
        var manager = doc.Objects.CreateSectionManager(requested, false); requested.Clear();
        Check(manager.Sections.SequenceEqual(new[] { first, second, first }) && !manager.RequiresFullUpdate, "Creation lost ordered actual repeated identities or flag");
        Equal(seed + 1, OwnershipSeed(doc), "Creation allocated more than the manager handle");
        Check(ReferenceEquals(manager.Owner, root) && manager.PersistentReactors.SequenceEqual(new[] { root }), "Canonical owner/reactor mismatch");
        Check(ReferenceEquals(root["ACAD_SECTION_MANAGER"], manager) && !root.Entries.Single(e => e.Name == "ACAD_SECTION_MANAGER").IsHardOwner, "Canonical soft root entry mismatch");
        Check(!root.IsHardOwner && root.Cloning == DictionaryCloningFlags.UseClone && ReferenceEquals(root["LIFECYCLE_KEEP"], marker), "Creation rewrote existing root metadata");
        Check(!doc.Entities.Remove(first) && !doc.Entities.Remove(second), "Created manager does not guard actual section targets");
        Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(manager));
        Throws<NotSupportedException>(() => doc.Objects.CloneObject(manager, root, "COPY"));
        var loaded = SectionManagerSave(doc, binary);
        // Establish the lifecycle packet baseline after the ordinary save/reload normalization.
        SectionManagerSave(loaded, binary, $"manager-lifecycle-created-{version}-{binary}.dxf");
        var again = SectionMembershipManager(loaded); string oldHandle = again.Handle; var oldSections = again.Sections; var oldTags = again.Tags;
        var survivors = loaded.Entities.Sections.ToArray(); var keep = loaded.Objects.Root["LIFECYCLE_KEEP"];
        again.ReplaceSections(new[] { again.Sections[1] }, true);
        Check(oldSections.Count == 3 && oldTags.Count == 6, "Explicit edit mutated prior creation snapshots");
        loaded.Objects.EraseSectionManager(again);
        Check(again.IsErased && again.Database == null && again.Owner == null && again.Handle == oldHandle, "Erasure did not preserve terminal inspection identity");
        Check(again.Sections.Count == 1 && again.RequiresFullUpdate && oldSections.Count == 3, "Erasure changed stored inspection snapshots");
        Check(survivors.All(s => ReferenceEquals(loaded.GetObjectByHandle(s.Handle), s)), "Manager erasure cascaded into section members");
        Check(ReferenceEquals(loaded.Objects.Root["LIFECYCLE_KEEP"], keep) && !loaded.Objects.Root.Contains("ACAD_SECTION_MANAGER"), "Manager erasure changed unrelated root entries");
        Equal(0, loaded.Classes["SECTION_MANAGER"].InstanceCount!.Value, "Erasure left a stale class count");
        SectionManagerSave(loaded, binary, $"manager-lifecycle-erased-{version}-{binary}.dxf");
        ManagerLifecycleReject(() => loaded.Objects.Root.Add("REVIVE", again));
        var replacement = loaded.Objects.CreateSectionManager(new[] { survivors[1], survivors[0], survivors[1] }, true);
        Check(replacement.Handle != oldHandle && !ReferenceEquals(replacement, again), "Explicit recreation reused terminal identity");
        Equal(1, loaded.Classes["SECTION_MANAGER"].InstanceCount!.Value, "Recreation did not update existing class count");
        SectionManagerSave(loaded, binary, $"manager-lifecycle-recreated-{version}-{binary}.dxf");
        Equal(0, loaded.Objects.Validate().Count, "Recreated graph validation");
    }
    private static void ManagerLifecycleEmpty(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var manager = doc.Objects.CreateSectionManager(Array.Empty<Section>(), true);
        Check(manager.RequiresFullUpdate && manager.Sections.Count == 0 && manager.Tags.Count == 3, "Explicit empty manager packet changed");
        Check(!doc.Classes.Contains("SECTION_MANAGER"), "Creation synthesized a class declaration before output");
        doc.AnalyzeVersionCompatibility(version); Check(!doc.Classes.Contains("SECTION_MANAGER"), "Analysis synthesized a class declaration");
        var loaded = SectionManagerSave(doc, binary, $"manager-lifecycle-empty-{version}-{binary}.dxf");
        Check(SectionMembershipManager(loaded).Sections.Count == 0, "Empty manager did not reload");
        doc.Objects.EraseSectionManager(manager);
        Check(!doc.Classes.Contains("SECTION_MANAGER"), "Erasure synthesized an absent class declaration");
    }
    private sealed class ManagerLifecycleEnumerable : IEnumerable<Section>
    {
        private readonly Section section; private readonly Action? get; private readonly Action? dispose;
        internal ManagerLifecycleEnumerable(Section section, Action? get = null, Action? dispose = null) { this.section = section; this.get = get; this.dispose = dispose; }
        public IEnumerator<Section> GetEnumerator() { this.get?.Invoke(); return new Enumerator(this.section, this.dispose); }
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        private sealed class Enumerator : IEnumerator<Section>
        {
            private bool moved; private readonly Action? dispose;
            internal Enumerator(Section section, Action? dispose) { this.Current = section; this.dispose = dispose; }
            public Section Current { get; }
            object IEnumerator.Current => this.Current;
            public bool MoveNext() { if (this.moved) return false; this.moved = true; return true; }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => this.dispose?.Invoke();
        }
    }
    private static void ManagerLifecycleCreateRejected(bool binary, string fault)
    {
        var (doc, first, _) = ManagerLifecycleDocument(); var (foreignDoc, foreign, _) = ManagerLifecycleDocument();
        Equal(first.Handle, foreign.Handle, "Foreign member probe must use a coinciding handle");
        var erased = SectionExample(); doc.Entities.Add(erased); doc.Objects.EraseSection(erased);
        IEnumerable<Section> members = new[] { first }; Action? repair = null;
        IEnumerable<Section> Throwing() { yield return first; throw new InvalidOperationException("MoveNext failed"); }
        IEnumerable<Section> Reentry(bool caught) { yield return first; if (caught) ManagerLifecycleReject(() => doc.Objects.CreateSectionManager(Array.Empty<Section>(), false)); else doc.Objects.CreateSectionManager(Array.Empty<Section>(), false); }
        Action anchor = () => doc.Objects.Root.Add("ACAD_SECTION_MANAGER", new DxfXRecord());
        if (fault == "null-list") members = null!;
        else if (fault == "null-entry") members = new[] { first, null! };
        else if (fault == "detached") members = new[] { new Section() };
        else if (fault == "foreign") members = new[] { foreign };
        else if (fault == "erased") members = new[] { erased };
        else if (fault == "excessive") members = Enumerable.Repeat(first, 65537);
        else if (fault == "move-throws") members = Throwing();
        else if (fault == "get-throws") members = new ManagerLifecycleEnumerable(first, get: () => throw new InvalidOperationException("GetEnumerator failed"));
        else if (fault == "dispose-throws") members = new SectionMembershipDisposingEnumerable(first);
        else if (fault == "reentry" || fault == "caught-reentry") members = Reentry(fault == "caught-reentry");
        else if (fault == "anchor") anchor();
        else if (fault == "lower-anchor") doc.Objects.Root.Add("acad_section_manager", new DxfXRecord());
        else if (fault == "existing" || fault == "orphan") { doc.Objects.CreateSectionManager(Array.Empty<Section>(), false); if (fault == "orphan") doc.Objects.Root.Remove("ACAD_SECTION_MANAGER"); }
        else if (fault == "opaque") { var raw = SectionManagerFixture(DxfVersion.AutoCad2018); var record = SectionManagerRecord(raw); doc = SectionManagerLoad(raw.WithRecord(record, record.Tags.Concat(new[] { new DxfTag(1, "private") })), binary); members = new[] { doc.Entities.Sections.First() }; }
        else if (fault == "class") doc.Classes.Add(new DxfClass("SECTION_MANAGER", "WrongClass", "ObjectDBX Classes"));
        else if (fault == "profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2004;
        else if (fault == "seed") ManagerLifecycleSetSeed(doc, long.MaxValue);
        else if (fault == "get-anchor") members = new ManagerLifecycleEnumerable(first, get: anchor);
        else if (fault == "dispose-anchor") members = new ManagerLifecycleEnumerable(first, dispose: anchor);
        else if (fault == "dispose-class") members = new ManagerLifecycleEnumerable(first, dispose: () => doc.Classes.Add(new DxfClass("SECTION_MANAGER", "WrongClass", "ObjectDBX Classes")));
        else if (fault == "dispose-profile") members = new ManagerLifecycleEnumerable(first, dispose: () => doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2004);
        else if (fault == "dispose-target") members = new ManagerLifecycleEnumerable(first, dispose: () => { Check(doc.Entities.Remove(first), "Caller removal failed"); repair = () => doc.Entities.Add(first); });
        else if (fault == "dispose-seed") members = new ManagerLifecycleEnumerable(first, dispose: () => ManagerLifecycleSetSeed(doc, long.MaxValue));
        long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count; var oldManagers = doc.Objects.Items.Where(o => o.CodeName is "SECTION_MANAGER" or "SECTIONMANAGER").ToArray();
        ManagerLifecycleReject(() => doc.Objects.CreateSectionManager(members, true));
        Check(oldManagers.SequenceEqual(doc.Objects.Items.Where(o => o.CodeName is "SECTION_MANAGER" or "SECTIONMANAGER")), "Rejected creation registered a partial manager");
        int expectedAdded = fault is "get-anchor" or "dispose-anchor" ? 1 : 0;
        Equal(count + expectedAdded, doc.Objects.Items.Count, "Rejected creation mutated object registration beyond caller changes");
        if (fault != "dispose-seed") Equal(seed + expectedAdded, OwnershipSeed(doc), "Rejected creation allocated a handle beyond caller changes");
        else Equal(long.MaxValue, OwnershipSeed(doc), "Caller seed mutation was rolled back or advanced");
        if (fault is "existing" or "orphan" or "opaque") return;
        if (doc.Objects.Root.Contains("ACAD_SECTION_MANAGER")) doc.Objects.EraseOwnedTree((DxfDatabaseObject)doc.Objects.Root["ACAD_SECTION_MANAGER"]);
        doc.Classes.Remove("SECTION_MANAGER"); doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
        if (fault is "seed" or "dispose-seed") ManagerLifecycleSetSeed(doc, 4096);
        repair?.Invoke(); var recovery = doc.Objects.CreateSectionManager(new[] { first }, false);
        Check(recovery.Sections.Single() == first, "Rejected creation left the database creation lock held");
        SectionManagerSave(doc, binary);
    }
    private static void ManagerLifecycleIncoming(bool binary, string field)
    {
        var (doc, first, _) = ManagerLifecycleDocument(); var manager = doc.Objects.CreateSectionManager(new[] { first }, false);
        var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line); Action release;
        if (field is "xrecord330" or "xrecord340")
        { var record = new DxfXRecord(); record.Data.Add(new DxfTag((short)(field == "xrecord330" ? 330 : 340), manager.Handle)); doc.Objects.Root.Add("BLOCKER", record); release = () => record.Data.Clear(); }
        else if (field is "xdata" or "attribute-xdata")
        {
            DxfObject carrier = line;
            if (field == "attribute-xdata") { var block = new Block("LIFECYCLE_ATTRIBUTE"); block.AttributeDefinitions.Add(new AttributeDefinition("TAG")); var insert = new Insert(block); doc.Entities.Add(insert); carrier = insert.Attributes.Single(); }
            var data = new XData(new ApplicationRegistry("MANAGER_INCOMING")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, manager.Handle)); carrier.XData.Add(data); release = () => carrier.XData.Remove("MANAGER_INCOMING");
        }
        else if (field == "reactor") { line.PersistentReactors.Add(manager); release = () => line.PersistentReactors.Remove(manager); }
        else if (field == "default") { var dict = new DxfDictionaryWithDefault(); doc.Objects.Root.Add("DEFAULT", dict); dict.Default = manager; release = () => dict.Default = null; }
        else if (field == "settings") { var settings = new DxfSectionSettings(); settings.SetTypeSettings(new[] { new DxfSectionTypeSettings(1, 0, new DxfObject[] { manager }, null!, "", Array.Empty<DxfSectionGeometrySettings>()) }); doc.Objects.SetSectionSettings(first, settings); release = () => settings.SetTypeSettings(Array.Empty<DxfSectionTypeSettings>()); }
        else if (field == "header") { doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$MANAGER_REF", 330, manager.Handle)); release = () => doc.DrawingVariables.RemoveCustomVariable("$MANAGER_REF"); }
        else { var extension = new DxfDictionary(); var note = new DxfXRecord(); extension.Add("NOTE", note); doc.Objects.SetExtensionDictionary(manager, extension); var record = new DxfXRecord(); record.Data.Add(new DxfTag(330, note.Handle)); doc.Objects.Root.Add("BLOCKER", record); release = () => record.Data.Clear(); }
        long seed = OwnershipSeed(doc); var objects = doc.Objects.Items.ToArray(); var sections = manager.Sections; var tags = manager.Tags;
        Throws<InvalidOperationException>(() => doc.Objects.EraseSectionManager(manager));
        Check(objects.SequenceEqual(doc.Objects.Items) && !manager.IsErased && manager.Sections.SequenceEqual(sections) && ReferenceEquals(manager.Tags, tags), "Blocked erasure partially mutated graph or snapshots");
        Equal(seed, OwnershipSeed(doc), "Blocked erasure allocated handles");
        release(); doc.Objects.EraseSectionManager(manager); Check(ReferenceEquals(doc.GetObjectByHandle(first.Handle), first), "Released erasure removed referenced section"); SectionManagerSave(doc, binary);
    }
    private static void ManagerLifecycleEraseRejected(bool binary, string fault)
    {
        var (doc, first, _) = ManagerLifecycleDocument(); var manager = doc.Objects.CreateSectionManager(new[] { first }, false); Action? repair = null;
        DxfStoredSectionManager selected = manager;
        if (fault == "foreign") { var other = new DxfDocument(DxfVersion.AutoCad2018); selected = other.Objects.CreateSectionManager(Array.Empty<Section>(), false); }
        else if (fault == "null") selected = null!;
        else if (fault == "erased") doc.Objects.EraseSectionManager(manager);
        else if (fault == "profile") { doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013; repair = () => doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018; }
        else if (fault == "root") { doc.Objects.Root.Remove("ACAD_SECTION_MANAGER"); repair = () => doc.Objects.Root.Add("ACAD_SECTION_MANAGER", manager, false); }
        else if (fault == "reactors") { manager.PersistentReactors.Add(first); repair = () => manager.PersistentReactors.Remove(first); }
        else if (fault == "opaque-owned")
        {
            var extension = new DxfDictionary(); var note = new DxfXRecord(); extension.Add("OPAQUE", note); doc.Objects.SetExtensionDictionary(manager, extension);
            var raw = DxfRawDocument.Load(new MemoryStream(SectionBytes(doc, false))); var row = raw.Sections.SelectMany(s => s.Records).Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == note.Handle));
            var tags = row.Tags.TakeWhile(t => t.Code != 100).Select(t => t.Code == 0 ? new DxfTag(0, "PRIVATE_MANAGER_NOTE") : t).Concat(new[] { new DxfTag(100, "AcDbPrivateManagerNote"), new DxfTag(1, "opaque") });
            doc = SectionManagerLoad(raw.WithRecord(row, tags), binary); manager = SectionMembershipManager(doc); selected = manager;
        }
        var objects = doc.Objects.Items.ToArray(); long seed = OwnershipSeed(doc); bool erasedBefore = manager.IsErased;
        ManagerLifecycleReject(() => doc.Objects.EraseSectionManager(selected));
        Check(objects.SequenceEqual(doc.Objects.Items) && manager.IsErased == erasedBefore, "Rejected erasure mutated registration"); Equal(seed, OwnershipSeed(doc), "Rejected erasure changed seed");
        repair?.Invoke(); if (fault is not "erased" and not "opaque-owned") { doc.Objects.EraseSectionManager(manager); SectionManagerSave(doc, binary); }
    }
    private static void ManagerLifecycleClass(bool binary, string declaration)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); DxfClass? definition = null;
        if (declaration != "absent") { definition = ManagerLifecycleClassDefinition(declaration == "count-absent" ? null : 37); doc.Classes.Add(definition); }
        var manager = doc.Objects.CreateSectionManager(Array.Empty<Section>(), true);
        Check(definition == null ? !doc.Classes.Contains("SECTION_MANAGER") : ReferenceEquals(doc.Classes["SECTION_MANAGER"], definition) && definition.InstanceCount == (declaration == "count-absent" ? null : 1), "Creation changed CLASS identity or absent/present count policy");
        if (declaration == "edited") { definition!.ApplicationName = "Inspection metadata"; definition.ProxyFlags = 123; definition.WasProxy = true; definition.IsEntity = true; definition.InstanceCount = 37; }
        doc.AnalyzeVersionCompatibility(DxfVersion.AutoCad2013);
        Check(definition == null ? !doc.Classes.Contains("SECTION_MANAGER") : definition.InstanceCount == (declaration == "count-absent" ? null : declaration == "edited" ? 37 : 1), "Analysis mutated CLASS declaration");
        doc.Objects.EraseSectionManager(manager);
        Check(definition == null ? !doc.Classes.Contains("SECTION_MANAGER") : ReferenceEquals(doc.Classes["SECTION_MANAGER"], definition) && definition.InstanceCount == (declaration == "count-absent" ? null : 0), "Erasure changed CLASS identity or count absence");
        if (declaration == "edited") Check(definition!.ApplicationName == "Inspection metadata" && definition.ProxyFlags == 123 && definition.WasProxy && definition.IsEntity, "Erasure rewrote unrelated CLASS metadata");
        SectionManagerSave(doc, binary);
    }
    private static void ManagerLifecycleRequiredMetadata(bool binary, string fault)
    {
        var (doc, first, _) = ManagerLifecycleDocument(); var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line);
        DxfObject carrier = fault.StartsWith("section", StringComparison.Ordinal) ? first : fault.StartsWith("root", StringComparison.Ordinal) ? doc.Objects.Root : line;
        long callbackSeed = -1; DxfDatabaseObject[]? callbackObjects = null;
        var members = new ManagerLifecycleEnumerable(first, dispose: () =>
        {
            if (fault.EndsWith("xdata", StringComparison.Ordinal))
            {
                var data = new XData(new ApplicationRegistry("MANAGER_REQUIRED_METADATA"));
                data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "ABCDEF")); carrier.XData.Add(data);
            }
            else carrier.PersistentReactors.Add(new DxfXRecord());
            callbackSeed = OwnershipSeed(doc); callbackObjects = doc.Objects.Items.ToArray();
        });
        if (fault == "unrelated-line")
        {
            var manager = doc.Objects.CreateSectionManager(members, false);
            Check(manager.Sections.Single() == first && line.PersistentReactors.Count == 1, "Creation validated unrelated entity metadata or rolled back caller changes");
            Equal(callbackSeed + 1, OwnershipSeed(doc), "Creation with unrelated invalid metadata allocated unexpected handles");
            line.PersistentReactors.Clear(); SectionManagerSave(doc, binary); return;
        }
        Throws<InvalidOperationException>(() => doc.Objects.CreateSectionManager(members, false));
        Check(callbackObjects != null && callbackObjects.SequenceEqual(doc.Objects.Items) && !doc.Objects.Root.Contains("ACAD_SECTION_MANAGER"), "Required metadata rejection partially installed a manager");
        Equal(callbackSeed, OwnershipSeed(doc), "Required metadata rejection allocated after the caller callback");
        if (fault.EndsWith("xdata", StringComparison.Ordinal)) carrier.XData.Remove("MANAGER_REQUIRED_METADATA"); else carrier.PersistentReactors.Clear();
        var recovery = doc.Objects.CreateSectionManager(new[] { first }, false); Check(recovery.Sections.Single() == first, "Required metadata rejection left creation locked");
        SectionManagerSave(doc, binary);
    }
    private static void ManagerLifecycleMaximum(bool binary)
    {
        var (doc, first, _) = ManagerLifecycleDocument(); var manager = doc.Objects.CreateSectionManager(Enumerable.Repeat(first, 65536), false);
        Equal(65536, manager.Sections.Count, "Maximum creation count rejected"); var loaded = SectionManagerSave(doc, binary);
        var again = SectionMembershipManager(loaded); Check(again.Sections.Count == 65536 && again.Sections.All(s => ReferenceEquals(s, again.Sections[0])), "Maximum creation collapsed repeated actual identities");
    }
    private static void ManagerLifecycleRetainedAllocation(bool binary)
    {
        var (doc, _, _) = ManagerLifecycleDocument(); var block = new Block("MANAGER_HANDLE_RESERVATION"); block.AttributeDefinitions.Add(new AttributeDefinition("TAG")); doc.Entities.Add(new Insert(block));
        var loaded = SectionManagerSave(doc, binary); var attribute = loaded.Entities.Inserts.Single().Attributes.Single();
        Check(loaded.GetObjectByHandle(attribute.Handle) == null, "Retained allocation probe must use an owner-held identity");
        var raw = DxfRawDocument.Load(new MemoryStream(SectionBytes(loaded, false))); var handles = raw.Sections.Where(s => s.Name != "HEADER").SelectMany(s => s.Records).SelectMany(r => r.Tags.Where(t => t.Code == 5)).Select(t => (string)t.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long normalSeed = OwnershipSeed(loaded);
        ManagerLifecycleSetSeed(loaded, Convert.ToInt64(attribute.Handle, 16));
        var manager = loaded.Objects.CreateSectionManager(loaded.Entities.Sections, false);
        Check(!handles.Contains(manager.Handle) && attribute.Owner != null, "Manager allocation captured a live retained ATTRIB/ENDBLK identity");
        ManagerLifecycleSetSeed(loaded, Math.Max(normalSeed, OwnershipSeed(loaded))); SectionManagerSave(loaded, binary);
    }
    private static void ManagerLifecycleOwnedAliases(bool binary)
    {
        var (doc, first, _) = ManagerLifecycleDocument(); var manager = doc.Objects.CreateSectionManager(new[] { first }, false);
        doc.Objects.Root.Add("MANAGER_ALIAS", manager, true); var extension = new DxfDictionary(); var note = new DxfXRecord(); note.Data.Add(new DxfTag(1, "owned note")); extension.Add("NOTE", note); extension.Add("ALIAS", note);
        doc.Objects.SetExtensionDictionary(manager, extension); var arbitrary = new DxfXRecord(); arbitrary.Data.Add(new DxfTag(320, manager.Handle)); doc.Objects.Root.Add("ARBITRARY", arbitrary);
        string[] handles = { manager.Handle, extension.Handle, note.Handle }; long seed = OwnershipSeed(doc);
        doc.Objects.EraseSectionManager(manager);
        Check(handles.All(h => doc.GetObjectByHandle(h) == null) && extension.IsErased && note.IsErased, "Explicit erase left typed owned metadata registered");
        Check(!doc.Objects.Root.Contains("MANAGER_ALIAS") && !doc.Objects.Root.Contains("ACAD_SECTION_MANAGER") && ReferenceEquals(doc.Objects.Root["ARBITRARY"], arbitrary), "Alias erasure or arbitrary handle policy changed");
        Equal(seed, OwnershipSeed(doc), "Erasure allocated handles"); Check(doc.Entities.Remove(first), "Erasure retained outgoing membership dependency"); SectionManagerSave(doc, binary);
    }
    private static void ManagerLifecycleProfileSave(bool binary)
    {
        var (doc, first, _) = ManagerLifecycleDocument(); var manager = doc.Objects.CreateSectionManager(new[] { first }, false); long seed = OwnershipSeed(doc);
        doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013; using var bytes = new MemoryStream();
        bool rejected = false;
        try { rejected = !doc.Save(bytes, binary); } catch (InvalidOperationException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Profile conversion was accepted"); Equal(0L, bytes.Length, "Profile rejection emitted partial output"); Equal(seed, OwnershipSeed(doc), "Profile rejection allocated handles");
        doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018; SectionManagerSave(doc, binary); doc.Objects.EraseSectionManager(manager); doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013; SectionManagerSave(doc, binary);
    }
    private static void ManagerLifecycleNative(bool input, bool binary)
    {
        byte[] bytes = SectionManagerNativeBytes(); var doc = input ? SectionManagerLoad(DxfRawDocument.Load(new MemoryStream(bytes)), true) : DxfDocument.Load(new MemoryStream(bytes))!;
        var manager = SectionMembershipManager(doc); var section = manager.Sections.Single(); var settings = section.GeometrySettings; var root = doc.Objects.Root;
        var entries = root.Entries.Where(e => !ReferenceEquals(e.Target, manager)).Select(e => (e.Name, e.Target, e.IsHardOwner)).ToArray();
        bool hard = root.IsHardOwner; var cloning = root.Cloning; var definition = doc.Classes[manager.CodeName]; var metadata = (definition.ApplicationName, definition.ProxyFlags, definition.WasProxy, definition.IsEntity);
        SectionManagerSave(doc, binary, $"manager-lifecycle-native-before-{input}-{binary}.dxf");
        doc.Objects.EraseSectionManager(manager);
        Check(ReferenceEquals(doc.GetObjectByHandle("228"), section) && ReferenceEquals(doc.GetObjectByHandle("22A"), settings), "Native erase replaced or removed actual SECTION/settings identities");
        Check(entries.SequenceEqual(root.Entries.Select(e => (e.Name, e.Target, e.IsHardOwner))) && root.IsHardOwner == hard && root.Cloning == cloning, "Native erase changed root flags or unrelated entries");
        Check(metadata == (definition.ApplicationName, definition.ProxyFlags, definition.WasProxy, definition.IsEntity) && definition.InstanceCount == 0, "Native erase changed CLASS metadata or retained stale count");
        var loaded = SectionManagerSave(doc, binary, $"manager-lifecycle-native-erased-{input}-{binary}.dxf");
        Check(!loaded.Objects.Items.OfType<DxfStoredSectionManager>().Any() && loaded.GetObjectByHandle("229") == null && loaded.GetObjectByHandle("228") is Section, "Native output resynthesized an erased manager or removed SECTION");
    }
}
