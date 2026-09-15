using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static int Failed;
    private static string Output = "";
    private static readonly PropertyInfo HandleSeed = typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static long Seed(DxfDocument drawing) => (long)HandleSeed.GetValue(drawing)!;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run(string name, Action action)
    {
        try { action(); Results.Add(new { name, passed = true, error = "" }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { Failed++; Results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static byte[] Save(DxfDocument drawing, bool binary)
    { using var output = new MemoryStream(); Check(drawing.Save(output, binary), "Save returned false."); return output.ToArray(); }
    private static DxfDocument Reload(byte[] bytes)
    { using var input = new MemoryStream(bytes); return DxfDocument.Load(input)!; }
    private static Section NewSection(string name)
    {
        var section = new Section { Name = name, TopHeight = 7, BottomHeight = -3 };
        section.Vertices.Add(Vector3.Zero); section.Vertices.Add(new Vector3(2, 3, 4)); return section;
    }
    private static DxfDocument Drawing(DxfVersion version = DxfVersion.AutoCad2018)
    {
        var drawing = new DxfDocument(version); _ = drawing.Objects;
        drawing.Entities.Add(NewSection("FIRST")); drawing.Entities.Add(NewSection("SECOND")); return drawing;
    }
    private static string State(DxfDocument drawing) => Seed(drawing) + ":" + string.Join("/", drawing.Objects.Items.Select(o => o.Handle + "=" + o.Owner?.Handle)) + ":" + string.Join("/", drawing.Objects.Root.Entries.Select(e => e.Name + "=" + e.Target.Handle));
    private static void Refuse(DxfDocument drawing, Action action, bool equalState = true)
    {
        string before = State(drawing); bool rejected = false;
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid operation succeeded."); if (equalState) Check(State(drawing) == before, "Rejected operation changed registrations, anchors or seed.");
    }
    private static DxfClass Definition(int? count) => new("SECTION_MANAGER", "AcDbSectionManager", "ObjectDBX Classes") { ProxyFlags = 1024, InstanceCount = count };
    private static void Main(string[] args)
    {
        Output = Path.GetFullPath(args[0]); Directory.CreateDirectory(Output);
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            string name = version + "-" + binary;
            Run("canonical-create-erase-" + name, () =>
            {
                var drawing = Drawing(version); var root = drawing.Objects.Root; root.IsHardOwner = false; root.Cloning = DictionaryCloningFlags.UseClone;
                var members = drawing.Entities.All.OfType<Section>().ToArray(); long seed = Seed(drawing); var request = new List<Section> { members[1], members[0], members[1] };
                var manager = drawing.Objects.CreateSectionManager(request, true); request.Clear(); var previous = manager.Sections; var tags = manager.Tags;
                Check(Seed(drawing) == seed + 1 && manager.Handle == seed.ToString("X"), "Creation did not allocate exactly one expected identity.");
                Check(manager.SourceVersion == version && ReferenceEquals(manager.Owner, root) && manager.RequiresFullUpdate && manager.Sections.SequenceEqual(new[] { members[1], members[0], members[1] }), "Canonical manager changed profile/owner/ordered identities.");
                Check(manager.PersistentReactors.SequenceEqual(new[] { root }) && !root.Entries.Single(e => e.Name == "ACAD_SECTION_MANAGER").IsHardOwner && !root.IsHardOwner && root.Cloning == DictionaryCloningFlags.UseClone, "Canonical anchor/reactor changed root flags.");
                Check(!drawing.Entities.Remove(members[0]) && !drawing.Entities.Remove(members[1]), "Created manager does not protect section membership.");
                root.Add("INDEPENDENT_MANAGER_ALIAS", manager, true);
                byte[] saved = Save(drawing, binary); File.WriteAllBytes(Path.Combine(Output, "created-" + name + ".dxf"), saved);
                var loaded = Reload(saved); var next = loaded.Objects.Items.OfType<DxfStoredSectionManager>().Single();
                Check(next.Sections.Select(s => s.Handle).SequenceEqual(manager.Sections.Select(s => s.Handle)) && next.Handle == manager.Handle && next.PersistentReactors.Single() == loaded.Objects.Root, "Reload changed canonical manager graph.");
                string handle = manager.Handle; drawing.Objects.EraseSectionManager(manager);
                Check(manager.IsErased && manager.Owner == null && manager.Database == null && drawing.GetObjectByHandle(handle) == null && !root.Contains("ACAD_SECTION_MANAGER") && !root.Contains("INDEPENDENT_MANAGER_ALIAS"), "Erasure did not remove exact manager and aliases.");
                Check(previous.SequenceEqual(new[] { members[1], members[0], members[1] }) && ReferenceEquals(tags, manager.Tags), "Erasure rewrote retired manager snapshots.");
                Check(drawing.Entities.Remove(members[0]) && drawing.Entities.Remove(members[1]), "Erasure removed sections or retained their dependency guards.");
                Refuse(drawing, () => drawing.Objects.EraseSectionManager(manager));
                var replacement = drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false); Check(replacement.Handle != handle && !replacement.RequiresFullUpdate, "Explicit recreation reused erased identity or flag.");
                File.WriteAllBytes(Path.Combine(Output, "recreated-" + name + ".dxf"), Save(drawing, binary));
            });
        }
        foreach (int? count in new int?[] { null, 0, 91 }) Run("class-metadata-" + (count?.ToString() ?? "absent"), () =>
        {
            var drawing = Drawing(); var definition = Definition(count); drawing.Classes.Add(definition); var manager = drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false);
            Check(ReferenceEquals(definition, drawing.Classes[definition.Name]) && definition.ApplicationName == "ObjectDBX Classes" && definition.ProxyFlags == 1024 && !definition.WasProxy && !definition.IsEntity && definition.InstanceCount == (count.HasValue ? 1 : (int?)null), "Creation replaced CLASS declaration, count presence or metadata.");
            drawing.Objects.EraseSectionManager(manager);
            Check(ReferenceEquals(definition, drawing.Classes[definition.Name]) && definition.InstanceCount == (count.HasValue ? 0 : (int?)null), "Erasure changed count presence or declaration identity.");
        });
        Run("enumeration-and-dispose-atomicity", () =>
        {
            var drawing = Drawing(); var section = drawing.Entities.All.OfType<Section>().First();
            IEnumerable<Section> Throwing() { yield return section; throw new InvalidOperationException("Caller MoveNext failure."); }
            Refuse(drawing, () => drawing.Objects.CreateSectionManager(Throwing(), true));
            Refuse(drawing, () => drawing.Objects.CreateSectionManager(new CallbackEnumerable(section, null, () => throw new InvalidOperationException("Caller Dispose failure.")), false));
            Check(drawing.Objects.CreateSectionManager(new[] { section }, false).Sections.Single() == section, "Failed enumeration left creation locked.");
        });
        foreach (bool dispose in new[] { false, true }) Run("caught-reentry-" + dispose, () =>
        {
            var drawing = Drawing(); var section = drawing.Entities.All.OfType<Section>().First(); bool caught = false;
            void Nested() { try { drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false); } catch (InvalidOperationException) { caught = true; } }
            var input = new CallbackEnumerable(section, dispose ? null : Nested, dispose ? Nested : null);
            Refuse(drawing, () => drawing.Objects.CreateSectionManager(input, false)); Check(caught, "Recursive attempt did not reject.");
            _ = drawing.Objects.CreateSectionManager(new[] { section }, false);
        });
        Run("disposal-removes-requested-member", () =>
        {
            var drawing = Drawing(); var section = drawing.Entities.All.OfType<Section>().First(); long seed = Seed(drawing);
            Refuse(drawing, () => drawing.Objects.CreateSectionManager(new CallbackEnumerable(section, null, () => Check(drawing.Entities.Remove(section), "Caller could not remove section.")), false), false);
            Check(Seed(drawing) == seed && !drawing.Objects.Root.Contains("ACAD_SECTION_MANAGER") && section.Owner == null, "Creation allocated or reversed caller side effect.");
        });
        Run("invalid-members-and-size", () =>
        {
            var drawing = Drawing(); var section = drawing.Entities.All.OfType<Section>().First(); var foreign = Drawing(); var sameHandle = foreign.Entities.All.OfType<Section>().First(); Check(section.Handle == sameHandle.Handle, "Foreign same-handle fixture differs.");
            Refuse(drawing, () => drawing.Objects.CreateSectionManager(new[] { sameHandle }, false)); Refuse(drawing, () => drawing.Objects.CreateSectionManager(new[] { NewSection("DETACHED") }, false));
            Refuse(drawing, () => drawing.Objects.CreateSectionManager(new Section[] { null! }, false)); Refuse(drawing, () => drawing.Objects.CreateSectionManager(null!, false));
            Refuse(drawing, () => drawing.Objects.CreateSectionManager(Enumerable.Repeat(section, 65537), false));
            var manager = drawing.Objects.CreateSectionManager(Enumerable.Repeat(section, 65536), true); Check(manager.Sections.Count == 65536, "Exact membership limit rejected.");
        });
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004 }) Run("unsupported-profile-" + version, () =>
        { var drawing = new DxfDocument(version); _ = drawing.Objects; Refuse(drawing, () => drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false)); });
        Run("occupied-anchor-and-orphan-manager", () =>
        {
            var drawing = Drawing(); drawing.Objects.Root.Add("acad_section_manager", new DxfXRecord()); Refuse(drawing, () => drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false));
            drawing.Objects.Root.Remove("acad_section_manager"); var manager = drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false); drawing.Objects.Root.Remove("ACAD_SECTION_MANAGER");
            Refuse(drawing, () => drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false)); Refuse(drawing, () => drawing.Objects.EraseSectionManager(manager));
        });
        Run("conflicting-class", () =>
        { var drawing = Drawing(); var definition = Definition(0); definition.ProxyFlags = 1025; drawing.Classes.Add(definition); Refuse(drawing, () => drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false)); });
        Run("erase-incoming-metadata-and-owned-tree", () =>
        {
            var drawing = Drawing(); var block = new Block("INDEPENDENT_ATTRIBUTES", Array.Empty<EntityObject>(), new[] { new AttributeDefinition("TAG") }); var insert = new Insert(block); drawing.Entities.Add(insert);
            var manager = drawing.Objects.CreateSectionManager(Array.Empty<Section>(), true); var attribute = insert.Attributes.Single();
            var app = drawing.ApplicationRegistries.Add(new ApplicationRegistry("INDEPENDENT_INCOMING")); var xdata = new XData(app); xdata.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, manager.Handle)); attribute.XData.Add(xdata);
            Refuse(drawing, () => drawing.Objects.EraseSectionManager(manager)); attribute.XData.Remove(app.Name);
            var extension = new DxfDictionary(); var note = new DxfXRecord(); extension.Add("NOTE", note); drawing.Objects.SetExtensionDictionary(manager, extension);
            var childReference = new XData(app); childReference.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, note.Handle)); attribute.XData.Add(childReference);
            Refuse(drawing, () => drawing.Objects.EraseSectionManager(manager)); attribute.XData.Remove(app.Name);
            drawing.Objects.EraseSectionManager(manager); Check(extension.IsErased && note.IsErased && insert.Owner != null && attribute.Owner == insert, "Explicit erasure did not cascade only typed owned metadata.");
        });
        Run("metadata-handle-reservation", () =>
        {
            var drawing = Drawing(); var block = new Block("INDEPENDENT_RESERVATION", Array.Empty<EntityObject>(), new[] { new AttributeDefinition("TAG") }); var insert = new Insert(block); drawing.Entities.Add(insert);
            var attribute = insert.Attributes.Single(); Check(attribute.Handle != null && drawing.GetObjectByHandle(attribute.Handle) == null, "Retained ATTRIB fixture is unexpectedly in ordinary registration.");
            long originalSeed = Seed(drawing); HandleSeed.SetValue(drawing, Convert.ToInt64(attribute.Handle, 16)); var manager = drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false);
            Check(manager.Handle != attribute.Handle && ReferenceEquals(attribute.Owner, insert), "Creation reused a live owner-held metadata identity.");
            // This deliberately artificial seed probes only manager allocation. Restore the valid
            // global seed before unrelated legacy writer paths allocate their own generated objects.
            HandleSeed.SetValue(drawing, Math.Max(originalSeed, Seed(drawing)));
            var loaded = Reload(Save(drawing, false)); Check(loaded.Objects.Items.OfType<DxfStoredSectionManager>().Single().Handle == manager.Handle, "Reserved allocation did not reload.");
        });
        Run("handle-range-exhaustion", () =>
        { var drawing = Drawing(); HandleSeed.SetValue(drawing, long.MaxValue); Refuse(drawing, () => drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false)); });
        Run("erasure-source-and-reactor-guards", () =>
        {
            var drawing = Drawing(); var manager = drawing.Objects.CreateSectionManager(Array.Empty<Section>(), false); drawing.DrawingVariables.AcadVer = DxfVersion.AutoCad2013;
            Refuse(drawing, () => drawing.Objects.EraseSectionManager(manager)); drawing.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
            manager.PersistentReactors.Clear(); Refuse(drawing, () => drawing.Objects.EraseSectionManager(manager)); manager.PersistentReactors.Add(drawing.Objects.Root);
            var foreign = Drawing(); Refuse(foreign, () => foreign.Objects.EraseSectionManager(manager)); drawing.Objects.EraseSectionManager(manager);
        });
        if (args.Length > 1) NativeCases(Path.GetFullPath(args[1]));
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonSerializer.Serialize(new { cases = Results.Count, failed = Failed, librarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(), results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Results.Count} cases; {Failed} failed."); Environment.ExitCode = Failed == 0 ? 0 : 1;
    }
    private static void NativeCases(string repository)
    {
        byte[] bytes;
        using (var file = File.OpenRead(Path.Combine(repository, "tests/fixtures/section/LiveSection1.dxf.gz")))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var expanded = new MemoryStream()) { gzip.CopyTo(expanded); bytes = expanded.ToArray(); }
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository, "tests/fixtures/section/manifest.json")));
        Check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.RootElement.GetProperty("source_sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Native fixture hash differs.");
        foreach (bool inputBinary in new[] { false, true }) foreach (bool outputBinary in new[] { false, true }) Run($"native-erase-recreate-{inputBinary}-{outputBinary}", () =>
        {
            using var source = new MemoryStream(bytes); var raw = DxfRawDocument.Load(source); using var converted = new MemoryStream();
            raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(converted, inputBinary); var drawing = Reload(converted.ToArray());
            var manager = drawing.Objects.Items.OfType<DxfStoredSectionManager>().Single(); var oldMembers = manager.Sections; var oldTags = manager.Tags; var originalSections = drawing.Entities.All.OfType<Section>().ToArray();
            string handle = manager.Handle; long seed = Seed(drawing); drawing.Objects.EraseSectionManager(manager);
            Check(manager.IsErased && drawing.GetObjectByHandle(handle) == null && Seed(drawing) == seed && originalSections.SequenceEqual(drawing.Entities.All.OfType<Section>()), "Native erasure changed unrelated entities/seed.");
            var replacement = drawing.Objects.CreateSectionManager(oldMembers.Concat(oldMembers), true);
            Check(replacement.Handle != handle && replacement.Sections.SequenceEqual(oldMembers.Concat(oldMembers)) && ReferenceEquals(oldTags, manager.Tags), "Native recreation lost requested identities or retired snapshots.");
            byte[] saved = Save(drawing, outputBinary); File.WriteAllBytes(Path.Combine(Output, $"native-recreated-{inputBinary}-{outputBinary}.dxf"), saved);
            var loaded = Reload(saved); var next = loaded.Objects.Items.OfType<DxfStoredSectionManager>().Single();
            Check(next.Handle == replacement.Handle && next.RequiresFullUpdate && next.Sections.Select(s => s.Handle).SequenceEqual(replacement.Sections.Select(s => s.Handle)), "Native recreation failed exact graph reload.");
        });
    }
    private sealed class CallbackEnumerable : IEnumerable<Section>
    {
        private readonly Section section; private readonly Action? begin, dispose;
        internal CallbackEnumerable(Section section, Action? begin, Action? dispose) { this.section = section; this.begin = begin; this.dispose = dispose; }
        public IEnumerator<Section> GetEnumerator() { begin?.Invoke(); return new Cursor(section, dispose); } IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Cursor : IEnumerator<Section>
        {
            private bool moved; private readonly Action? dispose; internal Cursor(Section section, Action? dispose) { Current = section; this.dispose = dispose; }
            public Section Current { get; } object IEnumerator.Current => Current; public bool MoveNext() { if (moved) return false; moved = true; return true; }
            public void Reset() => throw new NotSupportedException(); public void Dispose() => dispose?.Invoke();
        }
    }
}
