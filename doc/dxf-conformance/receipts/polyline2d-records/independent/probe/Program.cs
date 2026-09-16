using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

internal static partial class Program
{
    static string Fixtures="", Output="";
    static readonly List<object> Results=new(); static int Failed;
    static void Check(bool pass,string message){if(!pass)throw new Exception(message);}
    static void Near(double a,double b,string message){Check(Math.Abs(a-b)<=1e-9*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b))),message+": "+a+" != "+b);}
    static void Run(string name,Action action){try{action();Results.Add(new{name,passed=true,error=""});Console.WriteLine("PASS "+name);}catch(Exception e){Failed++;Results.Add(new{name,passed=false,error=e.ToString()});Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static long Seed(DxfDocument d)=>(long)typeof(DxfDocument).GetProperty("NumHandles",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(d)!;
    static DxfDocument Load(byte[] data){using var s=new MemoryStream(data);return DxfDocument.Load(s)??throw new Exception("Typed load failed");}
    static DxfRawDocument Raw(byte[] data){using var s=new MemoryStream(data);return DxfRawDocument.Load(s);}
    static byte[] Bytes(DxfRawDocument raw,bool binary){using var s=new MemoryStream();raw.Save(s,binary);return s.ToArray();}
    static byte[] Save(DxfDocument d,bool binary,string? file=null){using var s=new MemoryStream();Check(d.Save(s,binary),"Save failed");var b=s.ToArray();if(file!=null)File.WriteAllBytes(Path.Combine(Output,file),b);return b;}
    static string Handle(DxfRawRecord r)=>(string)r.Tags.First(t=>t.Code==5).Value;
    static string OwnerHandle(DxfRawRecord record){int depth=0;foreach(var tag in record.Tags){if(tag.Code==100)break;if(tag.Code==102){string value=(string)tag.Value;depth+=value.StartsWith("{",StringComparison.Ordinal)?1:-1;}if(tag.Code==330&&depth==0)return (string)tag.Value;}throw new Exception("No qualified source owner");}
    static DxfRawRecord Record(DxfRawDocument raw,string handle)=>raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Tags.Any(t=>t.Code==5)&&Handle(r)==handle);
    static DxfTag[] Header(DxfRawRecord r)=>r.Tags.SkipWhile(t=>t.Code!=100||!Equals(t.Value,"AcDb2dPolyline")).ToArray();
    static bool Same(IEnumerable<DxfTag> left,IEnumerable<DxfTag> right){var a=left.ToArray();var b=right.ToArray();return a.Length==b.Length&&a.Zip(b).All(p=>p.First.Code==p.Second.Code&&(p.First.Value is byte[] bytes?bytes.SequenceEqual((byte[])p.Second.Value):Equals(p.First.Value,p.Second.Value)));}
    static double? Optional(IEnumerable<DxfTag> tags,short code){var t=tags.FirstOrDefault(t=>t.Code==code);return t==null?null:(double)t.Value;}
    static string State(Polyline2D p)=>JsonSerializer.Serialize(new{p.Elevation,p.Thickness,normal=new[]{p.Normal.X,p.Normal.Y,p.Normal.Z},p.LegacyDefaultStartWidth,p.LegacyDefaultEndWidth,p.IsClosed,p.ConstantWidth,p.SmoothType,vertices=p.Vertexes.Select(v=>new{v.Position.X,v.Position.Y,v.Bulge,v.StartWidthOverride,v.EndWidthOverride,v.VertexIdentifier}),records=p.VertexRecords.Select(r=>r.Handle),end=p.EndSequenceRecord?.Handle});
    static DxfRawDocument Edit(DxfRawDocument raw,string handle,Action<List<DxfTag>> edit){var r=Record(raw,handle);var p=r.Tags.ToList();edit(p);return DxfRawDocument.Create(raw.Tags.Take(r.StartTagIndex).Concat(p).Concat(raw.Tags.Skip(r.EndTagIndex)));}
    static DxfRawDocument Field(DxfRawDocument raw,string handle,short code,object? value){return Edit(raw,handle,tags=>{int i=tags.FindIndex(t=>t.Code==code);if(i>=0){if(value==null)tags.RemoveAt(i);else tags[i]=new DxfTag(code,value);}else if(value!=null){i=tags.FindIndex(t=>t.Code==1001);tags.Insert(i<0?tags.Count:i,new DxfTag(code,value));}});}
    static Polyline2D Poly(DxfDocument d,string h)=>d.GetObjectByHandle(h) as Polyline2D??throw new Exception("Missing polyline "+h);
    static void Refuse(DxfDocument d,bool binary,string name){long seed=Seed(d);var reg=d.Blocks.SelectMany(b=>b.Entities).ToArray();byte[] sentinel={7,13,19};using var s=new MemoryStream();s.Write(sentinel);s.Position=1;bool saved;try{saved=d.Save(s,binary);}catch(Exception){saved=false;}Check(!saved&&s.Position==1&&s.ToArray().SequenceEqual(sentinel)&&Seed(d)==seed&&reg.SequenceEqual(d.Blocks.SelectMany(b=>b.Entities)),"Preflight mutated stream/seed/collection: "+name);}
    static void Main(string[] args)
    {
        Fixtures=Path.GetFullPath(args[0]);Output=Path.GetFullPath(args[1]);Directory.CreateDirectory(Output);
        var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures,"manifest.json"))).RootElement;
        foreach(var f in manifest.GetProperty("fixtures").EnumerateArray())
        {
            string file=f.GetProperty("file").GetString()!;bool binary=f.GetProperty("binary").GetBoolean();byte[] data=File.ReadAllBytes(Path.Combine(Fixtures,file));
            Check(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()==f.GetProperty("sha256").GetString(),"Fixture hash mismatch");
            string label=file.Replace(".dxf","");string parent=f.GetProperty("handles").GetProperty("polyline").GetString()!,plain=f.GetProperty("handles").GetProperty("plain_polyline").GetString()!;
            Run(label+"/source-packets",()=>Packets(data,binary,label));
            Run(label+"/width-presence",()=>Widths(data,plain,binary,label));
            Run(label+"/reverse",()=>Reverse(data,parent,binary,label));
            Run(label+"/transform",()=>Transform(data,parent,binary,label));
            Run(label+"/lifecycle",()=>Lifecycle(data,plain,binary,label));
            Run(label+"/common-child-guard",()=>ChildGuard(data,plain,binary,label));
            Run(label+"/nonzero-child-z",()=>RejectZ(data,plain,binary));
            foreach(int count in new[]{0,1}) Run(label+"/degenerate/"+count,()=>Degenerate(data,plain,binary,label,count));
            foreach(int mask in Enumerable.Range(0,7)) Run(label+"/dummy-point/"+mask,()=>DummyPoint(data,plain,binary,label,mask));
        }
        foreach(bool binary in new[]{false,true})foreach(string handle in new[]{"1EF","1FF"})
            Run("native/"+handle+"/"+binary,()=>Native(binary,handle));
        Extra();
        File.WriteAllText(Path.Combine(Output,"results.json"),JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Independent legacy2D: {Results.Count-Failed}/{Results.Count} passed");Environment.ExitCode=Failed==0?0:1;
    }
    static void Packets(byte[] data,bool binary,string label)
    {
        var raw=Raw(data);var d=Load(data);var parents=d.Entities.Polylines2D.ToArray();Check(parents.Length==2,"Expected two physical chains");
        foreach(var p in parents){Check(p.CodeName=="POLYLINE"&&p.VertexRecords.Count==p.Vertexes.Count&&p.EndSequenceRecord!=null,"Retained chain missing");for(int i=0;i<p.Vertexes.Count;i++){var r=p.VertexRecords[i];Check(ReferenceEquals(r.Vertex,p.Vertexes[i])&&ReferenceEquals(d.GetObjectByHandle(r.Handle),r),"Point/physical record identity mismatch");var physical=Record(raw,r.Handle);string owner=OwnerHandle(physical);Check(r.StoredOwner.Handle==owner&&r.UsesBlockRecordOwner==(owner==p.Owner.Record.Handle),"Stored owner form changed");}}
        foreach(bool output in new[]{false,true})
        {
            var bytes=Save(d,output,label+"-packet-"+output+".dxf");var written=Raw(bytes);var reload=Load(bytes);
            foreach(var p in parents){Check(Same(Header(Record(raw,p.Handle)),Header(Record(written,p.Handle))),"Parent subclass packet changed");foreach(var r in p.VertexRecords.Concat(new[]{p.EndSequenceRecord})){Check(Same(Record(raw,r.Handle).Tags,Record(written,r.Handle).Tags),"Complete child packet changed: "+r.Handle);Check(reload.GetObjectByHandle(r.Handle) is Polyline2DRecord,"Reload lost physical child");}}
        }
    }
    static void Widths(byte[] data,string parent,bool binary,string label)
    {
        var raw=Raw(data);var baseDoc=Load(data);var p=Poly(baseDoc,parent);string first=p.VertexRecords[0].Handle;
        raw=Field(raw,first,40,null);raw=Field(raw,first,41,null);raw=Field(raw,parent,40,2.0);raw=Field(raw,parent,41,3.0);
        foreach(int variant in new[]{0,1,2,3})
        {
            var current=raw;if((variant&1)!=0)current=Field(current,first,40,0.0);if((variant&2)!=0)current=Field(current,first,41,0.0);
            var d=Load(Bytes(current,binary));var line=Poly(d,parent);var v=line.Vertexes[0];
            Check(v.StartWidthOverride==((variant&1)!=0?0.0:(double?)null)&&v.EndWidthOverride==((variant&2)!=0?0.0:(double?)null),"Presence collapsed explicit zero");Near((variant&1)!=0?0:2,line.GetEffectiveStartWidth(0),"Effective start width");Near((variant&2)!=0?0:3,line.GetEffectiveEndWidth(0),"Effective end width");
            var written=Raw(Save(d,!binary,label+"-width-"+variant+".dxf"));Check(Optional(Record(written,first).Tags,40)==v.StartWidthOverride&&Optional(Record(written,first).Tags,41)==v.EndWidthOverride,"Output width presence changed");
            line.Reverse();int edge=line.Vertexes.Count-2;Near((variant&2)!=0?0:3,line.GetEffectiveStartWidth(edge),"Reversed explicit-zero start");Near((variant&1)!=0?0:2,line.GetEffectiveEndWidth(edge),"Reversed explicit-zero end");
            line.TransformBy(new Matrix3(2,0,0,0,2,0,0,0,2),Vector3.Zero);Near((variant&2)!=0?0:6,line.GetEffectiveStartWidth(edge),"Scaled explicit-zero start");Near((variant&1)!=0?0:4,line.GetEffectiveEndWidth(edge),"Scaled explicit-zero end");Save(d,binary,label+"-width-reverse-scale-"+variant+".dxf");
        }
    }
    static void Reverse(byte[] data,string parent,bool binary,string label)
    {
        foreach(bool closed in new[]{false,true})
        {
            var d=Load(data);var p=Poly(d,parent);p.IsClosed=closed;var models=p.Vertexes.ToArray();var ids=p.VertexRecords.ToArray();var positions=models.Select(v=>v.Position).ToArray();var bulges=models.Select(v=>v.Bulge).ToArray();var starts=models.Select(v=>v.StartWidthOverride).ToArray();var ends=models.Select(v=>v.EndWidthOverride).ToArray();var effectiveStarts=Enumerable.Range(0,models.Length).Select(p.GetEffectiveStartWidth).ToArray();var effectiveEnds=Enumerable.Range(0,models.Length).Select(p.GetEffectiveEndWidth).ToArray();string state=State(p);long seed=Seed(d);
            p.Reverse();int n=models.Length;
            for(int i=0;i<n;i++){int old=n-1-i,edge=i<n-1?n-2-i:n-1;Check(ReferenceEquals(models[old],p.Vertexes[i])&&ReferenceEquals(ids[old],p.VertexRecords[i])&&ReferenceEquals(d.GetObjectByHandle(ids[old].Handle),ids[old]),"Reverse lost point/record identity");Check(p.Vertexes[i].Position==positions[old],"Reverse position changed");Near(-bulges[edge],p.Vertexes[i].Bulge,"Outgoing reverse bulge");Check(p.Vertexes[i].StartWidthOverride==ends[edge]&&p.Vertexes[i].EndWidthOverride==starts[edge],"Outgoing nullable width role");Near(effectiveEnds[edge],p.GetEffectiveStartWidth(i),"Reverse inherited start");Near(effectiveStarts[edge],p.GetEffectiveEndWidth(i),"Reverse inherited end");}
            Check(Seed(d)==seed,"Reverse assigned handles");Save(d,!binary,label+"-reverse-"+closed+".dxf");p.Reverse();Check(State(p)==state,"Two reversals did not restore representation");
        }
    }
    static void Transform(byte[] data,string parent,bool binary,string label)
    {
        var d=Load(data);var p=Poly(d,parent);var ids=p.VertexRecords.ToArray();var old=p.Vertexes.Select(v=>(v.Position,v.Bulge,v.StartWidthOverride,v.EndWidthOverride)).ToArray();double elevation=p.Elevation,thickness=p.Thickness;double? a=p.LegacyDefaultStartWidth,b=p.LegacyDefaultEndWidth;
        Check(p.Normal==Vector3.UnitZ,"Independent transform fixture requires UnitZ source");p.TransformBy(new Matrix3(2,0,0,0,2,0,0,0,2),new Vector3(10,-4,5));
        for(int i=0;i<old.Length;i++){Near(old[i].Position.X*2+10,p.Vertexes[i].Position.X,"Scaled X");Near(old[i].Position.Y*2-4,p.Vertexes[i].Position.Y,"Scaled Y");Check(p.Vertexes[i].Bulge==old[i].Bulge&&p.Vertexes[i].StartWidthOverride==old[i].StartWidthOverride*2&&p.Vertexes[i].EndWidthOverride==old[i].EndWidthOverride*2&&ReferenceEquals(ids[i],p.VertexRecords[i]),"Transform altered segment presence/identity");}
        Near(elevation*2+5,p.Elevation,"Scaled elevation");Near(thickness*2,p.Thickness,"Scaled thickness");Check(p.LegacyDefaultStartWidth==a*2&&p.LegacyDefaultEndWidth==b*2,"Inherited widths not scaled");Save(d,!binary,label+"-transform.dxf");
        foreach(Action op in new Action[]{()=>p.TransformBy(new Matrix3(2,0,0,0,1,0,0,0,1),Vector3.Zero),()=>p.TransformBy(Matrix3.Identity,new Vector3(double.PositiveInfinity,0,0)),()=>p.TransformBy(new Matrix3(-1,0,0,0,1,0,0,0,1),Vector3.Zero)}){string state=State(p);long seed=Seed(d);bool rejected=false;try{op();}catch(Exception){rejected=true;}Check(rejected&&State(p)==state&&Seed(d)==seed,"Unsupported transform mutated geometry");}
    }
    static void Lifecycle(byte[] data,string parent,bool binary,string label)
    {
        var d=Load(data);var p=Poly(d,parent);var ids=p.VertexRecords.Concat(new[]{p.EndSequenceRecord}).ToArray();var clone=(Polyline2D)p.Clone();Check(clone.VertexRecords.Count==p.VertexRecords.Count&&clone.VertexRecords.Zip(p.VertexRecords).All(pair=>!ReferenceEquals(pair.First,pair.Second)&&!ReferenceEquals(pair.First.Vertex,pair.Second.Vertex)),"Clone shared child/model instances");d.Entities.Add(clone);Check(clone.VertexRecords.All(r=>ids.All(old=>r.Handle!=old.Handle)&&ReferenceEquals(d.GetObjectByHandle(r.Handle),r)),"Clone physical IDs not allocated independently");
        var sourceVersion=d.DrawingVariables.AcadVer;d.DrawingVariables.AcadVer=sourceVersion==DxfVersion.AutoCad2000?DxfVersion.AutoCad2018:DxfVersion.AutoCad2000;Refuse(d,binary,"source profile");d.DrawingVariables.AcadVer=sourceVersion;
        Check(d.Entities.Remove(p),"Clean retained chain detach failed");Check(ids.All(r=>d.GetObjectByHandle(r.Handle)==null&&ReferenceEquals(r.Owner,p)),"Detached children remained registered or lost structural parent");var foreign=new DxfDocument(sourceVersion);long seed=Seed(foreign);bool rejected=false;try{foreign.Entities.Add(p);}catch(Exception){rejected=true;}Check(rejected&&Seed(foreign)==seed&&!foreign.Entities.All.Any(),"Foreign source adoption mutated destination");d.Entities.Add(p);Check(ids.All(r=>ReferenceEquals(d.GetObjectByHandle(r.Handle),r)),"Same-document readoption lost source identity");Save(d,!binary,label+"-lifecycle.dxf");
    }
    static void ChildGuard(byte[] data,string parent,bool binary,string label)
    {
        var d=Load(data);var p=Poly(d,parent);var child=p.VertexRecords[0];var target=new Line(Vector3.Zero,Vector3.UnitX);d.Entities.Add(target);child.PersistentReactors.Add(target);Check(!d.Entities.Remove(target),"Retained2D child reactor did not protect ordinary target");child.PersistentReactors.Remove(target);
        var app=d.ApplicationRegistries.Add(new ApplicationRegistry("INDEPENDENT_CHILD_LINK"));var x=new XData(app);var link=new XDataRecord(XDataCode.DatabaseHandle,target.Handle);x.XDataRecord.Add(link);child.XData.Add(x);Check(!d.Entities.Remove(target),"Retained2D child XData1005 did not protect ordinary target");Save(d,!binary,label+"-child-link.dxf");x.XDataRecord.Remove(link);Check(d.Entities.Remove(target),"Explicit child-link release failed");
    }
    static void RejectZ(byte[] data,string parent,bool binary)
    {
        var d=Load(data);string child=Poly(d,parent).VertexRecords[0].Handle;foreach(double z in new[]{0.125,-1.0}){var raw=Field(Raw(data),child,30,z);byte[] input=Bytes(raw,binary);bool rejected=false;try{rejected=DxfDocument.Load(new MemoryStream(input))==null;}catch(Exception){rejected=true;}Check(rejected,"Nonzero/nonfinite child Z was silently projected");}
    }
    static void Native(bool binary,string handle)
    {
        byte[] data=File.ReadAllBytes(Path.Combine(Fixtures,"native-R2000.dxf"));var raw=Raw(data);data=Bytes(raw,binary);var d=Load(data);var p=Poly(d,handle);double width=handle=="1EF"?0.15:0.5;Check(p.LegacyDefaultStartWidth==width&&p.LegacyDefaultEndWidth==width,"Native header width defaults missing");Check(p.IsClosed==(handle=="1FF"),"Native omitted/open or closed flag changed");foreach(var v in p.Vertexes)Check(v.StartWidthOverride==null&&v.EndWidthOverride==null,"Native missing width overridden");for(int i=0;i<p.Vertexes.Count;i++){Near(width,p.GetEffectiveStartWidth(i),"Native effective start");Near(width,p.GetEffectiveEndWidth(i),"Native effective end");}
        foreach(bool output in new[]{false,true}){var written=Raw(Save(d,output,"native-"+handle+"-"+binary+"-"+output+".dxf"));Check(Same(Header(Record(raw,handle)),Header(Record(written,handle))),"Native parent subclass changed");foreach(var r in p.VertexRecords.Concat(new[]{p.EndSequenceRecord}))Check(Same(Record(raw,r.Handle).Tags,Record(written,r.Handle).Tags),"Native child packet changed");}
    }
}
