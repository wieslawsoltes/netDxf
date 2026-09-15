using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] ViewUcsCodes = { 72, 110, 120, 130, 111, 121, 131, 112, 122, 132, 79, 146, 345, 346 };
    private static void RegisterViewUcsTests()
    {
        Run("view-ucs/model-and-lifecycle", ViewUcsLifecycle);
        Run("view-ucs/clone-and-document-isolation", ViewUcsCloneIsolation);
        Run("view-ucs/invalid-values", ViewUcsInvalidValues);
        Run("view-ucs/foreign-registry/ucs", () => ViewUcsForeignRegistry(true));
        Run("view-ucs/foreign-registry/view", () => ViewUcsForeignRegistry(false));
        foreach (bool ucs in new[] { false, true })
        foreach (string scenario in new[] { "throw", "attach", "detach", "move", "collision", "attach-throw", "move-collision" })
        {
            bool u = ucs; string s = scenario;
            Run($"view-ucs/rename/{u}/{s}", () => ViewUcsRename(u, s));
        }
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            Run($"view-ucs/independent/{v}/{b}", () => ViewUcsIndependent(v, b));
            Run($"view-ucs/reordered/{v}/{b}", () => ViewUcsReordered(v, b));
            Run($"view-ucs/absence-and-authoring/{v}/{b}", () => ViewUcsAuthoring(v, b));
            foreach (string defect in new[] { "missing-enable", "disabled", "bad-enable", "duplicate-enable", "zero-axis",
                "missing-110", "missing-120", "missing-130", "missing-111", "missing-121", "missing-131", "missing-112", "missing-122", "missing-132",
                "bad-ortho", "base-without-ortho", "missing-named-target", "missing-base-target", "wrong-target-type", "duplicate-reference",
                "ucs-unsupported-type", "ucs-duplicate-flags", "vport-missing-target", "vport-wrong-target-type", "vport-base-without-ortho" })
            {
                string d = defect;
                Run($"view-ucs/invalid/{v}/{b}/{d}", () => ViewUcsMalformed(v, b, d));
            }
        }
    }

    private static string ViewUcsFixture(DxfVersion version) => Path.Combine("tests", "fixtures", "view-ucs", $"independent-view-ucs-R{version.ToString().Replace("AutoCad", "")}.dxf");
    private static DxfRawDocument ViewUcsRaw(DxfVersion version)
    {
        string path = ViewUcsFixture(version);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine("tests", "fixtures", "view-ucs", "manifest.json")));
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("path").GetString() == Path.GetFileName(path));
        byte[] bytes = File.ReadAllBytes(path);
        Equal(fixture.GetProperty("sha256").GetString()!, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "Independent fixture provenance");
        using var input = new MemoryStream(bytes); return DxfRawDocument.Load(input);
    }
    private static MemoryStream ViewUcsInput(DxfRawDocument raw, bool binary)
    {
        var result = new MemoryStream(); raw.Save(result, binary); result.Position = 0; return result;
    }
    private static DxfDocument ViewUcsLoad(DxfRawDocument raw, bool binary)
    {
        using var input = ViewUcsInput(raw, binary);
        return DxfDocument.Load(input) ?? throw new InvalidOperationException("UCS fixture failed to load.");
    }
    private static DxfRawRecord ViewUcsRecord(DxfRawDocument raw, string type, string name)
    {
        return raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == type && r.Tags.Any(t => t.Code == 2 && (string)t.Value == name));
    }
    private static DxfRawDocument EditViewUcsRecord(DxfRawDocument raw, string type, string name, Action<List<DxfTag>> edit)
    {
        var record = ViewUcsRecord(raw, type, name);
        var tags = record.Tags.ToList(); edit(tags);
        return raw.WithTags(raw.Tags.Take(record.StartTagIndex).Concat(tags).Concat(raw.Tags.Skip(record.EndTagIndex)));
    }
    private static void PutViewUcsTag(List<DxfTag> tags, short code, object value)
    {
        int i = tags.FindIndex(t => t.Code == code);
        if (i >= 0) tags[i] = new DxfTag(code, value);
        else tags.Insert(tags.FindIndex(t => t.Code == 1001), new DxfTag(code, value));
    }
    private static void CheckViewUcsModel(DxfDocument doc)
    {
        var named = doc.UCSs["NamedFrame"]; var baseUcs = doc.UCSs["BaseFrame"];
        Equal(UcsFlags.Referenced, named.Flags, "UCS flags lost");
        Equal(new Vector3(10.125, -20.25, 30.5), named.Origin, "Named UCS origin");
        Equal(new Vector3(-3.5, 4.25, 8), baseUcs.Origin, "Base UCS origin");
        foreach (string name in new[] { "NamedReview", "UcsReview" })
        {
            View view = doc.Views[name]; ViewUcs ucs = view.Ucs ?? throw new Exception("Associated UCS was discarded.");
            Equal(new Vector3(5.125, -6.25, 7.5), ucs.Origin, "VIEW UCS origin");
            Equal(new Vector3(0, 2, 0), ucs.XAxis, "VIEW UCS X direction magnitude");
            Equal(new Vector3(-3, 0, 0), ucs.YAxis, "VIEW UCS Y direction magnitude");
            Equal(-9.875, ucs.Elevation, "VIEW UCS elevation");
            Equal(new Vector3(2, -3, 7), view.ViewDirection, "Primary VIEW direction changed");
            bool isNamed = name == "NamedReview";
            Check(ReferenceEquals(ucs.NamedUcs, isNamed ? named : null) && ReferenceEquals(ucs.BaseUcs, isNamed ? null : baseUcs), "VIEW references lost their registered identities.");
            Equal((short)(isNamed ? 0 : 5), ucs.OrthographicType, "VIEW orthographic type");
            Equal("independent", (string)view.XData["UCS_QA"].XDataRecord[0].Value, "VIEW XData");
        }
        Check(ReferenceEquals(doc.Viewport.NamedUcs, named) && doc.Viewport.BaseUcs == null, "Active VPORT named reference");
        Check(ReferenceEquals(doc.VPorts["OrthoConfig"].BaseUcs, baseUcs) && doc.VPorts["OrthoConfig"].NamedUcs == null, "VPORT base reference");
        Equal((short)4, doc.VPorts["OrthoConfig"].UcsOrthographicType, "VPORT orthographic type");
        Check(named.HasReferences() && baseUcs.HasReferences(), "Loaded hard-pointer references were not tracked.");
        Equal(2, named.GetReferences().Sum(r => r.Uses), "Named reference uses");
        Equal(2, baseUcs.GetReferences().Sum(r => r.Uses), "Base reference uses");
        var line = doc.Entities.Lines.Single();
        Equal(new Vector3(1, 2, 3), line.StartPoint, "Following LINE start");
        Equal(new Vector3(4, 5, 6), line.EndPoint, "Following LINE end");
    }
    private static void ViewUcsIndependent(DxfVersion version, bool binary)
    {
        var raw = ViewUcsRaw(version); var doc = ViewUcsLoad(raw, binary); CheckViewUcsModel(doc);
        var identities = new[] { doc.UCSs["NamedFrame"].Handle, doc.UCSs["BaseFrame"].Handle, doc.Views["NamedReview"].Handle, doc.Views["UcsReview"].Handle };
        for (int cycle = 0; cycle < 3; cycle++)
        {
            bool transport = cycle % 2 == 0 ? binary : !binary;
            using var output = new MemoryStream(); Check(doc.Save(output, transport), "VIEW UCS save failed.");
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"view-ucs-{version}-{(binary ? "binary" : "text")}.dxf"), output.ToArray());
            output.Position = 0; var tags = DxfRawDocument.Load(output);
            var namedTags = ViewUcsRecord(tags, "VIEW", "NamedReview").Tags;
            Equal(identities[0], (string)namedTags.Single(t => t.Code == 345).Value, "Named pointer handle changed");
            Check(!namedTags.Any(t => t.Code == 346), "Absent base pointer was invented.");
            var baseTags = ViewUcsRecord(tags, "VIEW", "UcsReview").Tags;
            Equal(identities[1], (string)baseTags.Single(t => t.Code == 346).Value, "Base pointer handle changed");
            Check(!baseTags.Any(t => t.Code == 345), "Absent named pointer was invented.");
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new Exception("VIEW UCS reload failed."); CheckViewUcsModel(doc);
            Check(identities.SequenceEqual(new[] { doc.UCSs["NamedFrame"].Handle, doc.UCSs["BaseFrame"].Handle, doc.Views["NamedReview"].Handle, doc.Views["UcsReview"].Handle }), "UCS/VIEW identity changed across transports.");
        }
    }
    private static void ViewUcsReordered(DxfVersion version, bool binary)
    {
        var raw = ViewUcsRaw(version);
        foreach (string name in new[] { "NamedReview", "UcsReview" })
            raw = EditViewUcsRecord(raw, "VIEW", name, tags =>
            {
                var fields = tags.Where(t => ViewUcsCodes.Contains(t.Code)).OrderByDescending(t => t.Code).ToArray();
                tags.RemoveAll(t => ViewUcsCodes.Contains(t.Code));
                tags.InsertRange(tags.FindIndex(t => t.Code == 1001), fields);
            });
        CheckViewUcsModel(ViewUcsLoad(raw, binary));
    }
    private static void ViewUcsMalformed(DxfVersion version, bool binary, string defect)
    {
        var raw = ViewUcsRaw(version);
        string type = defect.StartsWith("ucs-") ? "UCS" : defect.StartsWith("vport-") ? "VPORT" : "VIEW";
        string name = type == "UCS" ? "NamedFrame" : type == "VPORT" ? "OrthoConfig" : defect is "base-without-ortho" or "missing-base-target" ? "UcsReview" : "NamedReview";
        string line = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "LINE").Tags.Single(t => t.Code == 5).Value.ToString()!;
        raw = EditViewUcsRecord(raw, type, name, tags =>
        {
            if (defect.StartsWith("missing-") && short.TryParse(defect.Substring(8), out short missing)) { tags.RemoveAll(t => t.Code == missing); return; }
            switch (defect)
            {
                case "missing-enable": tags.RemoveAll(t => t.Code == 72); break;
                case "disabled": PutViewUcsTag(tags, 72, (short)0); break;
                case "bad-enable": PutViewUcsTag(tags, 72, (short)2); break;
                case "duplicate-enable": tags.Insert(tags.FindIndex(t => t.Code == 72), new DxfTag(72, (short)1)); break;
                case "zero-axis": PutViewUcsTag(tags, 121, 0.0); break;
                case "bad-ortho": PutViewUcsTag(tags, 79, (short)7); break;
                case "base-without-ortho": case "vport-base-without-ortho": PutViewUcsTag(tags, 79, (short)0); break;
                case "missing-named-target": PutViewUcsTag(tags, 345, "FFFF"); break;
                case "missing-base-target": case "vport-missing-target": PutViewUcsTag(tags, 346, "FFFF"); break;
                case "wrong-target-type": PutViewUcsTag(tags, 345, line); break;
                case "vport-wrong-target-type": PutViewUcsTag(tags, 346, line); break;
                case "duplicate-reference": tags.Insert(tags.FindIndex(t => t.Code == 345), new DxfTag(345, "FFFF")); break;
                case "ucs-unsupported-type": PutViewUcsTag(tags, 79, (short)7); break;
                case "ucs-duplicate-flags": tags.Insert(tags.FindIndex(t => t.Code == 70), new DxfTag(70, (short)0)); break;
                default: throw new ArgumentException(defect);
            }
        });
        using var input = ViewUcsInput(raw, binary);
#if DEBUG
        Throws<Exception>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Invalid associated UCS input was admitted: " + defect);
#endif
        Check(input.CanRead, "Rejected UCS input closed caller stream.");
    }
    private static void ViewUcsAuthoring(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var target = doc.UCSs.Add(new UCS("Authored") { Flags = UcsFlags.XrefResolved | UcsFlags.ExternallyDependent });
        var plain = doc.Views.Add(new View("Plain"));
        var associated = doc.Views.Add(new View("Associated") { Ucs = new ViewUcs { OrthographicType = 2, Origin = new Vector3(1.125, -2.5, 3.75), BaseUcs = target } });
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Authored UCS did not save."); output.Position = 0;
        var raw = DxfRawDocument.Load(output); var tags = ViewUcsRecord(raw, "VIEW", plain.Name).Tags;
        Equal((short)0, (short)tags.Single(t => t.Code == 72).Value, "Disabled UCS flag");
        Check(!tags.Any(t => t.Code != 72 && ViewUcsCodes.Contains(t.Code)), "Plain view received a UCS bundle.");
        output.Position = 0; var loaded = DxfDocument.Load(output)!;
        Check(loaded.Views[plain.Name].Ucs == null, "Absent bundle was invented.");
        Equal(target.Flags, loaded.UCSs[target.Name].Flags, "UCS flag edit was lost");
        Equal(associated.Ucs.Origin, loaded.Views[associated.Name].Ucs.Origin, "Authored associated origin changed");
        associated.Ucs.BaseUcs = null; associated.Ucs.OrthographicType = 3;
        using var world = new MemoryStream(); Check(doc.Save(world, binary), "WORLD base did not save."); world.Position = 0;
        Check(!ViewUcsRecord(DxfRawDocument.Load(world), "VIEW", associated.Name).Tags.Any(t => t.Code == 346), "WORLD base was assigned a fabricated handle.");
    }
    private static void ViewUcsLifecycle()
    {
        var doc = new DxfDocument(); UCS target = doc.UCSs.Add(new UCS("Target"));
        var view = doc.Views.Add(new View("First") { Ucs = new ViewUcs { NamedUcs = target, BaseUcs = target, OrthographicType = 1 } });
        var port = doc.VPorts.AddRecord(new VPort("Tiles") { NamedUcs = target });
        var tile = doc.VPorts.AddRecord((VPort)port.Clone());
        Equal(4, target.GetReferences().Sum(r => r.Uses), "Distinct reference slots were collapsed");
        Equal(3, target.GetReferences().Count, "Physical tiles were collapsed by configuration name");
        Check(!doc.UCSs.Remove(target), "Referenced UCS was removed.");
        target.Name = "RenamedTarget"; view.Name = "RenamedView";
        view.Ucs.NamedUcs = null;
        Equal(3, target.GetReferences().Sum(r => r.Uses), "Rename broke outbound reference removal");
        ViewUcs detached = view.Ucs; view.Ucs = null; detached.NamedUcs = target;
        Equal(2, target.GetReferences().Sum(r => r.Uses), "Detached bundle retained a reference subscription");
        Check(doc.VPorts.Remove(port), "First tile did not remove.");
        Equal(1, target.GetReferences().Sum(r => r.Uses), "Tile removal erased sibling reference");
        Check(doc.VPorts.Remove(tile) && doc.UCSs.Remove(target), "Last reference did not release UCS.");
        var one = new View("One") { Ucs = new ViewUcs() }; var two = new View("Two");
        Throws<ArgumentException>(() => two.Ucs = one.Ucs);
        Check(two.Ucs == null && one.Ucs != null, "Rejected bundle attachment changed ownership.");
    }
    private static void ViewUcsCloneIsolation()
    {
        var source = new DxfDocument(); UCS original = source.UCSs.Add(new UCS("Source"));
        View view = source.Views.Add(new View("View") { Ucs = new ViewUcs { NamedUcs = original } });
        var copy = (View)view.Clone("Copy"); Check(!ReferenceEquals(copy.Ucs, view.Ucs), "Clone shared mutable UCS values.");
        Check(ReferenceEquals(copy.Ucs.NamedUcs, original), "Detached clone lost its reference identity.");
        copy.Ucs.Origin = Vector3.UnitX; Equal(Vector3.Zero, view.Ucs.Origin, "Clone values mutated source");
        var destination = new DxfDocument();
        Throws<ArgumentException>(() => destination.Views.Add(copy));
        Check(copy.Owner == null && copy.Handle == null && destination.Views.Count == 0, "Rejected cross-document add partially registered clone.");
        UCS mapped = destination.UCSs.Add((UCS)original.Clone()); copy.Ucs.NamedUcs = mapped; destination.Views.Add(copy);
        var foreign = destination.UCSs.Add(new UCS("Foreign")); string handle = original.Handle;
        Throws<ArgumentException>(() => view.Ucs.NamedUcs = foreign);
        Check(ReferenceEquals(view.Ucs.NamedUcs, original) && original.Handle == handle && original.Owner == source.UCSs, "Rejected foreign assignment changed source identity.");
        Check(source.Views.Remove(view) && !original.HasReferences(), "View removal left UCS references.");
    }
    private static void ViewUcsInvalidValues()
    {
        var value = new ViewUcs();
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => value.Elevation = invalid);
            Throws<ArgumentOutOfRangeException>(() => value.Origin = new Vector3(invalid, 0, 0));
        }
        Throws<ArgumentException>(() => value.XAxis = Vector3.Zero);
        Throws<ArgumentException>(() => value.YAxis = Vector3.Zero);
        Throws<ArgumentOutOfRangeException>(() => value.OrthographicType = 7);
        var doc = new DxfDocument(); var target = doc.UCSs.Add(new UCS("Target"));
        var view = doc.Views.Add(new View("View") { Ucs = new ViewUcs() }); view.Ucs.BaseUcs = target;
        using var output = new MemoryStream();
#if DEBUG
        Throws<InvalidOperationException>(() => doc.Save(output));
#else
        Check(!doc.Save(output), "Invalid base relationship saved successfully.");
#endif
        Equal(0L, output.Length, "Invalid UCS relationship wrote partial output");
    }

    private static void ViewUcsRename(bool isUcs, string scenario)
    {
        var source = new DxfDocument(); var destination = new DxfDocument();
        UCS sourceTarget = source.UCSs.Add(new UCS("Referenced"));
        UCS destinationTarget = destination.UCSs.Add(new UCS("Referenced"));
        TableObject record = isUcs ? new UCS("Before") : new View("Before") { Ucs = new ViewUcs { NamedUcs = sourceTarget } };
        void Add(DxfDocument doc) { if (isUcs) doc.UCSs.Add((UCS)record); else doc.Views.Add((View)record); }
        void Remove(DxfDocument doc) { Check(isUcs ? doc.UCSs.Remove((UCS)record) : doc.Views.Remove((View)record), "Rename observer could not detach record."); }
        TableObject? Get(DxfDocument doc, string name) => isUcs ? doc.UCSs[name] : doc.Views[name];
        void Collision(DxfDocument doc) { if (isUcs) doc.UCSs.Add(new UCS("After")); else doc.Views.Add(new View("After")); }
        if (!scenario.StartsWith("attach")) Add(source);
        View? dependent = null;
        if (isUcs && scenario is "throw" or "collision")
            dependent = source.Views.Add(new View("Dependent") { Ucs = new ViewUcs { NamedUcs = (UCS)record } });
        if (scenario == "move-collision") Collision(destination);
        record.NameChanged += (_, _) =>
        {
            if (scenario.StartsWith("attach")) Add(source);
            if (scenario is "detach" or "move" or "move-collision") Remove(source);
            if (scenario is "move" or "move-collision")
            {
                if (!isUcs) ((View)record).Ucs.NamedUcs = destinationTarget;
                Add(destination);
            }
            if (scenario == "collision") Collision(source);
            if (scenario is "throw" or "attach-throw") throw new InvalidOperationException("Rejected by user observer.");
        };
        if (scenario is "throw" or "attach-throw") Throws<InvalidOperationException>(() => record.Name = "After");
        else if (scenario is "collision" or "move-collision") Throws<ArgumentException>(() => record.Name = "After");
        else record.Name = "After";
        bool failed = scenario is "throw" or "attach-throw" or "collision" or "move-collision";
        Equal(failed ? "Before" : "After", record.Name, "Rename transaction changed the actual name incorrectly");
        DxfDocument final = scenario.StartsWith("move") ? destination : source;
        if (scenario == "detach") Check(record.Owner == null && Get(source, "Before") == null && Get(source, "After") == null, "Detached rename left a collection index.");
        else
        {
            Check(ReferenceEquals(Get(final, record.Name), record), "Rename committed to the wrong owner or index.");
            if (scenario.StartsWith("move")) Check(Get(source, "Before") == null && Get(source, "After") == null, "Moving during rename retained the source index.");
        }
        if (dependent != null)
        {
            Equal(1, ((UCS)record).GetReferences().Sum(r => r.Uses), "Rejected rename changed incoming uses");
            dependent.Ucs.NamedUcs = null;
            Check(!((UCS)record).HasReferences(), "Incoming reference could not be removed after rejected rename.");
        }
        if (!isUcs)
        {
            ((View)record).Ucs.NamedUcs = null;
            Check(!sourceTarget.HasReferences() && !destinationTarget.HasReferences(), "Rename or owner transfer corrupted outbound reference uses.");
        }
    }

    private static void ViewUcsForeignRegistry(bool isUcs)
    {
        var source = new DxfDocument(); var destination = new DxfDocument();
        var registry = source.ApplicationRegistries.Add(new ApplicationRegistry("FOREIGN_UCS_DATA"));
        string handle = registry.Handle;
        TableObject record = isUcs ? new UCS("ForeignData") : new View("ForeignData");
        var data = new XData(registry); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "retained")); record.XData.Add(data);
        Throws<ArgumentException>(() => { if (isUcs) destination.UCSs.Add((UCS)record); else destination.Views.Add((View)record); });
        Check(record.Owner == null && record.Handle == null, "Foreign registry rejection partially registered the table record.");
        Check(!destination.ApplicationRegistries.Contains(registry.Name), "Foreign registry rejection changed the destination registry table.");
        Check(registry.Owner == source.ApplicationRegistries && registry.Handle == handle && ReferenceEquals(source.GetObjectByHandle(handle), registry), "Foreign registry rejection changed source identity.");
        TableObject clone = (TableObject)record.Clone();
        if (isUcs) destination.UCSs.Add((UCS)clone); else destination.Views.Add((View)clone);
        Check(!ReferenceEquals(clone.XData[registry.Name].ApplicationRegistry, registry), "Clone reused the foreign registry.");
        Check(registry.Owner == source.ApplicationRegistries && registry.Handle == handle, "Adding a clone mutated the source registry.");
        Check(!(isUcs ? destination.UCSs.Remove((UCS)record) : destination.Views.Remove((View)record)), "A detached same-name record removed an owned record.");
        UCS referenced;
        if (isUcs)
        {
            referenced = source.UCSs.Add((UCS)record);
            source.Views.Add(new View("Dependent") { Ucs = new ViewUcs { NamedUcs = referenced } });
        }
        else
        {
            referenced = source.UCSs.Add(new UCS("ReferencedFrame"));
            ((View)record).Ucs = new ViewUcs { NamedUcs = referenced };
            source.Views.Add((View)record);
        }
        string recordHandle = record.Handle ?? throw new Exception("The source table record was not registered.");
        Check(!(isUcs ? destination.UCSs.Remove((UCS)record) : destination.Views.Remove((View)record)), "A foreign same-name record reached removal.");
        Check(ReferenceEquals(source.GetObjectByHandle(recordHandle), record) && ReferenceEquals(destination.GetObjectByHandle(clone.Handle), clone), "Rejected removal changed either document's identity map.");
        Equal(1, referenced.GetReferences().Sum(r => r.Uses), "Rejected removal changed foreign UCS reference uses");
    }
}
