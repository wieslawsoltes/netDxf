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
using netDxf.Tables;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static int Failed;
    private static string Output = "";
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run(string name, Action action)
    {
        try { action(); Results.Add(new { name, passed = true, error = "" }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { Failed++; Results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static long Seed(DxfDocument document) => Convert.ToInt64(typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document));
    private static byte[] Save(DxfDocument document, bool binary)
    { using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Save failed."); return stream.ToArray(); }
    private static DxfStoredTableGeometry Geometry(DxfDocument document) => document.Objects.Items.OfType<DxfStoredTableGeometry>().Single();
    private static DxfStoredTableCellGeometry Content(double width = -17) => new(new Vector3(1, 2, 3), new Vector3(4, 5, 6), width, -23, 0, 1.0e300, int.MinValue);
    private static DxfStoredTableGeometryCell Cell(DxfObject? target = null, double width = -0.0) => new(int.MinValue, width, -37, target!, new[] { Content() });
    private static DxfDocument Fixture(bool binary)
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018); var placeholder = new DxfXRecord(); document.Objects.Root.Add("INDEPENDENT_GEOMETRY", placeholder, true);
        using var original = new MemoryStream(Save(document, false)); var raw = DxfRawDocument.Load(original);
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Tags.Any(t => t.Code == 5 && Equals(t.Value, placeholder.Handle)));
        var header = record.Tags.TakeWhile(t => t.Code != 100).Select(t => t.Code == 0 ? new DxfTag(0, "TABLEGEOMETRY") : t);
        var body = new[] { new DxfTag(100, "AcDbTableGeometry"), new DxfTag(90, 1), new DxfTag(91, 1), new DxfTag(92, 1), new DxfTag(93, 0), new DxfTag(40, 0.0), new DxfTag(41, 0.0), new DxfTag(330, "0000"), new DxfTag(94, 0) };
        raw = raw.WithRecord(record, header.Concat(body)); using var input = new MemoryStream(); raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(input, binary); input.Position = 0;
        return DxfDocument.Load(input) ?? throw new FormatException("Synthetic stored geometry fixture rejected.");
    }
    private static void Refuse(DxfDocument document, Action action)
    {
        var geometry = Geometry(document); var payload = geometry.Payload; var cells = geometry.Cells; var references = geometry.References.ToArray(); int rows = geometry.RowCount, columns = geometry.ColumnCount; long seed = Seed(document);
        bool rejected = false; try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid geometry replacement succeeded.");
        Check(ReferenceEquals(payload, geometry.Payload) && ReferenceEquals(cells, geometry.Cells) && references.SequenceEqual(geometry.References) && geometry.RowCount == rows && geometry.ColumnCount == columns && Seed(document) == seed, "Rejected geometry replacement changed packet/snapshots/seed.");
    }
    private static void RoundTrip(DxfDocument document, bool binary, string name)
    {
        var geometry = Geometry(document); byte[] saved = Save(document, binary); File.WriteAllBytes(Path.Combine(Output, name + ".dxf"), saved);
        using var input = new MemoryStream(saved); var loaded = Geometry(DxfDocument.Load(input)!);
        Check(geometry.RowCount == loaded.RowCount && geometry.ColumnCount == loaded.ColumnCount && geometry.Cells.Count == loaded.Cells.Count, "Reload changed stored counts.");
        string Tag(DxfTag tag) => tag.Code + ":" + Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture);
        Check(geometry.Payload.Select(Tag).SequenceEqual(loaded.Payload.Select(Tag)), "Reload changed explicit replacement packet.");
    }
    private static void Main(string[] args)
    {
        Output = Path.GetFullPath(args[0]); Directory.CreateDirectory(Output);
        foreach (bool binary in new[] { false, true })
        {
            Run("no-op-preserves-packet-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); var payload = geometry.Payload; var cells = geometry.Cells; long seed = Seed(document);
                geometry.ReplaceGeometry(geometry.RowCount, geometry.ColumnCount, geometry.Cells);
                Check(ReferenceEquals(payload, geometry.Payload) && ReferenceEquals(cells, geometry.Cells) && Seed(document) == seed, "No-op replaced original snapshots or allocated.");
                RoundTrip(document, binary, "noop-" + binary);
            });
            Run("replace-values-and-snapshots-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); var oldPayload = geometry.Payload; var oldCells = geometry.Cells; var oldRefs = geometry.References; long seed = Seed(document);
                var source = new List<DxfStoredTableCellGeometry> { Content() }; var cell = new DxfStoredTableGeometryCell(int.MinValue, -3, -4, null!, source); source.Clear(); var cells = new List<DxfStoredTableGeometryCell> { cell, cell };
                geometry.ReplaceGeometry(777, 888, cells); cells.Clear();
                Check(geometry.Cells.Count == 2 && geometry.Cells.All(c => ReferenceEquals(c, cell)) && cell.Geometry.Count == 1, "Replacement borrowed caller collection or lost duplicate values.");
                Check(oldCells.Count == 1 && oldRefs.Count == 0 && oldPayload.Count == 9 && Seed(document) == seed, "Previous snapshots or handle seed changed.");
                Check(((IList)geometry.Payload).IsReadOnly && ((IList)geometry.Cells).IsReadOnly && ((IList)cell.Geometry).IsReadOnly, "Replacement exposes writable collections.");
                RoundTrip(document, binary, "replacement-" + binary);
            });
            Run("reference-identity-and-release-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); var first = document.TextStyles.Add(new TextStyle("FIRST", "txt.shx")); var second = document.TextStyles.Add(new TextStyle("SECOND", "txt.shx"));
                geometry.ReplaceGeometry(1, 1, new[] { Cell(first), Cell(first) }); var oldRefs = geometry.References;
                Check(oldRefs.SequenceEqual(new[] { first, first }) && !document.TextStyles.Remove(first), "First explicit dependency is not guarded.");
                geometry.ReplaceGeometry(0, 1048576, new[] { Cell(second) });
                Check(oldRefs.SequenceEqual(new[] { first, first }) && geometry.References.SequenceEqual(new[] { second }), "Reference snapshots changed or current dependency stayed stale.");
                Check(document.TextStyles.Remove(first) && !document.TextStyles.Remove(second), "Replacement removal guards do not follow actual current dependency.");
                RoundTrip(document, binary, "reference-replacement-" + binary);
                geometry.ReplaceGeometry(0, 0, Array.Empty<DxfStoredTableGeometryCell>()); Check(document.TextStyles.Remove(second), "Empty replacement retains old dependency."); RoundTrip(document, binary, "empty-" + binary);
            });
            Run("signed-zero-is-an-explicit-change-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); var original = geometry.Payload;
                var cell = new DxfStoredTableGeometryCell(0, -0.0, 0.0, null!, Array.Empty<DxfStoredTableCellGeometry>()); geometry.ReplaceGeometry(1, 1, new[] { cell });
                Check(!ReferenceEquals(original, geometry.Payload) && BitConverter.DoubleToInt64Bits((double)geometry.Payload.First(t => t.Code == 40).Value) == long.MinValue, "Negative zero request was treated as an equal no-op.");
                var payload = geometry.Payload; geometry.ReplaceGeometry(1, 1, new[] { cell }); Check(ReferenceEquals(payload, geometry.Payload), "Bit-exact repeated zero request changed packet."); RoundTrip(document, binary, "signed-zero-" + binary);
            });
            Run("iteration-and-disposal-atomicity-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); IEnumerable<DxfStoredTableGeometryCell> Throwing() { yield return Cell(); throw new InvalidOperationException("Caller iteration failed."); }
                Refuse(document, () => geometry.ReplaceGeometry(1, 1, Throwing())); Refuse(document, () => geometry.ReplaceGeometry(1, 1, new OnDispose(Cell(), () => throw new InvalidOperationException("Caller disposal failed."))));
                geometry.ReplaceGeometry(1, 1, new[] { Cell() }); Check(geometry.Cells.Count == 1, "Failed enumeration left edit locked.");
            });
            Run("caught-reentry-poisons-outer-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); bool caught = false;
                IEnumerable<DxfStoredTableGeometryCell> Cells() { yield return Cell(); try { geometry.ReplaceGeometry(0, 0, Array.Empty<DxfStoredTableGeometryCell>()); } catch (InvalidOperationException) { caught = true; } }
                Refuse(document, () => geometry.ReplaceGeometry(1, 1, Cells())); Check(caught, "Nested call did not reject."); geometry.ReplaceGeometry(2, 3, new[] { Cell() });
            });
            Run("dispose-removes-new-target-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); var target = document.TextStyles.Add(new TextStyle("DISPOSE_TARGET", "txt.shx")); var cell = Cell(target);
                Refuse(document, () => geometry.ReplaceGeometry(1, 1, new OnDispose(cell, () => Check(document.TextStyles.Remove(target), "Callback target could not be removed."))));
                Check(document.TextStyles["DISPOSE_TARGET"] == null, "Caller side effect was incorrectly rolled back.");
            });
            Run("source-anchor-noop-guard-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); document.Objects.Root.Remove("INDEPENDENT_GEOMETRY"); Refuse(document, () => geometry.ReplaceGeometry(1, 1, geometry.Cells));
            });
            Run("source-profile-noop-guard-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); document.DrawingVariables.AcadVer = DxfVersion.AutoCad2013; Refuse(document, () => geometry.ReplaceGeometry(1, 1, geometry.Cells));
            });
            Run("foreign-and-detached-identities-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document); var foreign = new DxfDocument(DxfVersion.AutoCad2018); var target = foreign.TextStyles.Add(new TextStyle("TARGET", "txt.shx"));
                Refuse(document, () => geometry.ReplaceGeometry(1, 1, new[] { Cell(target) })); Refuse(document, () => geometry.ReplaceGeometry(1, 1, new[] { Cell(new TextStyle("DETACHED", "txt.shx")) }));
            });
            Run("count-and-cell-null-guards-" + binary, () =>
            {
                var document = Fixture(binary); var geometry = Geometry(document);
                Refuse(document, () => geometry.ReplaceGeometry(-1, 1, Array.Empty<DxfStoredTableGeometryCell>())); Refuse(document, () => geometry.ReplaceGeometry(1, 1048577, Array.Empty<DxfStoredTableGeometryCell>()));
                Refuse(document, () => geometry.ReplaceGeometry(1, 1, new DxfStoredTableGeometryCell[] { null! })); Refuse(document, () => geometry.ReplaceGeometry(1, 1, Enumerable.Repeat(Cell(), 65536)));
            });
            Run("constructor-finite-values-" + binary, () =>
            {
                var document = Fixture(binary); Refuse(document, () => _ = Content(double.NaN)); Refuse(document, () => _ = new DxfStoredTableGeometryCell(0, double.PositiveInfinity, 0, null!, Array.Empty<DxfStoredTableCellGeometry>()));
                Refuse(document, () => _ = new DxfStoredTableCellGeometry(new Vector3(0, double.NegativeInfinity, 0), Vector3.Zero, 0, 0, 0, 0, 0));
            });
        }
        if (args.Length > 1) NativeCases(Path.GetFullPath(args[1]));
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonSerializer.Serialize(new { cases = Results.Count, failed = Failed, librarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(), results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Results.Count} cases; {Failed} failed."); Environment.ExitCode = Failed == 0 ? 0 : 1;
    }
    private static string TagValue(DxfTag tag) => tag.Code + ":" + Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture);
    private static void NativeCases(string repository)
    {
        string directory = Path.Combine(repository, "tests/fixtures/table-content");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        foreach (var fixture in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string path = Path.GetFullPath(Path.Combine(directory, fixture.GetProperty("fixture").GetString()!));
            byte[] bytes = File.ReadAllBytes(path);
            if (path.EndsWith(".gz", StringComparison.Ordinal))
            { using var compressed = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress); using var expanded = new MemoryStream(); compressed.CopyTo(expanded); bytes = expanded.ToArray(); }
            string hash = fixture.TryGetProperty("sha256", out var adaptedHash) ? adaptedHash.GetString()! : fixture.GetProperty("source_sha256").GetString()!;
            Check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase), "Native manifest hash differs.");
            using var input = new MemoryStream(bytes); var raw = DxfRawDocument.Load(input);
            var handles = raw.Sections.SelectMany(s => s.Records).Where(r => r.Tags.Any(t => t.Code == 0 && Equals(t.Value, "TABLEGEOMETRY"))).Select(r => (string)r.Tags.First(t => t.Code == 5).Value).ToArray();
            foreach (bool binary in new[] { false, true })
            foreach (string handle in handles)
            {
                string name = "native-" + Path.GetFileNameWithoutExtension(fixture.GetProperty("file").GetString()!) + "-" + handle + "-" + binary;
                Run(name, () =>
                {
                    using var converted = new MemoryStream(); raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(converted, binary); converted.Position = 0;
                    var document = DxfDocument.Load(converted)!;
                    var geometry = document.Objects.Items.OfType<DxfStoredTableGeometry>().Single(g => g.Handle == handle);
                    var oldPayload = geometry.Payload; var oldValues = oldPayload.Select(TagValue).ToArray(); var oldCells = geometry.Cells; int oldCount = oldCells.Count; long seed = Seed(document);
                    var others = document.Objects.Items.OfType<DxfStoredTableGeometry>().Where(g => g != geometry).ToDictionary(g => g.Handle, g => g.Payload.Select(TagValue).ToArray());
                    geometry.ReplaceGeometry(geometry.RowCount, geometry.ColumnCount, geometry.Cells);
                    Check(ReferenceEquals(oldPayload, geometry.Payload) && ReferenceEquals(oldCells, geometry.Cells) && Seed(document) == seed, "Native no-op changed snapshots/seed.");
                    geometry.ReplaceGeometry(777, 0, new[] { Cell() });
                    Check(geometry.Handle == handle && Seed(document) == seed && oldCells.Count == oldCount && oldValues.SequenceEqual(oldPayload.Select(TagValue)), "Native replacement changed identity/seed/prior snapshot.");
                    byte[] saved = Save(document, binary); File.WriteAllBytes(Path.Combine(Output, name + ".dxf"), saved);
                    using var reloadInput = new MemoryStream(saved); var reloaded = DxfDocument.Load(reloadInput)!;
                    var next = reloaded.Objects.Items.OfType<DxfStoredTableGeometry>().Single(g => g.Handle == handle);
                    Check(next.RowCount == 777 && next.ColumnCount == 0 && geometry.Payload.Select(TagValue).SequenceEqual(next.Payload.Select(TagValue)), "Native explicit replacement failed exact packet reload.");
                    foreach (var other in others) Check(other.Value.SequenceEqual(reloaded.Objects.Items.OfType<DxfStoredTableGeometry>().Single(g => g.Handle == other.Key).Payload.Select(TagValue)), "Native edit changed companion TABLEGEOMETRY packet.");
                });
            }
        }
    }
    private sealed class OnDispose : IEnumerable<DxfStoredTableGeometryCell>
    {
        private readonly DxfStoredTableGeometryCell cell; private readonly Action dispose;
        internal OnDispose(DxfStoredTableGeometryCell cell, Action dispose) { this.cell = cell; this.dispose = dispose; }
        public IEnumerator<DxfStoredTableGeometryCell> GetEnumerator() => new Cursor(cell, dispose); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Cursor : IEnumerator<DxfStoredTableGeometryCell>
        {
            private bool moved; private readonly Action dispose; internal Cursor(DxfStoredTableGeometryCell cell, Action dispose) { Current = cell; this.dispose = dispose; }
            public DxfStoredTableGeometryCell Current { get; } object IEnumerator.Current => Current;
            public bool MoveNext() { if (moved) return false; moved = true; return true; } public void Reset() => throw new NotSupportedException(); public void Dispose() => dispose();
        }
    }
}
