using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.Objects;

var results = new List<object>();
int failures = 0;
string inputDirectory = args[0];
void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
void Run(string name, Action body)
{
    try { body(); results.Add(new { name, passed = true }); }
    catch (Exception error) { failures++; results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
DxfDocument Load(int year)
{
    using var input = File.OpenRead(Path.Combine(inputDirectory, $"cell-map-edit-AutoCad{year}-False.dxf"));
    return DxfDocument.Load(input) ?? throw new Exception("seed rejected");
}
DxfStoredCellStyleMap Map(DxfDocument doc) => doc.Objects.Items.OfType<DxfStoredCellStyleMap>().Single();
void Reject(DxfStoredCellStyleMap map, IEnumerable<string> names)
{
    var payload = map.Payload; var entries = map.Entries; bool rejected = false;
    try { map.ReplaceEntryNames(names); }
    catch (Exception error) when (error is InvalidOperationException or ArgumentException) { rejected = true; }
    Check(rejected, "invalid edit accepted");
    Check(ReferenceEquals(payload, map.Payload) && ReferenceEquals(entries, map.Entries), "failure changed snapshots");
}
void Recover(DxfStoredCellStyleMap map)
{
    map.ReplaceEntryNames(new[] { "recovered", "second" });
    Check(map.Entries[0].Name == "recovered", "guard remained engaged");
}
void RoundTrip(DxfDocument doc, string expected, bool binary)
{
    using var output = new MemoryStream(); Check(doc.Save(output, binary), "save rejected");
    output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new Exception("reload rejected");
    Check(Map(loaded).Entries[0].Name == expected, "decoded name changed");
}
foreach (int year in new[] { 2004, 2018 })
{
    foreach (string phase in new[] { "enumerator", "move", "current", "dispose" })
    {
        Run($"reentry/{year}/{phase}", () =>
        {
            var doc = Load(year); var map = Map(doc);
            Reject(map, new CallbackNames(new[] { "first", "second" }, phase, () =>
            { try { map.ReplaceEntryNames(new[] { "nested", "edit" }); } catch (InvalidOperationException) { } }));
            Recover(map);
        });
        Run($"version/{year}/{phase}", () =>
        {
            var doc = Load(year); var map = Map(doc); var original = doc.DrawingVariables.AcadVer;
            // Equal names must still reject an invalid source graph.
            Reject(map, new CallbackNames(map.Entries.Select(entry => entry.Name).ToArray(), phase,
                () => doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000));
            doc.DrawingVariables.AcadVer = original; Recover(map);
        });
    }
    Run($"ownership/{year}", () =>
    {
        var doc = Load(year); var map = Map(doc); var owner = (DxfDictionary)map.Owner;
        Reject(map, new CallbackNames(new[] { "first", "second" }, "dispose", () => Check(owner.Remove("MAP"), "owner unlink failed")));
        owner.Add("MAP", map); Recover(map);
    });
    Run($"reactor/{year}", () =>
    {
        var doc = Load(year); var map = Map(doc);
        Reject(map, new CallbackNames(new[] { "first", "second" }, "current", () => map.PersistentReactors.Add(null!)));
        map.PersistentReactors.Clear(); Recover(map);
    });
    Run($"external-side-effect/{year}", () =>
    {
        var doc = Load(year); var map = Map(doc);
        Reject(map, new CallbackNames(new[] { "first", "second" }, "dispose", () =>
        { doc.Objects.Root.Add("SIDE_EFFECT", new DxfDictionaryVariable { Value = "retained" }); throw new InvalidOperationException("caller failed"); }));
        Check(doc.Objects.Root.Contains("SIDE_EFFECT"), "caller side effect was rolled back"); Recover(map);
    });
    Run($"escapes/{year}", () =>
    {
        var doc = Load(year); var map = Map(doc); string name = @"\U+0041\U+005C\u+0042\日本語😀";
        map.ReplaceEntryNames(new[] { name, "" }); RoundTrip(doc, name, false); RoundTrip(doc, name, true);
    });
    Run($"exact-escaped-limit/{year}", () =>
    {
        var doc = Load(year); var map = Map(doc); string name = new string('\\', 149796) + "xxxx";
        map.ReplaceEntryNames(new[] { name, "" }); RoundTrip(doc, name, true);
        Reject(map, new[] { name + "x", "" }); Recover(map);
    });
    Run($"exact-plain-limit/{year}", () =>
    {
        var doc = Load(year); var map = Map(doc); string name = new string('x', 1048576);
        map.ReplaceEntryNames(new[] { name, "" }); RoundTrip(doc, name, true);
        Reject(map, new[] { name + "x", "" }); Recover(map);
    });
    Run($"line-breaks/{year}", () =>
    {
        var doc = Load(year); var map = Map(doc); const string name = "first\r\nsecond";
        map.ReplaceEntryNames(new[] { name, "" }); RoundTrip(doc, name, true);
        using var output = new MemoryStream(); bool rejected = false;
        try { rejected = !doc.Save(output, false); } catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { rejected = true; }
        Check(rejected && output.Length == 0, "text newline rejection wrote partial output");
    });
}
string assembly = typeof(DxfDocument).Assembly.Location;
var summary = new { passed = failures == 0, cases = results.Count, failures,
    library_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))).ToLowerInvariant(), results };
File.WriteAllText(args[1], JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Independent CELLSTYLEMAP runtime probe: {results.Count - failures} passed; {failures} failed.");
return failures == 0 ? 0 : 1;

sealed class CallbackNames : IEnumerable<string>
{
    private readonly string[] names; private readonly string phase; private readonly Action callback; private bool called;
    public CallbackNames(string[] names, string phase, Action callback) { this.names = names; this.phase = phase; this.callback = callback; }
    private void Invoke(string when) { if (!called && phase == when) { called = true; callback(); } }
    public IEnumerator<string> GetEnumerator() { Invoke("enumerator"); return new Enumerator(this); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    private sealed class Enumerator : IEnumerator<string>
    {
        private readonly CallbackNames source; private int index = -1;
        public Enumerator(CallbackNames source) { this.source = source; }
        public string Current { get { source.Invoke("current"); return source.names[index]; } }
        object IEnumerator.Current => Current;
        public bool MoveNext() { source.Invoke("move"); return ++index < source.names.Length; }
        public void Dispose() => source.Invoke("dispose");
        public void Reset() => throw new NotSupportedException();
    }
}
