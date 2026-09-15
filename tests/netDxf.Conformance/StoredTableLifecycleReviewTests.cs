using System.Reflection;
using System.IO.Compression;
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
    private static bool ReviewTableReject(Action action) { try { action(); return false; } catch (Exception) { return true; } }
    private static List<DxfTag> ReviewTablePayload() => new() { new(100,"AcDbBlockReference"),new(2,"DISPLAY"),new(10,1.0),new(20,2.0),new(30,3.0),
    new(100,"AcDbTable"),new(90,22),new(91,1),new(92,1),new(141,4.0),new(142,5.0),new(171,(short)1),
    new(301,"CELL_VALUE"),new(93,2),new(90,4),new(1,"value"),new(304,"ACVALUE_END") };
    private static DxfDocument ReviewTableLoad(List<DxfTag> payload, bool binary, bool nested = false, DxfVersion version = DxfVersion.AutoCad2018, string targetKind = "xrecord", Action<DxfDocument>? configure = null)
{
    var doc = new DxfDocument(version); doc.Blocks.Add(new Block("DISPLAY"));
    DxfObject target;
    if(targetKind=="appid") target=doc.ApplicationRegistries.Add(new ApplicationRegistry("REVIEW_TARGET"));
    else if(targetKind=="entity") { var line=new Line(Vector3.Zero,Vector3.UnitX);doc.Entities.Add(line);target=line; }
    else if(targetKind=="nested-entity") { var line=new Line(Vector3.Zero,Vector3.UnitX);var block=new Block("TARGET_HOST");block.Entities.Add(line);doc.Blocks.Add(block);target=line; }
    else if(targetKind=="attribute"||targetKind=="nested-attribute")
    {
        var definition=new Block("ATTR_SOURCE");definition.AttributeDefinitions.Add(new AttributeDefinition("KEY"));var insert=new Insert(definition);
        if(targetKind=="attribute")doc.Entities.Add(insert);
        else {var host=new Block("TARGET_HOST");host.Entities.Add(insert);doc.Blocks.Add(host);}
        target=insert.Attributes.Single();
    }
    else if(targetKind=="endblock")
    {
        var host=new Block("TARGET_HOST");doc.Blocks.Add(host);
        target=(DxfObject)typeof(Block).GetProperty("End",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(host)!;
    }
    else { var xrecord=new DxfXRecord();doc.Objects.Root.Add("TARGET",xrecord);target=xrecord; }
    configure?.Invoke(doc);
    var point = new Point(Vector3.Zero);
    if (nested) { var host = new Block("HOST"); host.Entities.Add(point); doc.Blocks.Add(host); }
    else doc.Entities.Add(point);
    using var stream = new MemoryStream(); Check(doc.Save(stream,binary),"setup save"); stream.Position=0;
    var raw = DxfRawDocument.Load(stream); var record = raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="POINT");
    int end = record.Tags.ToList().FindIndex(t=>t.Code==100&&(string)t.Value=="AcDbPoint");
    var prefix=record.Tags.Take(end).Select(t=>t.Code==0?new DxfTag(0,"ACAD_TABLE"):t);
    raw=raw.WithRecord(record,prefix.Concat(payload.Select(t=>t.ValueType==DxfTagValueType.Handle&&(string)t.Value=="ABCDEF"?new DxfTag(t.Code,target.Handle):t)));
    using var input=new MemoryStream();raw.Save(input,binary);input.Position=0;
    return DxfDocument.Load(input)??throw new Exception("load returned null");
}
    private static long ReviewTableSeed(DxfDocument d)=>(long)typeof(DxfDocument).GetProperty("NumHandles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(d)!;
    private static StoredTable ReviewTableTable(DxfDocument d)=>d.Blocks.SelectMany(b=>b.Entities).OfType<StoredTable>().Single();
    private static string ReviewTableSavedName(DxfDocument doc)
{
    using var stream=new MemoryStream();Check(doc.Save(stream),"save failed");stream.Position=0;
    return (string)DxfRawDocument.Load(stream).Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="ACAD_TABLE").Tags.Single(t=>t.Code==2).Value;
}

    private static void RegisterStoredTableLifecycleReviewTests()
    {
        foreach (bool binary in new[] { false, true })
        {
    Run($"stored-table/review/unknown-display-rename/{binary}",()=>{
        var p=ReviewTablePayload();p[p.FindIndex(t=>t.Code==100&&(string)t.Value=="AcDbTable")]=new(100,"PrivateTable");
        var d=ReviewTableLoad(p,binary);Check(ReviewTableTable(d).Grid==null,"private schema projected");d.Blocks["DISPLAY"].Name="RENAMED";
        Check(ReviewTableSavedName(d)=="RENAMED","display name stayed stale");
    });
    foreach(bool foreign in new[]{false,true})
    Run($"stored-table/review/removed-nested-block-add/{binary}/{foreign}",()=>{
        var d=ReviewTableLoad(ReviewTablePayload(),binary,true);var host=d.Blocks["HOST"];var table=ReviewTableTable(d);Check(d.Blocks.Remove(host),"remove host failed");
        var target=foreign?new DxfDocument():d;long seed=ReviewTableSeed(target);int blocks=target.Blocks.Count;string? beforeHandle=host.Handle;
        Check(ReviewTableReject(()=>target.Blocks.Add(host)),"removed child reattached");
        Check(target.Blocks.Count==blocks&&!target.Blocks.Contains("HOST")&&ReviewTableSeed(target)==seed&&host.Handle==beforeHandle,"rejected nested block add partially mutated target");
    });
    Run($"stored-table/review/attached-nested-block-transfer/{binary}",()=>{
        var d=ReviewTableLoad(ReviewTablePayload(),binary,true);var host=d.Blocks["HOST"];var target=new DxfDocument();
        long seed=ReviewTableSeed(target);int blocks=target.Blocks.Count;string oldHandle=host.Handle;var oldOwner=host.Record.Owner;
        Check(ReviewTableReject(()=>target.Blocks.Add(host)),"foreign attached child transferred");
        Check(target.Blocks.Count==blocks&&!target.Blocks.Contains("HOST")&&ReviewTableSeed(target)==seed&&host.Handle==oldHandle&&ReferenceEquals(host.Record.Owner,oldOwner),"rejected attached transfer partially mutated state");
    });
    Run($"stored-table/review/duplicate-literal-envelope/{binary}",()=>{
        var p=ReviewTablePayload();p.AddRange(new DxfTag[]{new(301,"CELL_VALUE"),new(93,2),new(90,4),new(1,"contradiction"),new(304,"ACVALUE_END")});
        try {var d=ReviewTableLoad(p,binary);Check(!ReviewTableTable(d).Grid![0,0].HasLiteralValue,"ambiguous duplicate CELL_VALUE projected as literal");}
        catch(InvalidDataException){}
    });
    foreach(string operation in new[]{"insert","container","sync","endblock"})
    Run($"stored-table/review/owned-member-removal/{binary}/{operation}",()=>{
        string kind=operation=="container"?"nested-attribute":operation=="endblock"?"endblock":"attribute";
        var p=ReviewTablePayload();p.Add(new DxfTag(340,"ABCDEF"));var d=ReviewTableLoad(p,binary,targetKind:kind);var table=ReviewTableTable(d);long seed=ReviewTableSeed(d);
        if(operation=="endblock")Check(!d.Blocks.Remove("TARGET_HOST"),"referenced ENDBLK removed");
        else
        {
            var insert=operation=="container"?d.Blocks["TARGET_HOST"].Entities.OfType<Insert>().Single():d.Entities.Inserts.Single();
            int callbacks = 0; insert.AttributeRemoved += (_, __) => callbacks++;
            var attribute=insert.Attributes.Single();Check(table.References.Contains(attribute),"attribute handle unresolved");string handle=attribute.Handle;
            if(operation=="insert")Check(!d.Entities.Remove(insert),"referenced attribute removed through INSERT");
            else if(operation=="container")Check(!d.Blocks.Remove("TARGET_HOST"),"referenced attribute removed through containing block");
            else {Check(insert.Block.AttributeDefinitions.Remove("KEY"),"unreferenced definition removal failed");Check(ReviewTableReject(()=>insert.Sync()),"referenced attribute removed through Sync");}
            Check(insert.Attributes.Count==1&&ReferenceEquals(insert.Attributes.Single(),attribute)&&attribute.Handle==handle,"rejected owned removal changed attribute");
            Check(callbacks == 0, "rejected owned removal invoked attribute callbacks");
        }
        Check(ReviewTableSeed(d)==seed,"rejected owned removal changed seed");
    });
    foreach(bool removed in new[]{false,true})
    Run($"stored-table/review/nested-block-adoption/{binary}/{removed}",()=>{
        var source=ReviewTableLoad(ReviewTablePayload(),binary,true);var host=source.Blocks["HOST"];
        if(removed)Check(source.Blocks.Remove(host),"remove source block failed");
        var outer=new Block("OUTER");outer.Entities.Add(new Insert(host));var target=new DxfDocument();int count=target.Blocks.Count;long seed=ReviewTableSeed(target);string? old=host.Handle;
        Check(ReviewTableReject(()=>target.Blocks.Add(outer)),"nested foreign stored TABLE adopted");
        Check(target.Blocks.Count==count&&ReviewTableSeed(target)==seed&&outer.Handle==null&&host.Handle==old,"rejected nested adoption changed graph");
    });
        }
    foreach(bool binary in new[]{false,true})
    foreach(string mutation in new[]{"none","different-literal","unknown-value-type","duplicate-backing-value"})
    Run($"stored-table/review/backing-comparison/{binary}/{mutation}",()=>{
        using var file=File.OpenRead(Path.Combine("tests", "fixtures", "table-oracle", "acad_table_simple.dxf.gz"));using var gzip=new GZipStream(file,CompressionMode.Decompress);using var bytes=new MemoryStream();gzip.CopyTo(bytes);bytes.Position=0;
        var raw=DxfRawDocument.Load(bytes);
        if(mutation=="unknown-value-type")
        {
            var entity=raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="ACAD_TABLE");var p=entity.Tags.ToList();int marker=p.FindIndex(t=>t.Code==301&&(string)t.Value=="CELL_VALUE");
            p[p.FindIndex(marker+1,t=>t.Code==90)]=new DxfTag(90,2048);raw=raw.WithRecord(entity,p);
        }
        if(mutation!="none")
        {
            var backing=raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="TABLECONTENT");var p=backing.Tags.ToList();int marker=p.FindIndex(t=>t.Code==300&&(string)t.Value=="VALUE");
            if(mutation=="different-literal")p[p.FindIndex(marker+1,t=>t.Code==1)]=new DxfTag(1,"different literal");
            else if(mutation=="unknown-value-type")p[p.FindIndex(marker+1,t=>t.Code==90)]=new DxfTag(90,2048);
            else {int end=p.FindIndex(marker+1,t=>t.Code==304&&(string)t.Value=="ACVALUE_END");p.InsertRange(end+1,p.GetRange(marker,end-marker+1));}
            raw=raw.WithRecord(backing,p);
        }
        using var input=new MemoryStream();raw.Save(input,binary);input.Position=0;var doc=DxfDocument.Load(input)??throw new Exception("native load rejected");var table=ReviewTableTable(doc);
        Check(table.BackingContent!=null,"native backing unbound");bool? expected=mutation=="none"?true:mutation=="different-literal"?false:null;
        Check(table.BackingLiteralValuesAgree==expected,$"backing agreement expected {expected?.ToString()??"null"}, got {table.BackingLiteralValuesAgree?.ToString()??"null"}");
    });
    }
}
