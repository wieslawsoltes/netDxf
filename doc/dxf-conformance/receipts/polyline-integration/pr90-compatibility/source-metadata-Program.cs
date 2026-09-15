using netDxf;
using netDxf.Entities;
using netDxf.IO;
using System.Text.Json;
using System.Security.Cryptography;
var rows=new List<object>();
foreach(bool binary in new[]{false,true})foreach(bool duplicateBefore in new[]{false,true})foreach(bool metadata in new[]{false,true}){
 var d=new DxfDocument();var line=new Line(Vector3.Zero,Vector3.UnitX);var reactor=new Circle(Vector3.Zero,2);d.Entities.Add(line);d.Entities.Add(reactor);if(metadata)line.PersistentReactors.Add(reactor);
 using var output=new MemoryStream();if(!d.Save(output,binary))throw new Exception("baseline save failed");output.Position=0;var raw=DxfRawDocument.Load(output);var source=raw.Sections.Single(s=>s.Name=="ENTITIES").Records.Single(r=>r.Name=="LINE");
 var tags=raw.Tags.ToList();int at=duplicateBefore?source.StartTagIndex:source.StartTagIndex+source.Tags.Count;tags.InsertRange(at,new[]{new DxfTag(0,"UNSUPPORTED_CURVE"),new DxfTag(5,line.Handle),new DxfTag(330,line.Owner.Record.Handle),new DxfTag(100,"AcDbEntity"),new DxfTag(8,"0"),new DxfTag(100,"AcDbFutureCurve")});
 using var bytes=new MemoryStream();DxfRawDocument.Create(tags).Save(bytes,binary);File.WriteAllBytes($"input-{binary}-{duplicateBefore}-{metadata}.dxf",bytes.ToArray());bytes.Position=0;bool accepted=false;int? after=null;string? error=null;
 try{var loaded=DxfDocument.Load(bytes);accepted=loaded!=null;after=loaded?.Entities.Lines.Single().PersistentReactors.Count;}catch(Exception e){error=e.GetType().Name+": "+e.Message;}
 rows.Add(new{binary,duplicateBefore,metadata,source=line.Handle,reactor=reactor.Handle,before_reactors=metadata?1:0,accepted,after_reactors=after,error});
}
Console.WriteLine(JsonSerializer.Serialize(new{library_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(),results=rows},new JsonSerializerOptions{WriteIndented=true}));
