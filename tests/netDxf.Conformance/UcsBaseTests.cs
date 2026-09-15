using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunUcsBaseTests()
    {
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"ucs-base/producer/{version}/{binary}", () => UcsBaseProducer(version, binary));
            Run($"ucs-base/explicit-null/{version}/{binary}", () => UcsBaseNull(version, binary));
            Run($"ucs-base/canonical-forward/{version}/{binary}", () => UcsBaseCanonical(version, binary));
            Run($"ucs-base/private-context/{version}/{binary}", () => UcsBasePrivate(version, binary));
            Run($"ucs-base/cycles/{version}/{binary}", () => UcsBaseCycles(version, binary));
            Run($"ucs-base/all-types/{version}/{binary}", () => UcsBaseTypes(version, binary));
            foreach (string defect in new[] { "negative-type", "unknown-type", "missing-type", "zero-type", "zero-type-null", "duplicate-type", "duplicate-reference", "missing-target", "wrong-target-kind", "discarded-target", "missing-target-identity", "invalid-owner-name", "duplicate-owner-name", "missing-owner-identity" })
                Run($"ucs-base/malformed/{version}/{binary}/{defect}", () => UcsBaseMalformed(version, binary, defect));
        }
        Run("ucs-base/atomic-reference-lifecycle", UcsBaseLifecycle);
        Run("ucs-base/explicit-clone-mapping", UcsBaseClone);
    }
    private static string UcsBaseFile(DxfVersion version, bool binary) => $"ixmilia-ucs-base-R{version.ToString().Replace("AutoCad", "")}-{(binary ? "binary" : "ascii")}.dxf";
    private static DxfRawDocument UcsBaseRaw(DxfVersion version, bool binary)
    {
        string file = UcsBaseFile(version, binary).Replace("ixmilia-", "carrier-"); byte[] bytes = File.ReadAllBytes(Path.Combine("tests", "fixtures", "ucs-record-base", "carriers", file));
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine("tests", "fixtures", "ucs-record-base", "carriers", "manifest.json")));
        string expected = metadata.RootElement.GetProperty("files").EnumerateArray().Single(item => item.GetProperty("file").GetString() == file).GetProperty("sha256").GetString()!;
        Equal(expected, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), "Pinned independent producer bytes");
        return DxfRawDocument.Load(new MemoryStream(bytes));
    }
    private static DxfRawRecord UcsBaseRecord(DxfRawDocument raw, string name) => raw.Sections.Single(section => section.Name == "TABLES").Records.Single(record => record.Name == "UCS" && record.Tags.Any(tag => tag.Code == 2 && (string)tag.Value == name));
    private static DxfDocument UcsBaseLoad(DxfRawDocument raw)
    { using var bytes = new MemoryStream(); raw.Save(bytes); bytes.Position = 0; return DxfDocument.Load(bytes) ?? throw new Exception("UCS base load failed."); }
    private static DxfDocument UcsBaseRoundTrip(DxfDocument doc, bool binary, string? output = null)
    {
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "UCS base save"); if (output != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, output), bytes.ToArray()); bytes.Position = 0;
        return DxfDocument.Load(bytes) ?? throw new Exception("UCS base roundtrip load failed.");
    }
    private static void UcsBaseAssert(DxfDocument doc)
    {
        var child = doc.UCSs["LEFT_FROM_SURVEY"]; var parent = doc.UCSs["SURVEY_BASE"]; var world = doc.UCSs["BOTTOM_FROM_WORLD"];
        Equal((short)5, child.OrthographicViewType, "Stored group79 differs from origin override"); Check(ReferenceEquals(child.BaseUcs, parent), "Actual base object identity"); Equal("E", child.Handle, "Independent child handle"); Equal("F", parent.Handle, "Independent base handle");
        Equal(new Vector3(7, 8, 9), child.OrthographicOrigins[UcsOrthographicType.Right], "Distinct group71 origin override"); Equal(-8.5, child.Elevation, "Child elevation"); Equal(new Vector3(100, 200, 300), child.Origin, "Stored child origin");
        Equal((short)0, parent.OrthographicViewType, "Ordinary base frame"); Check(parent.BaseUcs == null, "Ordinary UCS has no base"); Equal((short)2, world.OrthographicViewType, "WORLD-based orthographic type"); Check(world.BaseUcs == null, "Absent base means WORLD");
        Check(parent.GetReferences().Any(reference => ReferenceEquals(reference.Reference, child) && reference.Uses == 1), "UCS base incoming count"); Check(!doc.UCSs.Remove(parent), "Referenced base cannot be removed"); Equal(1, doc.Entities.Lines.Count(), "Following geometry retained");
        Equal(0, doc.Objects.Validate().Count, "Database remains valid");
    }
    private static void UcsBaseProducer(DxfVersion version, bool binary)
    {
        var doc = UcsBaseLoad(UcsBaseRaw(version, binary));
        for (int cycle = 0; cycle < 3; cycle++) { UcsBaseAssert(doc); doc = UcsBaseRoundTrip(doc, binary, cycle == 2 ? $"ucs-base-{version}-{binary}.dxf" : null); }
        UcsBaseAssert(doc);
        var child = doc.UCSs["LEFT_FROM_SURVEY"]; var parent = child.BaseUcs; child.SetOrthographicBase(2); Check(doc.UCSs.Remove(parent), "Retarget to WORLD releases base"); Equal((short)2, child.OrthographicViewType, "Edited WORLD type");
        doc = UcsBaseRoundTrip(doc, binary); Check(doc.UCSs["LEFT_FROM_SURVEY"].BaseUcs == null, "WORLD edit persists");
    }
    private static void UcsBaseNull(DxfVersion version, bool binary)
    {
        var raw = UcsBaseRaw(version, binary); var child = UcsBaseRecord(raw, "LEFT_FROM_SURVEY"); raw = raw.WithRecord(child, child.Tags.Select(tag => tag.Code == 346 ? new DxfTag(346, "0000") : tag));
        var doc = UcsBaseLoad(raw); var typed = doc.UCSs["LEFT_FROM_SURVEY"]; Equal((short)5, typed.OrthographicViewType, "Explicit-null type"); Check(typed.BaseUcs == null, "Numeric null remains WORLD");
        var clone = (UCS)typed.Clone("NULL_COPY"); doc.UCSs.Add(clone);
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Explicit-null output"); bytes.Position = 0; var after = DxfRawDocument.Load(bytes);
        foreach (string name in new[] { "LEFT_FROM_SURVEY", "NULL_COPY" }) Equal("0", (string)UcsBaseRecord(after, name).Tags.Single(tag => tag.Code == 346).Value, "Physical null346 presence retained through load and clone");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"ucs-base-null-{version}-{binary}.dxf"), bytes.ToArray());
        typed.SetOrthographicBase(5); clone.SetOrthographicBase(0); using var cleared = new MemoryStream(); Check(doc.Save(cleared, binary), "Cleared null presence"); cleared.Position = 0; var edited = DxfRawDocument.Load(cleared);
        Check(!UcsBaseRecord(edited, "LEFT_FROM_SURVEY").Tags.Any(tag => tag.Code == 346) && !UcsBaseRecord(edited, "NULL_COPY").Tags.Any(tag => tag.Code == 346), "Explicit edits can clear physical base slot");
    }
    private static void UcsBaseCanonical(DxfVersion version, bool binary)
    {
        var raw = UcsBaseRaw(version, binary); var parent = UcsBaseRecord(raw, "SURVEY_BASE"); raw = raw.WithRecord(parent, parent.Tags.Select(tag => tag.Code == 5 ? new DxfTag(5, "000f") : tag));
        var child = UcsBaseRecord(raw, "LEFT_FROM_SURVEY"); raw = raw.WithRecord(child, child.Tags.Select(tag => tag.Code == 346 ? new DxfTag(346, "00f") : tag));
        UcsBaseAssert(UcsBaseLoad(raw));
    }
    private static void UcsBasePrivate(DxfVersion version, bool binary)
    {
        foreach (int context in new[] { 0, 1, 2, 3 })
        {
            var raw = UcsBaseRaw(version, binary); var child = UcsBaseRecord(raw, "LEFT_FROM_SURVEY");
            var tags = child.Tags.ToList();
            if (context == 3)
            {
                int at = tags.FindIndex(tag => tag.Code == 79); tags.InsertRange(at, new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(1001, "PRIVATE_APP"), new DxfTag(1000, "Private payload"), new DxfTag(102, "}") });
                raw = raw.WithRecord(child, tags); UcsBaseAssert(UcsBaseLoad(raw)); continue;
            }
            DxfTag[] prefix = context == 0 ? new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(102, "{NESTED") } : context == 1 ? new[] { new DxfTag(100, "PrivateUcs") } : new[] { new DxfTag(1001, "UCS_TRAILER"), new DxfTag(1000, "Stored metadata") };
            tags.AddRange(prefix); tags.Add(new DxfTag(79, (short)7)); tags.Add(new DxfTag(346, "ABCDEF")); if (context == 0) tags.AddRange(new[] { new DxfTag(102, "}"), new DxfTag(102, "}") });
            raw = raw.WithRecord(child, tags); UcsBaseAssert(UcsBaseLoad(raw));
        }
    }
    private static void UcsBaseTypes(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var parent = doc.UCSs.Add(new UCS("BASE"));
        for (short type = 1; type <= 6; type++) doc.UCSs.Add(new UCS("TYPE_" + type)).SetOrthographicBase(type, parent);
        doc = UcsBaseRoundTrip(doc, binary); parent = doc.UCSs["BASE"]; Equal(6, parent.GetReferences().Sum(reference => reference.Uses), "Every orthographic type holds its own reference");
        for (short type = 1; type <= 6; type++)
        {
            var child = doc.UCSs["TYPE_" + type]; Equal(type, child.OrthographicViewType, "All stored view types survive"); Check(ReferenceEquals(child.BaseUcs, parent), "All types resolve the same actual base"); child.SetOrthographicBase(type);
        }
        Check(doc.UCSs.Remove(parent), "All WORLD edits release the common base"); doc = UcsBaseRoundTrip(doc, binary);
        for (short type = 1; type <= 6; type++) { var child = doc.UCSs["TYPE_" + type]; Equal(type, child.OrthographicViewType, "All WORLD types survive"); Check(child.BaseUcs == null, "All WORLD types omit their base"); }
    }
    private static void UcsBaseCycles(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var first = doc.UCSs.Add(new UCS("CYCLE_A")); var second = doc.UCSs.Add(new UCS("CYCLE_B")); var self = doc.UCSs.Add(new UCS("SELF"));
        first.SetOrthographicBase(1, second); second.SetOrthographicBase(2, first); self.SetOrthographicBase(3, self);
        doc = UcsBaseRoundTrip(doc, binary); first = doc.UCSs["CYCLE_A"]; second = doc.UCSs["CYCLE_B"]; self = doc.UCSs["SELF"];
        Check(ReferenceEquals(first.BaseUcs, second) && ReferenceEquals(second.BaseUcs, first) && ReferenceEquals(self.BaseUcs, self), "Cycles remain explicit stored references without recursive evaluation");
        Check(!doc.UCSs.Remove(first) && !doc.UCSs.Remove(second) && !doc.UCSs.Remove(self), "Cycle references protect deletion"); first.SetOrthographicBase(0); self.SetOrthographicBase(0);
        Check(doc.UCSs.Remove(second) && doc.UCSs.Remove(first) && doc.UCSs.Remove(self), "Explicit unlink releases cycles without cascade");
    }
    private static void UcsBaseMalformed(DxfVersion version, bool binary, string defect)
    {
        var raw = UcsBaseRaw(version, binary); var child = UcsBaseRecord(raw, "LEFT_FROM_SURVEY"); var tags = child.Tags.ToList();
        void Set(short code, object value) { int at = tags.FindIndex(tag => tag.Code == code); tags[at] = new DxfTag(code, value); }
        switch (defect)
        {
            case "negative-type": Set(79, (short)-1); break;
            case "unknown-type": Set(79, (short)7); break;
            case "missing-type": tags.RemoveAll(tag => tag.Code == 79); break;
            case "zero-type": Set(79, (short)0); break;
            case "zero-type-null": Set(79, (short)0); Set(346, "0"); break;
            case "duplicate-type": tags.Add(new DxfTag(79, (short)5)); break;
            case "duplicate-reference": tags.Add(new DxfTag(346, "F")); break;
            case "missing-target": Set(346, "ABCDEF"); break;
            case "wrong-target-kind": Set(346, (string)raw.Sections.Single(section => section.Name == "ENTITIES").Records.Single(record => record.Name == "LINE").Tags.Single(tag => tag.Code == 5).Value); break;
            case "discarded-target":
                var invalid = UcsBaseRecord(raw, "SURVEY_BASE"); raw = raw.WithRecord(invalid, invalid.Tags.Select(tag => tag.Code == 2 ? new DxfTag(2, "INVALID/NAME") : tag)); break;
            case "missing-target-identity":
                var target = UcsBaseRecord(raw, "SURVEY_BASE"); raw = raw.WithRecord(target, target.Tags.Where(tag => tag.Code != 5)); break;
            case "invalid-owner-name": Set(2, "INVALID/NAME"); break;
            case "duplicate-owner-name": Set(2, "BOTTOM_FROM_WORLD"); break;
            case "missing-owner-identity": tags.RemoveAll(tag => tag.Code == 5); break;
        }
        child = UcsBaseRecord(raw, "LEFT_FROM_SURVEY"); raw = raw.WithRecord(child, tags); using var bytes = new MemoryStream(); raw.Save(bytes); bytes.Position = 0; bool rejected = false;
        try { rejected = DxfDocument.Load(bytes) == null; } catch (Exception error) when (error is FormatException || error is ArgumentException || error is InvalidDataException) { rejected = true; }
        Check(rejected, "Malformed or unretained UCS base relationship must reject: " + defect);
    }
    private static void UcsBaseLifecycle()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); _ = doc.Objects; var first = doc.UCSs.Add(new UCS("FIRST")); var second = doc.UCSs.Add(new UCS("SECOND")); var child = doc.UCSs.Add(new UCS("CHILD")); child.SetOrthographicBase(5, first);
        var view = doc.Views.Add(new View("USES_FIRST") { Ucs = new ViewUcs { NamedUcs = first } }); var port = doc.VPorts.AddRecord(new VPort("USES_FIRST") { NamedUcs = first });
        Equal(3, first.GetReferences().Sum(reference => reference.Uses), "UCS, VIEW and VPORT counts combine"); long seed = OwnershipSeed(doc);
        foreach (short bad in new short[] { -1, 7 }) Throws<ArgumentOutOfRangeException>(() => child.SetOrthographicBase(bad, second)); Throws<ArgumentException>(() => child.SetOrthographicBase(0, second));
        var foreign = new DxfDocument(DxfVersion.AutoCad2018); var foreignUcs = foreign.UCSs.Add(new UCS("SECOND")); Throws<ArgumentException>(() => child.SetOrthographicBase(2, foreignUcs)); Throws<ArgumentException>(() => child.SetOrthographicBase(2, new UCS("DETACHED")));
        Check(ReferenceEquals(child.BaseUcs, first) && child.OrthographicViewType == 5, "Rejected pair changes neither value"); Equal(seed, OwnershipSeed(doc), "Rejected pair allocates no handles"); Equal(3, first.GetReferences().Sum(reference => reference.Uses), "Rejected pair preserves all counts");
        child.SetOrthographicBase(6, second); Equal(2, first.GetReferences().Sum(reference => reference.Uses), "Previous base count released"); Equal(1, second.GetReferences().Sum(reference => reference.Uses), "New base count added"); child.SetOrthographicBase(4, second); Equal(1, second.GetReferences().Sum(reference => reference.Uses), "Retarget same identity does not duplicate use");
        child.Name = "RENAMED_CHILD"; second.Name = "RENAMED_BASE"; Check(second.GetReferences().Single().Reference == child, "Rename preserves reference identity bookkeeping"); Check(!doc.UCSs.Remove(second), "Referenced base protected after rename");
        Check(doc.UCSs.Remove(child), "Removing referring UCS releases outgoing base"); Check(doc.UCSs.Remove(second), "Base can be removed after last dependent");
        view.Ucs!.NamedUcs = null; port.NamedUcs = null; Check(doc.UCSs.Remove(first), "Mixed owner references release correctly");
    }
    private static void UcsBaseClone()
    {
        var source = new DxfDocument(DxfVersion.AutoCad2018); var parent = source.UCSs.Add(new UCS("PARENT")); var child = source.UCSs.Add(new UCS("CHILD")); child.SetOrthographicBase(5, parent); child.SetOrthographicOrigin(UcsOrthographicType.Right, new Vector3(7, 8, 9));
        var copy = (UCS)child.Clone("COPY"); Check(ReferenceEquals(copy.BaseUcs, parent) && copy.OrthographicViewType == 5, "Detached clone retains actual base identity");
        var destination = new DxfDocument(DxfVersion.AutoCad2018); _ = destination.Objects; var sameName = destination.UCSs.Add(new UCS("PARENT")); long seed = OwnershipSeed(destination); int count = destination.UCSs.Count;
        Throws<ArgumentException>(() => destination.UCSs.Add(copy)); Equal(seed, OwnershipSeed(destination), "Foreign adoption preflights before handle allocation"); Equal(count, destination.UCSs.Count, "Foreign adoption keeps collection atomic"); Check(copy.Owner == null && copy.Handle == null && ReferenceEquals(copy.BaseUcs, parent), "Failed adoption preserves detached clone and source dependency");
        copy.SetOrthographicBase(copy.OrthographicViewType, sameName); destination.UCSs.Add(copy); Check(ReferenceEquals(copy.BaseUcs, sameName), "Explicit remap permits destination adoption"); Check(!destination.UCSs.Remove(sameName), "Mapped destination target protected");
        copy.SetOrthographicOrigin(UcsOrthographicType.Right, Vector3.Zero); Equal(new Vector3(7, 8, 9), child.OrthographicOrigins[UcsOrthographicType.Right], "Base relation clone does not share origin overrides");
        var ignored = (UCS)child.Clone("COPY"); Check(ReferenceEquals(destination.UCSs.Add(ignored), copy) && ignored.Owner == null, "Existing-name lookup does not adopt foreign candidate");
        Check(destination.UCSs.Remove(copy) && destination.UCSs.Remove(sameName), "Explicit clone teardown releases destination references"); Check(ReferenceEquals(child.BaseUcs, parent) && parent.Owner == source.UCSs, "Source identity unaffected");
    }
}
