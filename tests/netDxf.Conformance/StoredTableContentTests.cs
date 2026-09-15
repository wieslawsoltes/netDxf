using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly string[] TableContentFiles = { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf", "sample_AC1018_ascii.dxf", "sample_AC1021_ascii.dxf", "sample_AC1024_ascii.dxf" };
    private static void RegisterStoredTableContentTests()
    {
        foreach (string file in TableContentFiles) foreach (bool inputBinary in new[] { false, true }) foreach (bool binary in new[] { false, true })
            Run($"table-content/native/{file}/{inputBinary}/{binary}", () => TableContentNative(file, inputBinary, binary));
        foreach (bool binary in new[] { false, true })
        {
            foreach (int fault in Enumerable.Range(0, 19)) Run($"table-content/malformed/{binary}/{fault}", () => TableContentMalformed(binary, fault));
            foreach (int variant in Enumerable.Range(0, 8)) Run($"table-content/opaque/{binary}/{variant}", () => TableContentOpaque(binary, variant));
            foreach (int operation in Enumerable.Range(0, 9)) Run($"table-content/lifecycle/{binary}/{operation}", () => TableContentLifecycle(binary, operation));
            foreach (string kind in new[] { "STYLE", "LTYPE", "BLOCK_RECORD", "ENTITY", "BLOCK_MEMBER", "APPID" })
                Run($"table-content/dependency/{binary}/{kind}", () => TableContentDependency(binary, kind));
            foreach (string literal in new[] { "TABLEFORMAT_BEGIN", "TABLEFORMAT_END", "LINKEDTABLEDATACOLUMN_BEGIN", "LINKEDTABLEDATAROW_BEGIN", "CELLCONTENT_BEGIN", "DATAMAP_BEGIN", @"Literal\U+0041" })
                Run($"table-content/literal-marker/{binary}/{literal}", () => TableContentLiteral(binary, literal));
            foreach (string literal in new[] { "TABLEFORMAT_BEGIN", "DATAMAP_BEGIN", "Private value", @"Literal\U+0041" })
            {
                Run($"table-content/unqualified-map/{binary}/{literal}", () => TableContentUnqualifiedMap(binary,literal));
                Run($"table-content/map-value/{binary}/{literal}", () => TableContentUnqualifiedMap(binary,literal,true));
            }
        }
    }
    // Reusable complete source carrier for composite ownership and mixed-family qualification.
    private static byte[] TableContentSourceBytes(string file)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/table-content/manifest.json"));
        var fixture = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("file").GetString() == file);
        string path = Path.Combine("tests/fixtures/table-content", fixture.GetProperty("fixture").GetString()!);
        byte[] bytes;
        if (path.EndsWith(".gz", StringComparison.Ordinal))
        { using var source = File.OpenRead(path); using var gzip = new GZipStream(source, CompressionMode.Decompress); using var output = new MemoryStream(); gzip.CopyTo(output); bytes = output.ToArray(); }
        else bytes = File.ReadAllBytes(path);
        string expected = fixture.TryGetProperty("sha256", out var hash) ? hash.GetString()! : fixture.GetProperty("source_sha256").GetString()!;
        Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "TABLECONTENT pinned source/carrier hash");
        return bytes;
    }
    private static DxfDocument TableContentLoad(byte[] bytes)
    { using var input = new MemoryStream(bytes); return DxfDocument.Load(input) ?? throw new Exception("TABLECONTENT load failed"); }
    private static byte[] TableContentSave(DxfDocument document, bool binary, string? name = null)
    {
        using var output = new MemoryStream(); Check(document.Save(output, binary), "TABLECONTENT save failed");
        byte[] bytes = output.ToArray(); if (name != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, name), bytes); return bytes;
    }
    private static byte[] TableContentRawBytes(DxfRawDocument raw, bool binary)
    { using var output = new MemoryStream(); if (binary) raw = DxfRawDocument.Create(raw.Tags.Where(t => t.Code != 999)); raw.Save(output, binary); return output.ToArray(); }
    private static DxfRawDocument TableContentRaw(byte[] bytes)
    { using var input = new MemoryStream(bytes); return DxfRawDocument.Load(input); }
    private static void TableContentNative(string file, bool inputBinary, bool binary)
    {
        byte[] bytes = TableContentSourceBytes(file);
        if (inputBinary) bytes = TableContentRawBytes(TableContentRaw(bytes), true);
        var doc = TableContentLoad(bytes); var contents = doc.Objects.Items.OfType<DxfStoredTableContent>().ToArray();
        Equal(file.StartsWith("sample_", StringComparison.Ordinal) ? 2 : 1, contents.Length, "typed native content count");
        foreach (var content in contents)
        {
            Equal(doc.DrawingVariables.AcadVer, content.SourceVersion, "native source profile");
            Equal(4, content.Subclasses.Count, "complete subclass hierarchy");
            Check(content.ColumnCount is 3 or 5 && content.RowCount is 3 or 4 or 7 or 10, "independent outer dimensions");
            Equal(string.Empty, content.Name, "native linked name"); Equal(string.Empty, content.Description, "native linked description");
            Check(content.TableStyle != null && content.TableStyle.CodeName == "TABLESTYLE" && ReferenceEquals(doc.GetObjectByHandle(content.TableStyle.Handle), content.TableStyle), "exact native table style");
            foreach (DxfObject target in content.References) Check(ReferenceEquals(doc.GetObjectByHandle(target.Handle), target), "exact native dependency identity");
        }
        Equal(0, doc.Objects.Validate().Count, "source object graph validation");
        var before = contents.ToDictionary(c => c.Handle, c => OwnershipTagValues(c.Payload).ToArray());
        var reloaded = TableContentLoad(TableContentSave(doc, binary, $"table-content-native-{file}-{binary}.dxf"));
        foreach (var content in reloaded.Objects.Items.OfType<DxfStoredTableContent>())
            Check(before[content.Handle].SequenceEqual(OwnershipTagValues(content.Payload)), "complete native content packet changed");
        Equal(0, reloaded.Objects.Validate().Count, "reloaded object graph validation");
    }
    private static List<DxfTag> TableContentMinimalPayload() => new() {
        new(100,"AcDbLinkedData"),new(1,"Stored name"),new(300,"Stored description"),
        new(100,"AcDbLinkedTableData"),new(90,0),new(91,0),new(92,0),
        new(100,"AcDbFormattedTableData"),new(100,"AcDbTableContent"),new(340,"0") };
    private static DxfRawDocument TableContentMinimal(DxfVersion version, Action<DxfDocument, List<DxfTag>>? setup = null)
    {
        var doc = new DxfDocument(version); var placeholder = new DxfXRecord(); doc.Objects.Root.Add("CONTENT", placeholder);
        var body = TableContentMinimalPayload(); setup?.Invoke(doc, body);
        var raw = TableContentRaw(TableContentSave(doc, false));
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == placeholder.Handle));
        int marker = record.Tags.ToList().FindIndex(t => t.Code == 100);
        return raw.WithRecord(record, record.Tags.Take(marker).Select(t => t.Code == 0 ? new DxfTag(0,"TABLECONTENT") : t).Concat(body));
    }
    private static void TableContentMalformed(bool binary, int fault)
    {
        var raw = TableContentRaw(TableContentSourceBytes("acad_table_simple.dxf"));
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "TABLECONTENT"); var tags = record.Tags.ToList();
        int start = tags.FindIndex(t => t.Code == 100); var markers = tags.Select((t,i) => new {t,i}).Where(p => p.t.Code == 100).Select(p => p.i).ToArray();
        if (fault < 4) tags.RemoveAt(markers[fault]);
        else if (fault == 4) tags.Insert(start, tags[start]);
        else if (fault == 5) { var tag = tags[markers[1]]; tags[markers[1]] = tags[markers[2]]; tags[markers[2]] = tag; }
        else if (fault == 6) tags.RemoveAt(tags.Count - 1);
        else if (fault == 7) tags.Add(new DxfTag(340,"0"));
        else if (fault == 8) tags[^1] = new DxfTag(340,"FFFFFFFF");
        else if (fault == 9) tags[^1] = new DxfTag(340,"11");
        else if (fault == 10) tags[markers[1] + 1] = new DxfTag(90,4);
        else if (fault == 11) tags[markers[1] + 1] = new DxfTag(90,-1);
        else if (fault == 12) tags.RemoveAt(tags.FindIndex(t => t.Code == 309 && (string)t.Value == "TABLEFORMAT_END"));
        else if (fault == 13) tags.RemoveAt(tags.FindIndex(t => t.Code == 304 && (string)t.Value == "ACVALUE_END"));
        else if (fault == 14) tags.Insert(start + 1, new DxfTag(1000,"stray public XData"));
        else if (fault == 15) tags.Insert(tags.FindIndex(t => t.Code == 5) + 1, new DxfTag(5,"FFFF"));
        else if (fault == 16)
        {
            string owner = (string)tags.Take(start).Last(t => t.Code == 330).Value;
            var parent = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == owner));
            var parentTags = parent.Tags.ToList(); int index = parentTags.FindLastIndex(parentTags.FindIndex(t => t.Code == 100) - 1, t => t.Code == 330);
            parentTags[index] = new DxfTag(330,owner); raw = raw.WithRecord(parent,parentTags);
            record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "TABLECONTENT");
        }
        else if (fault == 17) tags.RemoveAt(tags.FindIndex(t => t.Code==309&&(string)t.Value=="DATAMAP_END"));
        else
        {
            int map=tags.FindIndex(t => t.Code==1&&(string)t.Value=="DATAMAP_BEGIN"); tags[map+1]=new DxfTag(90,1);
            tags.InsertRange(map+2,new[]{new DxfTag(300,"Stored map name"),new DxfTag(301,"DATAMAP_VALUE"),new DxfTag(93,2),new DxfTag(90,2),new DxfTag(140,1.0),new DxfTag(94,0),new DxfTag(300,""),new DxfTag(302,"")});
        }
        raw = raw.WithRecord(record,tags); bool rejected = false;
        try { using var input = new MemoryStream(TableContentRawBytes(raw,binary)); rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; }
        Check(rejected,"malformed known TABLECONTENT was accepted or made opaque");
    }
    private static void TableContentOpaque(bool binary, int variant)
    {
        var raw = TableContentMinimal(variant == 3 ? DxfVersion.AutoCad2000 : DxfVersion.AutoCad2018);
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "TABLECONTENT"); var tags = record.Tags.ToList(); int first = tags.FindIndex(t => t.Code == 100);
        if (variant == 0) tags[first] = new DxfTag(100,"PrivateLinkedData");
        else if (variant == 1) tags.Add(new DxfTag(100,"PrivateContent"));
        else if (variant == 2) tags.InsertRange(first,new[]{new DxfTag(102,"{PRIVATE"),new DxfTag(1000,"header private"),new DxfTag(102,"}")});
        else if (variant == 4) tags.InsertRange(first+1,new[]{new DxfTag(102,"{PRIVATE"),new DxfTag(1000,"payload private"),new DxfTag(102,"}")});
        else if (variant == 5) tags.AddRange(new[]{new DxfTag(100,"PrivateContent"),new DxfTag(1000,"subclass private")});
        else if (variant == 6) tags.Insert(first,new DxfTag(300,"private common value"));
        else if (variant == 7) tags.AddRange(new[]{new DxfTag(100,"PrivateContent"),new DxfTag(340,"EEEEEEEE")});
        raw = raw.WithRecord(record,tags); var doc = TableContentLoad(TableContentRawBytes(raw,binary)); var opaque = doc.Objects.Items.OfType<DxfOpaqueObject>().Single(o => o.CodeName == "TABLECONTENT");
        var before = OwnershipTagValues(opaque.Tags).ToArray(); var loaded = TableContentLoad(TableContentSave(doc,binary,$"table-content-opaque-{variant}-{binary}.dxf"));
        Check(before.SequenceEqual(OwnershipTagValues(loaded.Objects.Items.OfType<DxfOpaqueObject>().Single(o => o.CodeName == "TABLECONTENT").Tags)),"whole opaque packet changed");
    }
    private static void TableContentLifecycle(bool binary, int operation)
    {
        var doc = TableContentLoad(TableContentSourceBytes("acad_table_simple.dxf")); var content = doc.Objects.Items.OfType<DxfStoredTableContent>().Single(); int count = doc.Objects.Items.Count();
        if (operation == 0) Throws<NotSupportedException>(() => doc.Objects.CloneObject(content,doc.Objects.Root,"COPY"));
        else if (operation == 1) Throws<NotSupportedException>(() => doc.Objects.CloneObject((DxfDatabaseObject)content.Owner,doc.Objects.Root,"COPY"));
        else if (operation == 2) Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(content));
        else if (operation == 3) Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree((DxfDatabaseObject)content.Owner));
        else if (operation == 4) Check(!doc.Entities.Remove(doc.Entities.StoredTables.Single()),"owning TABLE removed");
        else if (operation == 5)
        {
            var contentOnly = TableContentLoad(TableContentRawBytes(TableContentMinimal(DxfVersion.AutoCad2018),binary));
            contentOnly.DrawingVariables.AcadVer = DxfVersion.AutoCad2013; using var output = new MemoryStream();
            bool rejected = false; try { rejected = !contentOnly.Save(output,binary); } catch (NotSupportedException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            Check(rejected,"TABLECONTENT profile conversion accepted"); Equal(0L,output.Length,"profile rejection wrote bytes");
        }
        else if (operation == 6) { var list = (IList<DxfTag>)content.Payload; Throws<NotSupportedException>(() => list.Clear()); Check(typeof(DxfStoredTableContent).GetConstructors().Length==0,"authored constructor unexpectedly exposed"); }
        else if (operation == 7)
        {
            doc.Classes.Remove("TABLECONTENT"); var loaded = TableContentLoad(TableContentSave(doc,binary)); Equal(1152,loaded.Classes["TABLECONTENT"].ProxyFlags,"native CLASS synthesis flags");
        }
        else { doc.Classes.Remove("TABLECONTENT"); doc.Classes.Add(new DxfClass("TABLECONTENT","PrivateClass","Private")); using var output = new MemoryStream(); CheckSaveRejected(doc,output); Equal(0L,output.Length,"CLASS rejection wrote bytes"); }
        Equal(count,doc.Objects.Items.Count(),"rejected lifecycle changed object count");
    }
    private static void TableContentDependency(bool binary, string kind)
    {
        string targetHandle = string.Empty; string blockName = "CONTENT_RESOURCE_BLOCK";
        var raw = TableContentMinimal(DxfVersion.AutoCad2018,(doc,tags) =>
        {
            DxfObject target;
            if (kind == "STYLE") target = doc.TextStyles.Add(new TextStyle("CONTENT_RESOURCE_STYLE","Arial.ttf"));
            else if (kind == "LTYPE") target = doc.Linetypes.Add(new Linetype("CONTENT_RESOURCE_LTYPE"));
            else if (kind == "APPID") target = doc.ApplicationRegistries.Add(new ApplicationRegistry("CONTENT_RESOURCE_APP"));
            else if (kind == "ENTITY") { var entity = new Line(Vector3.Zero,Vector3.UnitX); doc.Entities.Add(entity); target = entity; }
            else { var block = new Block(blockName); var line = new Line(Vector3.Zero,Vector3.UnitX); block.Entities.Add(line); doc.Blocks.Add(block); target = kind == "BLOCK_RECORD" ? block.Record : line; }
            targetHandle = target.Handle; tags.Insert(3,new DxfTag(340,target.Handle));
        });
        var doc = TableContentLoad(TableContentRawBytes(raw,binary)); var content = doc.Objects.Items.OfType<DxfStoredTableContent>().Single(); var target = doc.GetObjectByHandle(targetHandle);
        Check(content.References.Any(item => ReferenceEquals(item,target)),"synthetic semantic identity not bound");
        bool removed = kind switch { "STYLE" => doc.TextStyles.Remove("CONTENT_RESOURCE_STYLE"),"LTYPE" => doc.Linetypes.Remove("CONTENT_RESOURCE_LTYPE"),"APPID" => doc.ApplicationRegistries.Remove("CONTENT_RESOURCE_APP"),"ENTITY" => doc.Entities.Remove((EntityObject)target),_ => doc.Blocks.Remove(blockName) };
        Check(!removed,"TABLECONTENT dependency removed"); Equal(0,doc.Objects.Validate().Count,"resource guard changed graph");
    }
    private static void TableContentLiteral(bool binary, string literal)
    {
        var raw = TableContentRaw(TableContentSourceBytes("acad_table_simple.dxf")); var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "TABLECONTENT"); var tags = record.Tags.ToList();
        int value = tags.FindIndex(t => t.Code == 300 && (string)t.Value == "VALUE"); int text = tags.FindIndex(value+1,t => t.Code == 1); tags[text] = new DxfTag(1,literal);
        var doc = TableContentLoad(TableContentRawBytes(raw.WithRecord(record,tags),binary)); var content = doc.Objects.Items.OfType<DxfStoredTableContent>().Single(); Equal((int?)3,content.ColumnCount,"literal misread as structural marker"); Equal((int?)3,content.RowCount,"literal changed row count");
        var loaded = TableContentLoad(TableContentSave(doc,binary)); Check(loaded.Objects.Items.OfType<DxfStoredTableContent>().Single().Payload.Any(t => t.Code==1&&(string)t.Value==literal),"stored literal changed");
    }
    private static void TableContentUnqualifiedMap(bool binary, string literal, bool qualified = false)
    {
        var raw = TableContentRaw(TableContentSourceBytes("acad_table_simple.dxf"));
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "TABLECONTENT"); var tags = record.Tags.ToList();
        int map = tags.FindIndex(t => t.Code==1&&(string)t.Value=="DATAMAP_BEGIN");
        tags[map+1]=new DxfTag(90,1);
        if (qualified) tags.InsertRange(map+2,new[]{new DxfTag(300,"Stored map name"),new DxfTag(301,"DATAMAP_VALUE"),new DxfTag(93,6),new DxfTag(90,4),new DxfTag(1,literal),new DxfTag(94,0),new DxfTag(300,""),new DxfTag(302,literal),new DxfTag(304,"ACVALUE_END")});
        else tags.Insert(map+2,new DxfTag(1,literal));
        var doc = TableContentLoad(TableContentRawBytes(raw.WithRecord(record,tags),binary)); var content = doc.Objects.Items.OfType<DxfStoredTableContent>().Single();
        int? expected=qualified?3:null; Equal(expected,content.ColumnCount,"custom-map column projection"); Equal(expected,content.RowCount,"custom-map row projection");
        var before=OwnershipTagValues(content.Payload).ToArray();
        var loaded=TableContentLoad(TableContentSave(doc,binary)).Objects.Items.OfType<DxfStoredTableContent>().Single();
        Check(loaded.ColumnCount==expected&&loaded.RowCount==expected&&before.SequenceEqual(OwnershipTagValues(loaded.Payload)),"custom map packet changed");
    }
}
