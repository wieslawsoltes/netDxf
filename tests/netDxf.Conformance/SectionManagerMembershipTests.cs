using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSectionManagerMembershipTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (string spelling in new[] { "SECTION_MANAGER", "SECTIONMANAGER" })
        foreach (bool binary in new[] { false, true })
        {
            var v = version; string s = spelling; bool b = binary;
            Run($"section-membership/edit/{v}/{s}/{b}", () => SectionMembershipEdit(v, s, b));
            Run($"section-membership/mapping/{v}/{s}/{b}", () => SectionMembershipMap(v, s, b));
            Run($"section-membership/metadata/{v}/{s}/{b}", () => SectionMembershipMetadata(v, s, b));
        }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            foreach (string defect in new[] { "null-list", "null-entry", "detached", "foreign", "erased", "excessive", "enumerator-throws", "dispose-throws", "reentry", "enumerator-root", "enumerator-target", "profile", "reactors", "root" })
            { string d = defect; Run($"section-membership/rejected/{b}/{d}", () => SectionMembershipRejected(b, d)); }
            Run($"section-membership/maximum/{b}", () => SectionMembershipMaximum(b));
            Run($"section-membership/caught-reentry/{b}", () => SectionMembershipCaughtReentry(b));
        }
        foreach (bool input in new[] { false, true }) foreach (bool output in new[] { false, true })
        { bool i = input, o = output; Run($"section-membership/native/{i}/{o}", () => SectionMembershipNative(i, o)); }
    }

    private static DxfStoredSectionManager SectionMembershipManager(DxfDocument doc) => doc.Objects.Items.OfType<DxfStoredSectionManager>().Single();

    private static void SectionMembershipEdit(DxfVersion version, string spelling, bool binary)
    {
        var doc = SectionManagerLoad(SectionManagerFixture(version, spelling), binary); var manager = SectionMembershipManager(doc);
        var first = manager.Sections[0]; var second = manager.Sections[1]; var oldSections = manager.Sections; var oldTags = manager.Tags;
        var third = SectionExample("SECTIONOBJECT"); third.Name = "Manager third"; doc.Entities.Add(third);
        string handle = manager.Handle, owner = manager.Owner.Handle; long seed = OwnershipSeed(doc);
        manager.ReplaceSections(new[] { second, third, second }, false);
        Check(!manager.RequiresFullUpdate && manager.Sections.SequenceEqual(new[] { second, third, second }), "Explicit membership order/flag changed");
        Check(oldSections.SequenceEqual(new[] { first, second, first }) && oldTags[1].Value.Equals((short)1), "Previously returned membership/packet snapshot mutated");
        Equal(seed, OwnershipSeed(doc), "Membership edit assigned handles"); Equal(handle, manager.Handle, "Manager identity changed"); Equal(owner, manager.Owner.Handle, "Manager owner changed");
        Throws<NotSupportedException>(() => ((IList<Section>)manager.Sections).Clear()); Throws<NotSupportedException>(() => ((IList<DxfTag>)manager.Tags).Clear());
        Check(doc.Entities.Remove(first), "Released original section remained referenced");
        Check(!doc.Entities.Remove(second) && !doc.Entities.Remove(third), "New manager target was removable");
        Throws<InvalidOperationException>(() => doc.Objects.EraseSection(second));
        var loaded = SectionManagerSave(doc, binary, $"section-membership-edited-{version}-{spelling}-{binary}.dxf");
        var again = SectionMembershipManager(loaded);
        Check(again.Sections.Select(s => s.Name).SequenceEqual(new[] { "Manager second", "Manager third", "Manager second" }) && !again.RequiresFullUpdate, "Edited packet reload changed order or independent update flag");
        manager.ReplaceSections(Array.Empty<Section>(), true);
        Check(manager.RequiresFullUpdate && manager.Sections.Count == 0, "Explicit empty/update-true combination changed");
        Check(doc.Entities.Remove(second) && doc.Entities.Remove(third), "Cleared manager retained incoming dependencies");
        SectionManagerSave(doc, binary, $"section-membership-empty-{version}-{spelling}-{binary}.dxf");
        Throws<NotSupportedException>(() => doc.Objects.CloneObject(manager, doc.Objects.Root, "MANAGER_COPY"));
        Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(manager));
        Equal(0, doc.Objects.Validate().Count, "Edited manager graph validation");
    }

    private static void SectionMembershipMap(DxfVersion version, string spelling, bool binary)
    {
        var source = SectionManagerLoad(SectionManagerFixture(version, spelling), binary);
        var destination = SectionManagerLoad(SectionManagerFixture(version, spelling), binary);
        var original = SectionMembershipManager(source); var target = SectionMembershipManager(destination);
        var before = OwnershipTagValues(target.Tags).ToArray(); long seed = OwnershipSeed(destination);
        Throws<ArgumentException>(() => target.ReplaceSections(original.Sections, false));
        Check(before.SequenceEqual(OwnershipTagValues(target.Tags)), "Foreign identities changed manager packet"); Equal(seed, OwnershipSeed(destination), "Foreign membership attempt allocated handles");
        var map = new Dictionary<Section, Section> { { original.Sections[0], target.Sections[0] }, { original.Sections[1], target.Sections[1] } };
        var requested = new[] { original.Sections[1], original.Sections[0], original.Sections[1] };
        target.ReplaceSections(requested.Select(section => map[section]), true);
        Check(target.Sections.All(s => !original.Sections.Contains(s)), "Mapped membership retained foreign section objects");
        Check(original.Sections.Select(s => s.Name).SequenceEqual(new[] { "Manager first", "Manager second", "Manager first" }), "Mapped membership edited the source");
        SectionManagerSave(destination, binary, $"section-membership-mapped-{version}-{spelling}-{binary}.dxf");
        Throws<NotSupportedException>(() => destination.Objects.CloneObject(original, destination.Objects.Root, "MANAGER_COPY"));
    }

    private static void SectionMembershipMetadata(DxfVersion version, string spelling, bool binary)
    {
        var doc = SectionManagerLoad(SectionManagerFixture(version, spelling), binary); var manager = SectionMembershipManager(doc);
        var first = manager.Sections[0]; var second = manager.Sections[1];
        var data = new XData(new ApplicationRegistry("MEMBERSHIP_METADATA")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "Keep metadata"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, first.Handle)); manager.XData.Add(data);
        var extension = new DxfDictionary(); doc.Objects.SetExtensionDictionary(manager, extension);
        var entry = new DxfXRecord(); entry.Data.Add(new DxfTag(1, "Keep extension")); extension.Add("ANNOTATION", entry, true);
        var packet = OwnershipTagValues(manager.Tags).ToArray(); var reactors = manager.PersistentReactors.ToArray(); var storedData = manager.XData["MEMBERSHIP_METADATA"];
        manager.ReplaceSections(new[] { second }, false);
        Check(ReferenceEquals(manager.ExtensionDictionary, extension) && ReferenceEquals(extension["ANNOTATION"], entry), "Membership edit changed extension graph");
        Check(manager.PersistentReactors.SequenceEqual(reactors) && ReferenceEquals(manager.XData["MEMBERSHIP_METADATA"], storedData), "Membership edit changed common metadata");
        Check(!doc.Entities.Remove(first), "Independent retained XData dependency was released with membership");
        manager.XData.Remove("MEMBERSHIP_METADATA"); Check(doc.Entities.Remove(first), "Cleared independent XData dependency remained");
        var loaded = SectionManagerSave(doc, binary); var again = SectionMembershipManager(loaded);
        Equal("Keep extension", (string)((DxfXRecord)again.ExtensionDictionary["ANNOTATION"]).Data[0].Value, "Extension payload changed on edited-manager save");
        Check(!packet.SequenceEqual(OwnershipTagValues(manager.Tags)), "Membership did not change public packet");
    }

    private static void SectionMembershipRejected(bool binary, string defect)
    {
        var doc = SectionManagerLoad(SectionManagerFixture(DxfVersion.AutoCad2018), binary); var manager = SectionMembershipManager(doc);
        var first = manager.Sections[0]; var before = manager.Sections.ToArray(); var packet = OwnershipTagValues(manager.Tags).ToArray(); bool flag = manager.RequiresFullUpdate;
        var foreignDoc = new DxfDocument(DxfVersion.AutoCad2018); var foreign = SectionExample("SECTIONOBJECT"); foreignDoc.Entities.Add(foreign);
        var erased = SectionExample("SECTIONOBJECT"); doc.Entities.Add(erased); doc.Objects.EraseSection(erased);
        var candidate = SectionExample("SECTIONOBJECT"); doc.Entities.Add(candidate);
        long seed = OwnershipSeed(doc);
        IEnumerable<Section> Throwing() { yield return first; throw new InvalidOperationException("Caller enumeration failed"); }
        IEnumerable<Section> Reentry() { yield return first; manager.ReplaceSections(Array.Empty<Section>(), false); }
        IEnumerable<Section> ChangeRoot() { yield return first; doc.Objects.Root.Remove("ACAD_SECTION_MANAGER"); yield return first; }
        IEnumerable<Section> RemoveTarget() { yield return candidate; Check(doc.Entities.Remove(candidate), "Caller target removal failed"); }
        IEnumerable<Section> members = new[] { first };
        if (defect == "null-list") members = null!;
        else if (defect == "null-entry") members = new[] { first, null! };
        else if (defect == "detached") members = new[] { new Section() };
        else if (defect == "foreign") members = new[] { foreign };
        else if (defect == "erased") members = new[] { erased };
        else if (defect == "excessive") members = Enumerable.Repeat(first, 65537);
        else if (defect == "enumerator-throws") members = Throwing();
        else if (defect == "dispose-throws") members = new SectionMembershipDisposingEnumerable(first);
        else if (defect == "reentry") members = Reentry();
        else if (defect == "enumerator-root") members = ChangeRoot();
        else if (defect == "enumerator-target") members = RemoveTarget();
        else if (defect == "profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013;
        else if (defect == "reactors") manager.PersistentReactors.Add(doc.Objects.Root);
        else if (defect == "root") doc.Objects.Root.Remove("ACAD_SECTION_MANAGER");
        bool rejected = false;
        try { manager.ReplaceSections(members, false); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { rejected = true; }
        Check(rejected, "Invalid manager membership request was accepted");
        Check(before.SequenceEqual(manager.Sections) && packet.SequenceEqual(OwnershipTagValues(manager.Tags)) && flag == manager.RequiresFullUpdate, "Rejected membership request partially changed manager");
        Equal(seed, OwnershipSeed(doc), "Rejected membership request allocated handles");
        if (defect is "null-list" or "null-entry" or "detached" or "foreign" or "erased" or "excessive" or "enumerator-throws" or "dispose-throws" or "reentry" or "enumerator-target")
        { manager.ReplaceSections(new[] { first }, false); Check(manager.Sections.Count == 1 && !manager.RequiresFullUpdate, "Failed edit left the manager locked"); }
    }

    private sealed class SectionMembershipDisposingEnumerable : IEnumerable<Section>
    {
        private readonly Section section;
        internal SectionMembershipDisposingEnumerable(Section section) { this.section = section; }
        public IEnumerator<Section> GetEnumerator() => new Enumerator(this.section);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
        private sealed class Enumerator : IEnumerator<Section>
        {
            private bool moved;
            internal Enumerator(Section section) { this.Current = section; }
            public Section Current { get; }
            object System.Collections.IEnumerator.Current => this.Current;
            public bool MoveNext() { if (this.moved) return false; this.moved = true; return true; }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => throw new InvalidOperationException("Caller disposal failed");
        }
    }

    private static void SectionMembershipCaughtReentry(bool binary)
    {
        var doc = SectionManagerLoad(SectionManagerFixture(DxfVersion.AutoCad2018), binary); var manager = SectionMembershipManager(doc);
        var first = manager.Sections[0]; var second = manager.Sections[1]; var oldTags = manager.Tags; bool caught = false;
        IEnumerable<Section> Members()
        {
            yield return second;
            try { manager.ReplaceSections(Array.Empty<Section>(), true); }
            catch (InvalidOperationException) { caught = true; }
            Check(ReferenceEquals(oldTags, manager.Tags), "Rejected nested edit mutated the manager");
            yield return first;
        }
        manager.ReplaceSections(Members(), false);
        Check(caught && manager.Sections.SequenceEqual(new[] { second, first }) && !manager.RequiresFullUpdate, "Caught nested edit prevented valid outer replacement");
        var again = SectionMembershipManager(SectionManagerSave(doc, binary));
        Check(again.Sections.Select(s => s.Name).SequenceEqual(new[] { "Manager second", "Manager first" }) && !again.RequiresFullUpdate, "Outer replacement after caught reentry did not survive save");
    }

    private static void SectionMembershipMaximum(bool binary)
    {
        var doc = SectionManagerLoad(SectionManagerFixture(DxfVersion.AutoCad2018), binary); var manager = SectionMembershipManager(doc); var first = manager.Sections[0];
        manager.ReplaceSections(Enumerable.Repeat(first, 65536), false);
        Equal(65536, manager.Sections.Count, "Maximum membership rejected"); Equal(65539, manager.Tags.Count, "Maximum packet count");
        var again = SectionMembershipManager(SectionManagerSave(doc, binary));
        Check(again.Sections.Count == 65536 && again.Sections.All(s => ReferenceEquals(s, again.Sections[0])) && !again.RequiresFullUpdate, "Maximum list collapsed repeated identities");
    }

    private static void SectionMembershipNative(bool input, bool binary)
    {
        byte[] bytes = SectionManagerNativeBytes(); var raw = DxfRawDocument.Load(new MemoryStream(bytes));
        var doc = input ? SectionManagerLoad(raw, true) : DxfDocument.Load(new MemoryStream(bytes))!;
        var manager = SectionMembershipManager(doc); var section = manager.Sections.Single();
        var old = manager.Tags; string handle = manager.Handle; var owner = manager.Owner; var reactors = manager.PersistentReactors.ToArray(); long seed = OwnershipSeed(doc);
        manager.ReplaceSections(new[] { section, section }, true);
        Equal(seed, OwnershipSeed(doc), "Native membership edit allocated handles"); Equal(handle, manager.Handle, "Native manager identity changed");
        Check(ReferenceEquals(owner, manager.Owner) && manager.PersistentReactors.SequenceEqual(reactors), "Native manager common ownership/reactors changed");
        Check(old.Count == 4 && old[1].Value.Equals((short)0), "Native original packet snapshot mutated");
        var loaded = SectionManagerSave(doc, binary, $"section-membership-native-{input}-{binary}.dxf");
        var again = SectionMembershipManager(loaded);
        Check(again.RequiresFullUpdate && again.Sections.Count == 2 && ReferenceEquals(again.Sections[0], again.Sections[1]), "Native edit reload lost repeated identity");
        Equal("228", again.Sections[0].Handle, "Native target identity changed"); Equal("229", again.Handle, "Native manager identity changed on output");
    }
}
