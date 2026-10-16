using System.Text;
using System.Text.Json;
using netDxf.Shadow;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Studio;

internal static class BigFontMapFileVerification
{
    private static readonly byte[] File = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"resource\":\"big.shx\",\"characters\":{\"U+03A9\":32832,\"U+1F600\":32833}}");
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action action) => tests.Add(("bigfont-map-file/" + name, action));
        Test("canonical UTF8 roundtrip preserves supplementary characters", () =>
        {
            var first = ShxBigFontMapFile.Read(File); var second = ShxBigFontMapFile.Read(first.Write());
            Check(first.Resource == "big.shx" && first.Map.Equals(second.Map));
            Check(first.Map.Characters[0x1f600] == 32833 && first.Map.GetHashCode() == second.Map.GetHashCode());
            Check(first.Write().SequenceEqual(second.Write()));
        });
        Test("map equality ignores key insertion order", () =>
        {
            var a = new ShxBigFontMap([new(65, 1),new(66, 2)]); var b = new ShxBigFontMap([new(66,2),new(65,1)]);
            Check(a.Equals(b) && a.GetHashCode() == b.GetHashCode());
            Check(!a.Equals(new ShxBigFontMap([new(65,2),new(66,1)])));
        });
        foreach (var invalid in new[] {
            "[]", "{}", "{\"schemaVersion\":\"1\"}", "{\"schemaVersion\":2}",
            "{\"schemaVersion\":1,\"schemaVersion\":1}",
            "{\"schemaVersion\":1,\"resource\":\"big.shx\",\"characters\":{},\"execute\":\"x\"}",
            "{\"schemaVersion\":1,\"resource\":\"\",\"characters\":{}}",
            "{\"schemaVersion\":1,\"resource\":\"big.shx\",\"characters\":{\"A\":1}}",
            "{\"schemaVersion\":1,\"resource\":\"big.shx\",\"characters\":{\"U+D800\":1}}",
            "{\"schemaVersion\":1,\"resource\":\"big.shx\",\"characters\":{\"U+110000\":1}}",
            "{\"schemaVersion\":1,\"resource\":\"big.shx\",\"characters\":{\"U+0041\":65536}}",
            "{\"schemaVersion\":1,\"resource\":\"big.shx\",\"characters\":{\"U+0041\":1.5}}",
            "{\"schemaVersion\":1,\"resource\":\"big.shx\",\"characters\":{\"U+0041\":\"1\"}}"
        }) Test("strict schema rejection/" + tests.Count, () => Throws<FormatException>(() => ShxBigFontMapFile.Read(Encoding.UTF8.GetBytes(invalid))));
        Test("Unicode aliases and duplicate JSON mapping keys are refused", () =>
        {
            foreach(var chars in new[]{"\"U+0041\":1,\"U+41\":2","\"U+0041\":1,\"U+0041\":1"})
                Throws<ArgumentException>(() => ShxBigFontMapFile.Read(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"resource\":\"x\",\"characters\":{"+chars+"}}")));
        });
        Test("byte character and cancellation quotas", () =>
        {
            Throws<InvalidOperationException>(()=>ShxBigFontMapFile.Read(File,maximumBytes:File.Length-1));
            Throws<InvalidOperationException>(()=>ShxBigFontMapFile.Read(File,maximumCharacters:1));
            Throws<OperationCanceledException>(()=>ShxBigFontMapFile.Read(File,cancellationToken:new(true)));
        });
        Test("catalog registration of equivalent maps preserves revision and identity", () =>
        {
            var catalog=new ShxFontCatalog(); var font=ShxFontReader.ReadAscii(BigFontVerification.Regular);
            var a=catalog.RegisterBigFont("big.shx",font,ShxBigFontMapFile.Read(File).Map);
            Check(ReferenceEquals(a,catalog.RegisterBigFont("big.shx",font,ShxBigFontMapFile.Read(File).Map)));
        });
        Test("Studio explicit map upload is source neutral and idempotent", () =>
        {
            using var studio=new Studio();var initial=State(studio);
            studio.LoadFont(initial.GetProperty("revision").GetInt64(),"big.shp",Encoding.ASCII.GetBytes(BigFontVerification.Regular));
            var loaded=State(studio);studio.LoadBigFontMap(loaded.GetProperty("revision").GetInt64(),File);var mapped=State(studio);
            Check(mapped.GetProperty("fontRevision").GetInt64()==loaded.GetProperty("fontRevision").GetInt64()+1);
            Check(mapped.GetProperty("revision").GetInt64()==loaded.GetProperty("revision").GetInt64());
            Check(mapped.GetProperty("viewVersion").GetInt64()>loaded.GetProperty("viewVersion").GetInt64());
            Check(mapped.GetProperty("undoCount").GetInt32()==loaded.GetProperty("undoCount").GetInt32());
            studio.LoadBigFontMap(mapped.GetProperty("revision").GetInt64(),File);var same=State(studio);
            Check(same.GetProperty("viewVersion").GetInt64()==mapped.GetProperty("viewVersion").GetInt64());
            Check(same.GetProperty("changeVersion").GetInt64()==mapped.GetProperty("changeVersion").GetInt64());
        });
        Test("missing or ordinary font refuses map before resource publication", () =>
        {
            using var studio=new Studio();var initial=State(studio);var revision=initial.GetProperty("revision").GetInt64();
            Throws<ArgumentException>(()=>studio.LoadBigFontMap(revision,File));
            Check(State(studio).GetProperty("fontRevision").GetInt64()==initial.GetProperty("fontRevision").GetInt64());
            var plain=new ShxBigFontMapFile("shadow-demo.shx",new([new(65,1)])).Write();
            Throws<ArgumentException>(()=>studio.LoadBigFontMap(revision,plain));
        });
        Test("stale source revision refuses map without changing font revision", () =>
        {
            using var studio=new Studio();var initial=State(studio);
            studio.LoadFont(initial.GetProperty("revision").GetInt64(),"big.shp",Encoding.ASCII.GetBytes(BigFontVerification.Regular));
            var loaded=State(studio);
            Throws<ShadowConflictException>(()=>studio.LoadBigFontMap(loaded.GetProperty("revision").GetInt64()+1,File));
            Check(State(studio).GetProperty("fontRevision").GetInt64()==loaded.GetProperty("fontRevision").GetInt64());
        });
    }
    private static JsonElement State(Studio studio)=>JsonSerializer.SerializeToElement(studio.State(),new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Check(bool value){if(!value)throw new Exception("BIGFONT map-file assertion failed.");}
    private static void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}.");}
}
