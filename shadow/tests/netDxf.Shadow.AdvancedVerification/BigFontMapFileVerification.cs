using System.Text;
using System.Text.Json;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Studio;

internal static class BigFontMapFileVerification
{
    private const string Valid = "{\"schemaVersion\":1,\"resource\":\"companion.shx\",\"characters\":{\"U+03A9\":32832,\"U+1F600\":32833}}";
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("bigfont-map-file/" + name, run));
        Test("BMP and supplementary scalar round trip", () =>
        {
            var file = Read(Valid); Check(file.Resource == "companion.shx" && file.Map.Characters.Count == 2);
            Check(file.Map.Characters[0x3a9] == 32832 && file.Map.Characters[0x1f600] == 32833);
            Check(ShxBigFontMapFile.Equivalent(file.Map, ShxBigFontMapFile.Read(file.Write()).Map));
        });
        Test("canonical order ignores insertion order and hexadecimal case", () =>
        {
            var other = Read("{\"characters\":{\"U+1f600\":32833,\"U+03a9\":32832},\"resource\":\"companion.shx\",\"schemaVersion\":1}");
            Check(other.Write().SequenceEqual(Read(Valid).Write()));
        });
        Test("BOM is accepted but never written", () =>
        {
            var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(Valid)).ToArray();
            Check(ShxBigFontMapFile.Read(bytes).Write()[0] == (byte)'{');
        });
        Test("duplicate Unicode aliases reject", () => Throws<FormatException>(() => Read(Valid.Replace("U+1F600", "U+0003A9"))));
        Test("escaped duplicate field rejects", () => Throws<FormatException>(() => Read(Valid.Replace("\"schemaVersion\":1,", "\"schemaVersion\":1,\"schema\\u0056ersion\":1,"))));
        Test("unknown field rejects", () => Throws<FormatException>(() => Read(Valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"unknown\":false"))));
        Test("unsupported schema rejects", () => Throws<FormatException>(() => Read(Valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"))));
        Test("missing required field rejects", () => Throws<FormatException>(() => Read(Valid.Replace("\"schemaVersion\":1,", ""))));
        Test("trailing JSON rejects", () => Throws<FormatException>(() => Read(Valid + "{}")));
        Test("invalid UTF8 rejects", () => Throws<FormatException>(() => ShxBigFontMapFile.Read(new byte[] { 0xff, 0xfe })));
        Test("every truncated document rejects", () =>
        {
            var bytes = Encoding.UTF8.GetBytes(Valid);
            for (var i = 0; i < bytes.Length; i++) Throws<FormatException>(() => ShxBigFontMapFile.Read(bytes.AsSpan(0, i)));
        });
        foreach (var key in new[] { "U+D800", "U+110000", "U+XYZW", "U+041", "41", "u+0041", "U+ 041" })
            Test("invalid scalar key/" + key, () => Throws<FormatException>(() => Read(Valid.Replace("U+03A9", key))));
        foreach (var number in new[] { "0", "-1", "65536", "1.5", "null", "\"32832\"" })
            Test("invalid packed word/" + number, () => Throws<FormatException>(() => Read(Valid.Replace("32832", number))));
        foreach (var resource in new[] { "../companion.shx", "C:/companion.shx", "companion.ttf", ".shx" })
            Test("unsafe resource/" + resource, () => Throws<ArgumentException>(() => Read(Valid.Replace("companion.shx", resource))));
        Test("byte and character quotas", () =>
        {
            var bytes = Encoding.UTF8.GetBytes(Valid);
            Throws<InvalidOperationException>(() => ShxBigFontMapFile.Read(bytes, new(MaximumBytes: bytes.Length - 1)));
            Throws<InvalidOperationException>(() => ShxBigFontMapFile.Read(bytes, new(MaximumCharacters: 1)));
            Check(ShxBigFontMapFile.Read(bytes, new(MaximumBytes: bytes.Length, MaximumCharacters: 2)).Map.Characters.Count == 2);
        });
        Test("invalid limits and cancellation", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => ShxBigFontMapFile.Read([], new(MaximumBytes: 0)));
            Throws<OperationCanceledException>(() => ShxBigFontMapFile.Read([], cancellationToken: new(true)));
            Throws<OperationCanceledException>(() => Read(Valid).Write(new(true)));
        });
        Test("catalog imports only explicit uploaded BIGFONT resources", () =>
        {
            var catalog = new ShxFontCatalog(); var empty = catalog.Current;
            Throws<ArgumentException>(() => catalog.ImportBigFontMap(Read(Valid))); Check(ReferenceEquals(empty, catalog.Current));
            catalog.Register("companion.shx", ShxFontReader.ReadAscii("*1,0,X\n8,1,0,0")); var plain = catalog.Current;
            Throws<ArgumentException>(() => catalog.ImportBigFontMap(Read(Valid))); Check(ReferenceEquals(plain, catalog.Current));
        });
        Test("catalog preflight is atomic and validates lead bytes", () =>
        {
            var catalog = Catalog(); var before = catalog.Current;
            Throws<ArgumentException>(() => catalog.ImportBigFontMap(Read(Valid.Replace("32832", "65"))));
            Throws<ArgumentException>(() => catalog.ImportBigFontMap(Read(Valid.Replace("32832", "33280"))));
            Check(ReferenceEquals(before, catalog.Current));
        });
        Test("equivalent imports retain exact set and mapping identity", () =>
        {
            var catalog = Catalog(); var first = catalog.ImportBigFontMap(Read(Valid));
            var second = catalog.ImportBigFontMap(Read(Valid)); Check(ReferenceEquals(first, second));
            Check(ReferenceEquals(first.ResolveBigFont("companion.shx")!.Map, second.ResolveBigFont("COMPANION.SHX")!.Map));
        });
        Test("replacement leaves retained interpretations immutable", () =>
        {
            var catalog = Catalog(); var first = catalog.ImportBigFontMap(Read(Valid));
            var second = catalog.ImportBigFontMap(Read(Valid.Replace("32832", "32833")));
            Check(second.Revision == first.Revision + 1 && first.ResolveBigFont("companion.shx")!.Map.Characters[0x3a9] == 32832);
            Check(second.ResolveBigFont("companion.shx")!.Map.Characters[0x3a9] == 32833);
        });
        Test("absent mapped glyph is retained for consumer diagnostics", () =>
        {
            var catalog = Catalog(); catalog.ImportBigFontMap(Read(Valid.Replace("32832", "32834")));
            Check(catalog.Current.ResolveBigFont("companion.shx")!.Map.Characters[0x3a9] == 32834);
        });
        Test("Studio map change advances view not source or undo history", () =>
        {
            using var studio = new Studio(); var initial = State(studio);
            var revision = initial.GetProperty("revision").GetInt64();
            studio.LoadFont(revision, "companion.shp", Encoding.ASCII.GetBytes(BigFontVerification.Regular));
            var before = State(studio); studio.LoadBigFontMap(revision, Encoding.UTF8.GetBytes(Valid)); var after = State(studio);
            Check(after.GetProperty("revision").GetInt64() == revision);
            Check(after.GetProperty("viewVersion").GetInt64() == before.GetProperty("viewVersion").GetInt64() + 1);
            Check(after.GetProperty("fontRevision").GetInt64() == before.GetProperty("fontRevision").GetInt64() + 1);
            Check(after.GetProperty("undoCount").GetInt32() == before.GetProperty("undoCount").GetInt32());
            studio.LoadBigFontMap(revision, Encoding.UTF8.GetBytes(Valid)); var noop = State(studio);
            Check(noop.GetProperty("viewVersion").GetInt64() == after.GetProperty("viewVersion").GetInt64());
            Check(noop.GetProperty("changeVersion").GetInt64() == after.GetProperty("changeVersion").GetInt64());
            Throws<OperationCanceledException>(() => studio.LoadBigFontMap(revision, Encoding.UTF8.GetBytes(Valid), new(true)));
        });
    }
    private static ShxBigFontMapFile Read(string value) => ShxBigFontMapFile.Read(Encoding.UTF8.GetBytes(value));
    private static ShxFontCatalog Catalog()
    {
        var catalog = new ShxFontCatalog(); catalog.Register("companion.shx", ShxFontReader.ReadAscii(BigFontVerification.Regular)); return catalog;
    }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Check(bool condition) { if (!condition) throw new Exception("BIGFONT map file assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
