using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static int Failed;
    private static string Output = "";
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Run(string name, Action action)
    {
        try { action(); Results.Add(new { name, passed = true, error = "" }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { Failed++; Results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static long Seed(DxfDocument document) => Convert.ToInt64(typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document));
    private static byte[] Save(DxfDocument document, bool binary)
    { using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Save returned false."); return stream.ToArray(); }
    private static DxfDocument Load(DxfRawDocument raw, bool binary)
    {
        using var stream = new MemoryStream(); raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(stream, binary); stream.Position = 0;
        return DxfDocument.Load(stream) ?? throw new FormatException("Input was rejected.");
    }
    private static DxfStoredSectionManager Manager(DxfDocument document) => document.Objects.Items.OfType<DxfStoredSectionManager>().Single();
    private static Section NewSection(string name)
    {
        var section = new Section { Name = name, TopHeight = 7, BottomHeight = -3 };
        section.Vertices.Add(Vector3.Zero); section.Vertices.Add(new Vector3(2, 3, 4)); return section;
    }
    private static DxfDocument Fixture(DxfVersion version, string spelling, bool binary)
    {
        var document = new DxfDocument(version); var placeholder = new DxfXRecord(); document.Objects.Root.Add("ACAD_SECTION_MANAGER", placeholder, false);
        var first = NewSection("Independent first"); var second = NewSection("Independent second"); document.Entities.Add(first); document.Entities.Add(second);
        using var stream = new MemoryStream(Save(document, false)); var raw = DxfRawDocument.Load(stream);
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Tags.Any(t => t.Code == 5 && Equals(t.Value, placeholder.Handle)));
        var header = record.Tags.TakeWhile(t => t.Code != 100).Select(t => t.Code == 0 ? new DxfTag(0, spelling) : t);
        return Load(raw.WithRecord(record, header.Concat(new[] { new DxfTag(100, "AcDbSectionManager"), new DxfTag(70, (short)1), new DxfTag(90, 3), new DxfTag(330, first.Handle), new DxfTag(330, second.Handle), new DxfTag(330, first.Handle) })), binary);
    }
    private static void RejectUnchanged(DxfDocument document, Action action)
    {
        var manager = Manager(document); var sections = manager.Sections; var tags = manager.Tags; bool flag = manager.RequiresFullUpdate; long seed = Seed(document);
        bool rejected = false; try { action(); } catch (Exception error) when (error is InvalidOperationException or ArgumentException) { rejected = true; }
        Check(rejected, "Invalid replacement succeeded."); Check(ReferenceEquals(tags, manager.Tags) && sections.SequenceEqual(manager.Sections) && flag == manager.RequiresFullUpdate && seed == Seed(document), "Rejected replacement changed manager or seed.");
    }
    private static void Main(string[] args)
    {
        Output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Output);
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (string spelling in new[] { "SECTION_MANAGER", "SECTIONMANAGER" })
        foreach (bool binary in new[] { false, true })
        {
            string prefix = version + "-" + spelling + "-" + binary;
            Run(prefix + "-edit-snapshots-dependencies", () =>
            {
                var document = Fixture(version, spelling, binary); var manager = Manager(document); var old = manager.Sections; var oldTags = manager.Tags;
                var first = old[0]; var second = old[1]; var third = NewSection("Independent third"); document.Entities.Add(third);
                long seed = Seed(document); string handle = manager.Handle; var owner = manager.Owner;
                var requested = new List<Section> { third, second, third }; manager.ReplaceSections(requested, false); requested.Clear();
                Check(manager.Sections.SequenceEqual(new[] { third, second, third }) && !manager.RequiresFullUpdate, "Replacement borrowed caller list or changed order.");
                Check(old.SequenceEqual(new[] { first, second, first }) && Equals(oldTags[1].Value, (short)1), "Old snapshots changed.");
                Check(Seed(document) == seed && manager.Handle == handle && ReferenceEquals(manager.Owner, owner), "Replacement changed identity or allocation.");
                Check(((IList)manager.Tags).IsReadOnly && ((IList)manager.Sections).IsReadOnly, "Snapshots are writable.");
                Check(document.Entities.Remove(first), "Released target remains referenced."); Check(!document.Entities.Remove(second) && !document.Entities.Remove(third), "Replacement targets are removable.");
                var data = Save(document, binary); File.WriteAllBytes(Path.Combine(Output, prefix + ".dxf"), data);
                using var stream = new MemoryStream(data); var reloaded = Manager(DxfDocument.Load(stream)!);
                Check(reloaded.Sections.Select(s => s.Handle).SequenceEqual(new[] { third.Handle, second.Handle, third.Handle }) && ReferenceEquals(reloaded.Sections[0], reloaded.Sections[2]) && reloaded.CodeName == spelling, "Saved replacement lost order, exact identity, repetitions or spelling.");
                manager.ReplaceSections(Array.Empty<Section>(), true); Check(manager.RequiresFullUpdate && manager.Tags.Count == 3, "Empty independent flag combination changed.");
                Check(document.Entities.Remove(second) && document.Entities.Remove(third), "Empty replacement retained old references."); _ = Save(document, binary);
            });
            Run(prefix + "-enumeration-atomic", () =>
            {
                var document = Fixture(version, spelling, binary); var manager = Manager(document); var first = manager.Sections[0];
                IEnumerable<Section> Throwing() { yield return first; throw new InvalidOperationException("Caller iterator failed."); }
                RejectUnchanged(document, () => manager.ReplaceSections(Throwing(), false));
                RejectUnchanged(document, () => manager.ReplaceSections(new DisposeFailure(first), false));
                manager.ReplaceSections(new[] { first }, false); Check(manager.Sections.Count == 1, "Failed enumeration left edit locked.");
            });
            Run(prefix + "-caught-and-uncaught-reentry", () =>
            {
                var document = Fixture(version, spelling, binary); var manager = Manager(document); var first = manager.Sections[0]; var second = manager.Sections[1];
                IEnumerable<Section> Uncaught() { yield return second; manager.ReplaceSections(Array.Empty<Section>(), false); }
                RejectUnchanged(document, () => manager.ReplaceSections(Uncaught(), false));
                bool caught = false; IEnumerable<Section> Caught() { yield return second; try { manager.ReplaceSections(Array.Empty<Section>(), true); } catch (InvalidOperationException) { caught = true; } yield return first; }
                manager.ReplaceSections(Caught(), false); Check(caught && manager.Sections.SequenceEqual(new[] { second, first }) && !manager.RequiresFullUpdate, "Caught nested rejection prevented valid outer commit.");
            });
            Run(prefix + "-actual-source-identities", () =>
            {
                var document = Fixture(version, spelling, binary); var manager = Manager(document); var first = manager.Sections[0]; var foreign = Fixture(version, spelling, binary);
                Check(Manager(foreign).Sections[0].Handle == first.Handle, "Same-handle foreign fixture changed.");
                RejectUnchanged(document, () => manager.ReplaceSections(new[] { Manager(foreign).Sections[0] }, false));
                RejectUnchanged(document, () => manager.ReplaceSections(new[] { first, null! }, false));
                RejectUnchanged(document, () => manager.ReplaceSections(new[] { NewSection("Detached") }, false));
                RejectUnchanged(document, () => manager.ReplaceSections(Enumerable.Repeat(first, 65537), false));
            });
            Run(prefix + "-source-lifecycle", () =>
            {
                var document = Fixture(version, spelling, binary); var manager = Manager(document); var first = manager.Sections[0];
                document.DrawingVariables.AcadVer = version == DxfVersion.AutoCad2013 ? DxfVersion.AutoCad2018 : DxfVersion.AutoCad2013;
                RejectUnchanged(document, () => manager.ReplaceSections(new[] { first }, false)); document.DrawingVariables.AcadVer = version;
                Check(document.Objects.Root.Remove("ACAD_SECTION_MANAGER"), "Source root entry could not be removed for control.");
                RejectUnchanged(document, () => manager.ReplaceSections(new[] { first }, false));
            });
        }
        byte[] native; using (var file = File.OpenRead(Path.Combine(args[0], "tests/fixtures/section/LiveSection1.dxf.gz")))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress)) using (var stream = new MemoryStream()) { gzip.CopyTo(stream); native = stream.ToArray(); }
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(args[0], "tests/fixtures/section/manifest.json")));
        Check(Convert.ToHexString(SHA256.HashData(native)).ToLowerInvariant() == manifest.RootElement.GetProperty("source_sha256").GetString(), "Native fixture hash changed.");
        foreach (bool input in new[] { false, true }) foreach (bool output in new[] { false, true }) Run($"native-{input}-{output}", () =>
        {
            using var source = new MemoryStream(native); var document = Load(DxfRawDocument.Load(source), input); var manager = Manager(document); var section = manager.Sections.Single();
            var tags = manager.Tags; var reactors = manager.PersistentReactors.ToArray(); long seed = Seed(document);
            manager.ReplaceSections(new[] { section, section }, true);
            Check(tags.Count == 4 && Seed(document) == seed && manager.Handle == "229" && section.Handle == "228" && manager.PersistentReactors.SequenceEqual(reactors), "Native edit changed original packet snapshot or common identity.");
            byte[] saved = Save(document, output); File.WriteAllBytes(Path.Combine(Output, $"native-{input}-{output}.dxf"), saved);
            using var stream = new MemoryStream(saved); var reloaded = Manager(DxfDocument.Load(stream)!);
            Check(reloaded.RequiresFullUpdate && reloaded.Sections.Count == 2 && ReferenceEquals(reloaded.Sections[0], reloaded.Sections[1]), "Native edited packet lost repeated exact identity.");
        });
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonSerializer.Serialize(new { cases = Results.Count, failed = Failed, librarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(), results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Results.Count} cases; {Failed} failed."); Environment.ExitCode = Failed == 0 ? 0 : 1;
    }
    private sealed class DisposeFailure : IEnumerable<Section>
    {
        private readonly Section section; internal DisposeFailure(Section section) { this.section = section; }
        public IEnumerator<Section> GetEnumerator() => new Cursor(section); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Cursor : IEnumerator<Section>
        {
            private bool moved; internal Cursor(Section section) { Current = section; }
            public Section Current { get; } object IEnumerator.Current => Current;
            public bool MoveNext() { if (moved) return false; moved = true; return true; }
            public void Reset() => throw new NotSupportedException(); public void Dispose() => throw new InvalidOperationException("Caller disposal failed.");
        }
    }
}
