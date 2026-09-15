using System.Collections;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;

var results = new List<object>(); bool expectFixed = args.Length > 1 && args[1] == "fixed"; int failed = 0;
foreach (string kind in new[] { "requested-section-reactor", "source-root-reactor", "unrelated-line-reactor" })
{
    var drawing = new DxfDocument(DxfVersion.AutoCad2018); _ = drawing.Objects;
    var section = new Section { Name = "REQUESTED" }; section.Vertices.Add(Vector3.Zero); section.Vertices.Add(Vector3.UnitX); drawing.Entities.Add(section);
    var line = new Line(Vector3.Zero, Vector3.UnitX); drawing.Entities.Add(line);
    var definition = new DxfClass("SECTION_MANAGER", "AcDbSectionManager", "ObjectDBX Classes") { ProxyFlags = 1024, InstanceCount = 0 }; drawing.Classes.Add(definition);
    var seed = typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!;
    long before = (long)seed.GetValue(drawing)!;
    Action mutate = () => (kind == "requested-section-reactor" ? section.PersistentReactors : kind == "source-root-reactor" ? drawing.Objects.Root.PersistentReactors : line.PersistentReactors).Add(new DxfXRecord());
    bool created = false; string creationError = "";
    try { _ = drawing.Objects.CreateSectionManager(new DisposeCallback(section, mutate), false); created = true; } catch (Exception error) { creationError = error.GetType().Name + ": " + error.Message; }
    long creationSeedDelta = (long)seed.GetValue(drawing)! - before; int? creationClassCount = definition.InstanceCount; bool hasAnchor = drawing.Objects.Root.Contains("ACAD_SECTION_MANAGER");
    int managerCount = drawing.Objects.Items.OfType<DxfStoredSectionManager>().Count();
    bool shouldCreate = !expectFixed || kind == "unrelated-line-reactor";
    bool passed = created == shouldCreate && creationSeedDelta == (shouldCreate ? 1 : 0) && creationClassCount == (shouldCreate ? 1 : 0) && hasAnchor == shouldCreate && managerCount == (shouldCreate ? 1 : 0);
    if (!passed) failed++;
    using var output = new MemoryStream(); bool saved = false; string saveError = "";
    try { saved = drawing.Save(output, false); } catch (Exception error) { saveError = error.GetType().Name + ": " + error.Message; }
    results.Add(new { kind, passed, created, creationError, creationSeedDelta, creationClassCount, hasAnchor, managerCount, validation = drawing.Objects.Validate(), saved, writtenBytes = output.Length, saveError });
}
Directory.CreateDirectory(args[0]); File.WriteAllText(Path.Combine(args[0], "results.json"), JsonSerializer.Serialize(new { expectFixed, failed, librarySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(), results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(results)); Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class DisposeCallback : IEnumerable<Section>
{
    private readonly Section section; private readonly Action callback;
    internal DisposeCallback(Section section, Action callback) { this.section = section; this.callback = callback; }
    public IEnumerator<Section> GetEnumerator() => new Cursor(section, callback); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    private sealed class Cursor : IEnumerator<Section>
    {
        private bool moved; private readonly Action callback; internal Cursor(Section section, Action callback) { Current = section; this.callback = callback; }
        public Section Current { get; } object IEnumerator.Current => Current; public bool MoveNext() { if (moved) return false; moved = true; return true; } public void Reset() => throw new NotSupportedException(); public void Dispose() => callback();
    }
}
