using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

internal static class Program
{
    private static readonly List<object> Results = new(); private static int Failed; private static string Output = "";
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run(string name, Action action) { try { action(); Results.Add(new { name, passed = true, error = "" }); Console.WriteLine("PASS " + name); } catch (Exception error) { Failed++; Results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error.Message); } }
    private static long Seed(DxfDocument drawing) => (long)typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawing)!;
    private static byte[] Save(DxfDocument drawing, bool binary) { using var stream = new MemoryStream(); Check(drawing.Save(stream, binary), "Save returned false."); return stream.ToArray(); }
    private static DxfDocument Load(byte[] bytes) { using var stream = new MemoryStream(bytes); return DxfDocument.Load(stream)!; }
    private static DxfRawDocument Raw(byte[] bytes) { using var stream = new MemoryStream(bytes); return DxfRawDocument.Load(stream); }
    private static byte[] Transport(DxfRawDocument raw, bool binary) { using var stream = new MemoryStream(); raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(stream, binary); return stream.ToArray(); }
    private static DxfStoredTableContent Content(DxfDocument drawing, string? handle = null) => drawing.Objects.Items.OfType<DxfStoredTableContent>().Single(c => handle == null || c.Handle == handle);
    private static string Value(DxfTag tag) => tag.Code + ":" + (tag.Value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(tag.Value, CultureInfo.InvariantCulture));
    private static object Replacement(DxfStoredTableContentValue value) => value.Kind switch { DxfStoredTableContentValueKind.Integer => int.MinValue, DxfStoredTableContentValueKind.Double => -17.125, DxfStoredTableContentValueKind.String => "CELLCONTENT_BEGIN \\U+0041 Ω", _ => new Vector3(-11.25, 23.5, 37.75) };
    private static string? Display(DxfStoredTableContentValue value) => value.StoredFormatFlags.HasValue ? "Explicit display \\U+0042 Ω" : null;
    private static void Refuse(DxfDocument drawing, DxfStoredTableContent content, Action action)
    {
        var payload = content.Payload; var subclasses = content.Subclasses; var values = content.StoredValues; var references = content.References.ToArray(); long seed = Seed(drawing); bool rejected = false;
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid content operation succeeded."); Check(ReferenceEquals(payload, content.Payload) && ReferenceEquals(subclasses, content.Subclasses) && ReferenceEquals(values, content.StoredValues) && references.SequenceEqual(content.References) && Seed(drawing) == seed, "Rejected edit changed snapshots or seed.");
    }
    private static DxfDocument Fixture(DxfVersion version, bool binary)
    {
        var drawing = new DxfDocument(version); var holder = new DxfXRecord(); drawing.Objects.Root.Add("INDEPENDENT_CONTENT", holder);
        var raw = Raw(Save(drawing, false)); var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Tags.Any(t => t.Code == 5 && Equals(t.Value, holder.Handle)));
        var tags = record.Tags.TakeWhile(t => t.Code != 100).Select(t => t.Code == 0 ? new DxfTag(0, "TABLECONTENT") : t).ToList();
        tags.AddRange(new[] { new DxfTag(100,"AcDbLinkedData"),new DxfTag(1,"INITIAL"),new DxfTag(300,"DESCRIPTION"),new DxfTag(100,"AcDbLinkedTableData"),new DxfTag(90,1),new DxfTag(300,"COLUMN"),new DxfTag(91,1),new DxfTag(301,"ROW"),new DxfTag(1,"LINKEDTABLEDATAROW_BEGIN"),new DxfTag(1,"LINKEDTABLEDATACELL_BEGIN") });
        foreach (int kind in new[] { 1, 2, 4, 32 })
        {
            tags.AddRange(new[] { new DxfTag(302,"CONTENT"),new DxfTag(1,"CELLCONTENT_BEGIN"),new DxfTag(90,1),new DxfTag(300,"VALUE") });
            if (version >= DxfVersion.AutoCad2007) tags.Add(new DxfTag(93,2)); tags.Add(new DxfTag(90,kind));
            if (kind == 1) tags.Add(new DxfTag(91,7)); else if (kind == 2) tags.Add(new DxfTag(140,0.0)); else if (kind == 4) tags.Add(new DxfTag(1,"INITIAL VALUE"));
            else tags.AddRange(new[] { new DxfTag(11,1.0),new DxfTag(21,2.0),new DxfTag(31,3.0) });
            if (version >= DxfVersion.AutoCad2007) tags.AddRange(new[] { new DxfTag(94,0),new DxfTag(300,"%lu2"),new DxfTag(302,"ORIGINAL DISPLAY"),new DxfTag(304,"ACVALUE_END") });
            tags.AddRange(new[] { new DxfTag(91,0),new DxfTag(309,"CELLCONTENT_END") });
        }
        tags.AddRange(new[] { new DxfTag(309,"LINKEDTABLEDATACELL_END"),new DxfTag(309,"LINKEDTABLEDATAROW_END"),new DxfTag(92,0),new DxfTag(100,"AcDbFormattedTableData"),new DxfTag(100,"AcDbTableContent"),new DxfTag(340,"0") });
        return Load(Transport(raw.WithRecord(record,tags),binary));
    }
    private static void RoundTrip(DxfDocument drawing, DxfStoredTableContent content, bool binary, string name)
    {
        byte[] saved = Save(drawing,binary); File.WriteAllBytes(Path.Combine(Output,name+".dxf"),saved); var next=Content(Load(saved),content.Handle);
        Check(next.Payload.Select(Value).SequenceEqual(content.Payload.Select(Value)),"Edited TABLECONTENT packet changed on reload.");
        Check(next.Name==content.Name && next.Description==content.Description && next.StoredValues.Count==content.StoredValues.Count,"Edited projections changed on reload.");
        foreach(var pair in content.StoredValues.Zip(next.StoredValues)) Check(Equals(pair.First.Value,pair.Second.Value) && pair.First.FormattedText==pair.Second.FormattedText,"Scalar/display decode differs after reload.");
    }
    private static DxfDocument StyleFixture(DxfVersion version,bool binary)
    {
        var drawing=Fixture(version,binary);var first=new DxfXRecord();var second=new DxfXRecord();drawing.Objects.Root.Add("STYLE_ONE",first);drawing.Objects.Root.Add("STYLE_TWO",second);
        var raw=Raw(Save(drawing,false));foreach(string handle in new[]{first.Handle,second.Handle})
        {
            var record=raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Tags.Any(t=>t.Code==5&&Equals(t.Value,handle)));
            var tags=record.Tags.TakeWhile(t=>t.Code!=100).Select(t=>t.Code==0?new DxfTag(0,"TABLESTYLE"):t).Concat(new[]{new DxfTag(100,"IndependentPrivateTableStyle"),new DxfTag(1,"Private style "+handle)});
            raw=raw.WithRecord(record,tags);
        }
        return Load(Transport(raw,binary));
    }
    private static void Main(string[] args)
    {
        Output=Path.GetFullPath(args[1]);Directory.CreateDirectory(Output);
        foreach(var version in new[]{DxfVersion.AutoCad2004,DxfVersion.AutoCad2018}) foreach(bool binary in new[]{false,true})
        {
            string name=version+"-"+binary;
            Run(name+"-four-kinds-and-snapshots",()=>
            {
                var drawing=Fixture(version,binary);var content=Content(drawing);Check(content.StoredValues.Count==4,"Exact four-kind frame fixture was not projected.");
                var payload=content.Payload;var values=content.StoredValues;var subclasses=content.Subclasses;var references=content.References;long seed=Seed(drawing);
                content.ReplaceContent(content.Name,content.Description,content.TableStyle,values.Select(v=>v.WithValue(v.Value,v.FormattedText)));
                Check(ReferenceEquals(payload,content.Payload)&&ReferenceEquals(values,content.StoredValues)&&ReferenceEquals(subclasses,content.Subclasses),"Equivalent request changed snapshots.");
                var edits=values.Select(v=>v.WithValue(Replacement(v),Display(v))).ToList();content.ReplaceContent("Independent \\U+0041 Ω","Description Ω",content.TableStyle,edits);edits.Clear();
                Check(values.Count==4&&(int)values[0].Value==7&&references.Count==0&&Seed(drawing)==seed,"Committed edit changed previous values/references/seed.");
                Check(content.ColumnCount==1&&content.RowCount==1&&content.StoredValues.Select(v=>v.Kind).SequenceEqual(values.Select(v=>v.Kind)),"Edit changed counts or value kinds.");
                Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,content.TableStyle,new[]{values[0].WithValue(7,values[0].FormattedText)}));
                RoundTrip(drawing,content,binary,name+"-values");
            });
            Run(name+"-duplicate-foreign-and-kind-guards",()=>
            {
                var drawing=Fixture(version,binary);var content=Content(drawing);var value=content.StoredValues[0];var edit=value.WithValue(13,Display(value));
                Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,content.TableStyle,new[]{edit,edit}));var foreign=Content(Fixture(version,binary));
                Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,content.TableStyle,new[]{foreign.StoredValues[0].WithValue(13,Display(foreign.StoredValues[0]))}));
                Refuse(drawing,content,()=>value.WithValue(13L,Display(value)));Refuse(drawing,content,()=>content.StoredValues[1].WithValue(double.NaN,Display(content.StoredValues[1])));
                if(version>=DxfVersion.AutoCad2007)Refuse(drawing,content,()=>value.WithValue(13));else Refuse(drawing,content,()=>value.WithValue(13,"unavailable display"));
            });
            Run(name+"-callbacks-and-source-noop",()=>
            {
                var drawing=Fixture(version,binary);var content=Content(drawing);var value=content.StoredValues[0];var edit=value.WithValue(13,Display(value));
                IEnumerable<DxfStoredTableContentValueEdit> Throwing(){yield return edit;throw new InvalidOperationException("Caller MoveNext");}
                Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,content.TableStyle,Throwing()));
                Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,content.TableStyle,new Callback(edit,()=>throw new InvalidOperationException("Caller Dispose"))));
                bool caught=false;void Reenter(){try{content.ReplaceContent(content.Name,content.Description,content.TableStyle,Array.Empty<DxfStoredTableContentValueEdit>());}catch(InvalidOperationException){caught=true;}}
                Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,content.TableStyle,new Callback(edit,Reenter)));Check(caught,"Nested edit was not rejected.");
                drawing.DrawingVariables.AcadVer=version==DxfVersion.AutoCad2018?DxfVersion.AutoCad2013:DxfVersion.AutoCad2007;Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,content.TableStyle,Array.Empty<DxfStoredTableContentValueEdit>()));
                drawing.DrawingVariables.AcadVer=version;drawing.Objects.Root.Remove("INDEPENDENT_CONTENT");Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,content.TableStyle,Array.Empty<DxfStoredTableContentValueEdit>()));
            });
            Run(name+"-signed-zero",()=>
            {
                var drawing=Fixture(version,binary);var content=Content(drawing);var value=content.StoredValues[1];var original=content.Payload;
                content.ReplaceContent(content.Name,content.Description,content.TableStyle,new[]{value.WithValue(-0.0,value.FormattedText)});
                Check(!ReferenceEquals(original,content.Payload)&&BitConverter.DoubleToInt64Bits((double)content.StoredValues[1].Value)==long.MinValue,"Signed-zero change was lost.");RoundTrip(drawing,content,binary,name+"-zero");
            });
            Run(name+"-text-framing-and-encoded-limit",()=>
            {
                var drawing=Fixture(version,binary);var content=Content(drawing);var text=content.StoredValues[2];
                content.ReplaceContent(content.Name,content.Description,content.TableStyle,new[]{text.WithValue("LINKEDTABLEDATAROW_BEGIN",text.StoredFormatFlags.HasValue?"ACVALUE_END":null)});
                Check(content.StoredValues.Count==4&&(string)content.StoredValues[2].Value=="LINKEDTABLEDATAROW_BEGIN","Scalar marker string was interpreted as framing.");
                Refuse(drawing,content,()=>content.ReplaceContent(new string('\\',149797),content.Description,content.TableStyle,Array.Empty<DxfStoredTableContentValueEdit>()));
                Refuse(drawing,content,()=>content.ReplaceContent("\ud800",content.Description,content.TableStyle,Array.Empty<DxfStoredTableContentValueEdit>()));
                RoundTrip(drawing,content,binary,name+"-marker-string");
            });
            Run(name+"-standalone-style-identities",()=>
            {
                var drawing=StyleFixture(version,binary);var content=Content(drawing);var first=(DxfDatabaseObject)drawing.Objects.Root["STYLE_ONE"];var second=(DxfDatabaseObject)drawing.Objects.Root["STYLE_TWO"];
                content.ReplaceContent(content.Name,content.Description,first,Array.Empty<DxfStoredTableContentValueEdit>());var previous=content.References;
                Check(ReferenceEquals(content.TableStyle,first)&&previous.SequenceEqual(new[]{first}),"Standalone style did not bind actual first identity.");
                content.ReplaceContent(content.Name,content.Description,second,Array.Empty<DxfStoredTableContentValueEdit>());
                Check(previous.SequenceEqual(new[]{first})&&ReferenceEquals(content.TableStyle,second)&&content.References.SequenceEqual(new[]{second}),"Standalone style replacement changed old references or retained stale current identity.");
                var foreign=(DxfDatabaseObject)StyleFixture(version,binary).Objects.Root["STYLE_TWO"];Check(foreign.Handle==second.Handle,"Foreign style fixture did not preserve same-handle control.");
                Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,foreign,Array.Empty<DxfStoredTableContentValueEdit>()));
                RoundTrip(drawing,content,binary,name+"-style");
                content.ReplaceContent(content.Name,content.Description,null!,Array.Empty<DxfStoredTableContentValueEdit>());Check(content.References.Count==0&&content.TableStyle==null,"Null standalone style did not release current dependency.");
            });
        }
        string directory=Path.Combine(Path.GetFullPath(args[0]),"tests/fixtures/table-content");using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory,"manifest.json")));
        foreach(var fixture in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string path=Path.GetFullPath(Path.Combine(directory,fixture.GetProperty("fixture").GetString()!));byte[] bytes=File.ReadAllBytes(path);
            if(path.EndsWith(".gz",StringComparison.Ordinal)){using var compressed=new GZipStream(new MemoryStream(bytes),CompressionMode.Decompress);using var expanded=new MemoryStream();compressed.CopyTo(expanded);bytes=expanded.ToArray();}
            string hash=fixture.TryGetProperty("sha256",out var adapted)?adapted.GetString()!:fixture.GetProperty("source_sha256").GetString()!;Check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash,StringComparison.OrdinalIgnoreCase),"Native fixture hash differs.");
            var raw=Raw(bytes);var handles=raw.Sections.SelectMany(s=>s.Records).Where(r=>r.Tags.Any(t=>t.Code==0&&Equals(t.Value,"TABLECONTENT"))).Select(r=>(string)r.Tags.First(t=>t.Code==5).Value).ToArray();
            foreach(bool binary in new[]{false,true})foreach(string handle in handles)Run("native-"+fixture.GetProperty("profile").GetString()+"-"+handle+"-"+binary,()=>
            {
                var drawing=Load(Transport(raw,binary));var content=Content(drawing,handle);Check(content.StoredValues.Count>0,"Native content has no qualified scalar frame.");
                var payload=content.Payload;var values=content.StoredValues;var oldReferences=content.References.ToArray();var other=drawing.Objects.Items.OfType<DxfStoredTableContent>().Where(c=>c!=content).ToDictionary(c=>c.Handle,c=>c.Payload.Select(Value).ToArray());long seed=Seed(drawing);
                content.ReplaceContent(content.Name,content.Description,content.TableStyle,Array.Empty<DxfStoredTableContentValueEdit>());Check(ReferenceEquals(payload,content.Payload)&&ReferenceEquals(values,content.StoredValues),"Native no-op replaced snapshots.");
                var selected=values[0];content.ReplaceContent("Independent native Ω","Explicit description",content.TableStyle,new[]{selected.WithValue(Replacement(selected),Display(selected))});
                Check(Seed(drawing)==seed&&oldReferences.SequenceEqual(content.References)&&values[0]==selected,"Native edit changed handle seed/dependencies/prior snapshot.");
                Refuse(drawing,content,()=>content.ReplaceContent(content.Name,content.Description,null!,Array.Empty<DxfStoredTableContentValueEdit>()));
                foreach(var companion in other)Check(companion.Value.SequenceEqual(Content(drawing,companion.Key).Payload.Select(Value)),"Native edit changed companion content.");
                RoundTrip(drawing,content,binary,"native-"+fixture.GetProperty("profile").GetString()+"-"+handle+"-"+binary);
            });
        }
        File.WriteAllText(Path.Combine(Output,"results.json"),JsonSerializer.Serialize(new{cases=Results.Count,failed=Failed,librarySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(),results=Results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{Results.Count} cases; {Failed} failed.");Environment.ExitCode=Failed==0?0:1;
    }
    private sealed class Callback:IEnumerable<DxfStoredTableContentValueEdit>
    {
        private readonly DxfStoredTableContentValueEdit edit;private readonly Action dispose;internal Callback(DxfStoredTableContentValueEdit edit,Action dispose){this.edit=edit;this.dispose=dispose;}
        public IEnumerator<DxfStoredTableContentValueEdit> GetEnumerator()=>new Cursor(edit,dispose);IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        private sealed class Cursor:IEnumerator<DxfStoredTableContentValueEdit>{private bool moved;private readonly Action dispose;internal Cursor(DxfStoredTableContentValueEdit edit,Action dispose){Current=edit;this.dispose=dispose;}public DxfStoredTableContentValueEdit Current{get;}object IEnumerator.Current=>Current;public bool MoveNext(){if(moved)return false;moved=true;return true;}public void Reset()=>throw new NotSupportedException();public void Dispose()=>dispose();}
    }
}
