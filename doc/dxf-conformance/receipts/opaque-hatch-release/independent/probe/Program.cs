using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf; using netDxf.Blocks; using netDxf.Entities; using netDxf.IO;

internal static class Probe
{
    private static readonly string[] Operations = { "unlink", "clear", "replace", "insert", "create-false", "create-true", "transform", "remove-hatch", "remove-block", "update" };
    private static string output = "";
    private static int Main(string[] args)
    {
        output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var results=new List<object>();int failures=0;
        foreach(bool binary in new[]{false,true}) foreach(bool physical in new[]{false,true}) foreach(string op in Operations) foreach(bool corrupt in new[]{false,true})
        {
            string name=$"{op}-{binary}-{physical}-{corrupt}";try {var outcome=Run(name,op,binary,physical,corrupt);results.Add(new{name,passed=true,outcome});}catch(Exception e){failures++;results.Add(new{name,passed=false,error=e.ToString()});}
        }
        var dll=typeof(Hatch).Assembly.Location;var report=new{dll_sha256=Hash(File.ReadAllBytes(dll)),passed=results.Count-failures,failed=failures,scope="Explicitly authored controlled HATCH graph; renamed source is opaque storage, without native geometry claims",results};
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{results.Count-failures} passed / {failures} failed");return failures==0?0:1;
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Check(bool b,string m){if(!b)throw new Exception(m);}
    private static byte[] Save(DxfDocument d,bool binary){using var m=new MemoryStream();Check(d.Save(m,binary),"Save returned false");return m.ToArray();}
    private static DxfDocument Load(byte[] bytes){using var m=new MemoryStream(bytes);return DxfDocument.Load(m)??throw new Exception("Load returned null");}
    private sealed record Graph(DxfDocument Document,Block Block,Hatch Hatch,Hatch Other,DxfOpaqueEntity Source,Line Sentinel,string SourceHash);
    private static Graph Make(bool binary,bool physical,bool named)
    {
        var d=new DxfDocument();var block=named?new Block("OPAQUE_HATCH_BLOCK"):d.Layouts[netDxf.Objects.Layout.ModelSpaceName].AssociatedBlock;if(named)d.Blocks.Add(block);
        var sentinel=new Line(new Vector3(70,80,0),new Vector3(90,80,0));block.Entities.Add(sentinel);
        var circle=new Circle(Vector2.Zero,4);var typed=new Circle(new Vector2(10,10),2);
        var h=new Hatch(HatchPattern.Solid,new[]{new HatchBoundaryPath(new[]{typed}),new HatchBoundaryPath(new EntityObject[]{circle,circle}),new HatchBoundaryPath(new[]{circle})},true);
        var other=new Hatch(HatchPattern.Solid,new[]{new HatchBoundaryPath(new[]{circle})},true);block.Entities.Add(h);block.Entities.Add(other);
        var bytes=Save(d,binary);using var m=new MemoryStream(bytes);var raw=DxfRawDocument.Load(m);var record=raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Tags.Any(t=>t.Code==5&&Equals(t.Value,circle.Handle)));var tags=record.Tags.ToList();tags[0]=new DxfTag(0,"OPAQUE_HATCH_TEST");
        if(!physical)
        {
            int start=tags.FindIndex(t=>t.Code==102&&Equals(t.Value,"{ACAD_REACTORS"));if(start>=0){int end=start+1;while(!(tags[end].Code==102&&Equals(tags[end].Value,"}")))end++;tags.RemoveRange(start,end-start+1);}
        }
        tags.Add(new DxfTag(340,sentinel.Handle));raw=raw.WithRecord(record,tags);using var edited=new MemoryStream();raw.Save(edited,binary);d=Load(edited.ToArray());
        var source=(DxfOpaqueEntity)d.GetObjectByHandle(circle.Handle);return new Graph(d,source.Owner,(Hatch)d.GetObjectByHandle(h.Handle),(Hatch)d.GetObjectByHandle(other.Handle),source,(Line)d.GetObjectByHandle(sentinel.Handle),Hash(JsonSerializer.SerializeToUtf8Bytes(source.SourceTags)));
    }
    private static string Snapshot(Graph g)
    {
        var entities=g.Document.Blocks.SelectMany(b=>b.Entities).Distinct().OrderBy(e=>e.Handle).Select(e=>new{reference=RuntimeHelpers.GetHashCode(e),e.Handle,owner=e.Owner?.Name,reactors=e.Reactors.Select(r=>r.Handle).ToArray(),persistent=e.PersistentReactors.Select(r=>r.Handle).ToArray()}).ToArray();
        var hatches=new[]{g.Hatch,g.Other}.Select(h=>new{h.Handle,owner=h.Owner?.Name,h.Associative,h.Normal,h.Elevation,paths=h.BoundaryPaths.Select(p=>new{reference=RuntimeHelpers.GetHashCode(p),flags=(int)p.PathType,sources=p.Entities.Select(e=>e.Handle).ToArray(),edges=p.Edges.Select(e=>JsonSerializer.Serialize(e,e.GetType())).ToArray()}).ToArray()}).ToArray();
        var seed=(long)typeof(DxfDocument).GetProperty("NumHandles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g.Document)!;
        return JsonSerializer.Serialize(new{seed,entities,hatches,blocks=g.Document.Blocks.Select(b=>new{b.Name,b.Handle,record=b.Record.Handle}).ToArray()});
    }
    private static object Run(string name,string op,bool binary,bool physical,bool corrupt)
    {
        var g=Make(binary,physical,op=="remove-block");var h=g.Hatch;int callbacks=0;h.HatchBoundaryPathAdded+=(_,_)=>callbacks++;h.HatchBoundaryPathRemoved+=(_,_)=>callbacks++;
        if(corrupt)g.Source.PersistentReactors.Add(g.Sentinel);string before=Snapshot(g);bool? removed=null;Exception? failure=null;
        try
        {
            switch(op)
            {
                case "unlink":h.UnLinkBoundary();break;
                case "clear":h.BoundaryPaths.Clear();break;
                case "replace":h.BoundaryPaths[1]=new HatchBoundaryPath(new[]{new Circle(new Vector2(20,20),2)});break;
                case "insert":h.BoundaryPaths.Insert(1,new HatchBoundaryPath(new[]{new Circle(new Vector2(20,20),2)}));break;
                case "create-false":h.CreateBoundary(false);break;
                case "create-true":h.CreateBoundary(true);break;
                case "transform":h.TransformBy(new Matrix3(2,0,0,0,1,0,0,0,1),new Vector3(3,4,0));break;
                case "remove-hatch":removed=g.Block.Entities.Remove(h);break;
                case "remove-block":removed=g.Document.Blocks.Remove(g.Block);break;
                case "update":h.BoundaryPaths[1].Update();break;
            }
        }catch(Exception e){failure=e;}
        string after=Snapshot(g);Check(Hash(JsonSerializer.SerializeToUtf8Bytes(g.Source.SourceTags))==g.SourceHash,"Immutable opaque SourceTags changed");
        if(corrupt&&op!="insert"||op=="update")
        {
            Check(failure!=null||removed==false,"Unsupported operation did not refuse before mutation");Check(before==after&&callbacks==0,"Rejected operation partially changed paths, source relationships, membership, callbacks or seed");
        }
        else
        {
            Check(failure==null,"Supported operation threw: "+failure);
            if(removed==false)Check(before==after&&callbacks==0,"Refused removal changed state");
            if(op=="insert")Check(h.BoundaryPaths.Count==4&&g.Source.Reactors.Count==4,"Insertion released an existing opaque-source occurrence");
        }
        if(corrupt)g.Source.PersistentReactors.Remove(g.Sentinel);
        if(op=="clear"&&!corrupt)Check(g.Block.Entities.Remove(h),"Remove intentionally emptied HATCH before typed export");
        if(physical&&ReferenceEquals(g.Document.GetObjectByHandle(g.Source.Handle),g.Source))
            Check(g.Source.PersistentReactors.OfType<Hatch>().ToHashSet().SetEquals(g.Source.Reactors.OfType<Hatch>()),"Qualified persistent HATCH backlinks differ from surviving managed associations");
        byte[] bytes=Save(g.Document,binary);File.WriteAllBytes(Path.Combine(output,name+".dxf"),bytes);var loaded=Load(bytes);Check(loaded.Objects.Validate().Count==0,"Reloaded database invalid");
        foreach(var source in loaded.Blocks.SelectMany(b=>b.Entities).OfType<DxfOpaqueEntity>())
        {
            Check(source.SourceTags.Where(t=>t.Code==340).Select(t=>t.Value.ToString()).SequenceEqual(g.Source.SourceTags.Where(t=>t.Code==340).Select(t=>t.Value.ToString())),"Actual output private standard pointer changed during qualified release");
            var occurrences=loaded.Blocks.SelectMany(b=>b.Entities).OfType<Hatch>().SelectMany(x=>x.BoundaryPaths.SelectMany(p=>p.Entities).Where(e=>ReferenceEquals(e,source)).Select(_=>x)).ToArray();
            Check(source.Reactors.Count==occurrences.Length,"Reloaded managed occurrence count differs from actual HATCH sources");
            Check(occurrences.All(x=>ReferenceEquals(x.Owner,source.Owner)),"Reloaded HATCH source owner changed");
        }
        return new{removed,exception=failure?.GetType().Name,callbacks,source_live=g.Source.Owner!=null,source_managed=g.Source.Reactors.Count,source_persistent=g.Source.PersistentReactors.Count,output_sha256=Hash(bytes)};
    }
}
