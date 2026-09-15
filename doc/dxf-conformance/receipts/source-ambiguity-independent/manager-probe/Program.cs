using System.Collections;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.IO;
using netDxf.Objects;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var results=new List<object>();
Directory.CreateDirectory(args[1]);
DxfRawDocument Native(){using var f=File.OpenRead(args[0]);using var g=new GZipStream(f,CompressionMode.Decompress);using var m=new MemoryStream();g.CopyTo(m);m.Position=0;return DxfRawDocument.Load(m);}
foreach(bool binary in new[]{false,true})foreach(string placement in new[]{"control","before","after","private"}){
 var raw=Native();var record=raw.Sections.Single(s=>s.Name=="ENTITIES").Records.Single(r=>r.Tags.TakeWhile(t=>t.Code!=100).Any(t=>t.Code==5&&Equals(t.Value,"228")));
 if(placement is "before" or "after"){
  var duplicate=new[]{new DxfTag(0,"FUTURE_SECTION_ENTITY"),new DxfTag(5,"000228"),new DxfTag(100,"AcDbEntity"),new DxfTag(8,"0"),new DxfTag(100,"AcDbFutureSectionEntity")};
  int index=record.StartTagIndex+(placement=="after"?record.Tags.Count:0);
  raw=DxfRawDocument.Create(raw.Tags.Take(index).Concat(duplicate).Concat(raw.Tags.Skip(index)));
 }else if(placement=="private"){
  var tags=record.Tags.ToList();int index=tags.FindIndex(t=>t.Code==100);tags.InsertRange(index,new[]{new DxfTag(102,"{PRIVATE"),new DxfTag(5,"228"),new DxfTag(102,"}")});raw=raw.WithRecord(record,tags);
 }
 using var stream=new MemoryStream();raw.Save(stream,binary);File.WriteAllBytes(Path.Combine(args[1],$"manager-{placement}-{binary}.dxf"),stream.ToArray());stream.Position=0;
 bool accepted=false;string? error=null;string? type=null;bool referenceExact=false;
 try{var doc=DxfDocument.Load(stream);accepted=doc!=null;if(doc!=null){var manager=doc.GetObjectByHandle("229");type=manager.GetType().Name;var refs=((IEnumerable)manager.GetType().GetProperty("Sections")!.GetValue(manager)!).Cast<object>().ToArray();referenceExact=refs.Length==1&&ReferenceEquals(refs[0],doc.GetObjectByHandle("228"));}}
 catch(Exception e){error=e.GetType().Name+": "+e.Message;}
 bool expected=placement is "control" or "private";
 results.Add(new{placement,binary,expected_acceptance=expected,accepted,type,referenceExact,error,passed=accepted==expected});
 Console.WriteLine($"{placement}/{binary}: accepted={accepted}, exact-target={referenceExact}, error={error}");
}
File.WriteAllText(Path.Combine(args[1],"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
