using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
var rows = new List<object>();
long Seed(DxfDocument doc) => (long)typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!;
Polyline3D ForeignPolyline() {
 var source=new DxfDocument(DxfVersion.AutoCad2018);source.Entities.Add(new Polyline3D(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY}));
 using var stream=new MemoryStream();source.Save(stream);stream.Position=0;source=DxfDocument.Load(stream);var p=source.Entities.Polylines3D.Single();
 if(!source.Entities.Remove(p))throw new Exception("fixture detach refused");return p;
}
foreach(bool attached in new[]{false,true}){
 var foreign=ForeignPolyline();var target=new DxfDocument(DxfVersion.AutoCad2018);var line=new Line(Vector3.Zero,Vector3.UnitX);
 var hatch=new Hatch(HatchPattern.Solid,true);if(attached)target.Entities.Add(hatch);
 var path=new HatchBoundaryPath(new EntityObject[]{line,foreign});if(!attached)hatch.BoundaryPaths.Add(path);
 long beforeSeed=Seed(target);int before=target.Entities.All.Count();int paths=hatch.BoundaryPaths.Count;int lineReactors=line.Reactors.Count;bool rejected=false;string? error=null;
 try{if(attached)hatch.BoundaryPaths.Add(path);else target.Entities.Add(hatch);}catch(Exception e){rejected=true;error=e.GetType().Name+": "+e.Message;}
 rows.Add(new{scenario=attached?"attached-path-foreign-source":"hatch-attachment-foreign-source",rejected,error,before_entities=before,after_entities=target.Entities.All.Count(),before_seed=beforeSeed,after_seed=Seed(target),before_paths=paths,after_paths=hatch.BoundaryPaths.Count,first_source_owner=line.Owner?.Name,first_source_handle=line.Handle,foreign_owner=foreign.Owner?.Name,before_first_source_reactors=lineReactors,after_first_source_reactors=line.Reactors.Count,hatch_owner=hatch.Owner?.Name});
}
{
 var source=new Line(Vector3.Zero,Vector3.UnitX);var path=new HatchBoundaryPath(new EntityObject[]{source});
 Hatch? first=null,second=null;bool rejected=false;string? error=null;
 try{first=new Hatch(HatchPattern.Solid,new[]{path},true);second=new Hatch(HatchPattern.Solid,new[]{path},true);first.UnLinkBoundary();}catch(Exception e){rejected=true;error=e.GetType().Name+": "+e.Message;}
 rows.Add(new{scenario="shared-path-instance-unlink",rejected,error,second_associative=second?.Associative,second_sources=second?.BoundaryPaths.Single().Entities.Count,source_reactors=source.Reactors.Count,source_references_second=source.Reactors.Contains(second)});
}
{
 var source=new Line(Vector3.Zero,Vector3.UnitX);var path=new HatchBoundaryPath(new EntityObject[]{source});
 Hatch? hatch=null;int before=source.Reactors.Count;bool rejected=false;string? error=null;
 try{hatch=new Hatch(HatchPattern.Solid,new[]{path,path},true);before=source.Reactors.Count;hatch.UnLinkBoundary();}catch(Exception e){rejected=true;error=e.GetType().Name+": "+e.Message;}
 rows.Add(new{scenario="repeated-path-instance-unlink",rejected,error,before_reactors=before,after_reactors=source.Reactors.Count,remaining_sources=hatch?.BoundaryPaths.Sum(p=>p.Entities.Count),associative=hatch?.Associative});
}
foreach(bool binary in new[]{false,true}) foreach(int variant in new[]{0,1,2}) {
 var boundary=new Line(Vector3.Zero,Vector3.UnitX);var hatch=new Hatch(HatchPattern.Solid,new[]{new HatchBoundaryPath(new EntityObject[]{boundary})},true);var source=new DxfDocument();source.Entities.Add(hatch);
 using var output=new MemoryStream();source.Save(output,binary);output.Position=0;var raw=DxfRawDocument.Load(output);var record=raw.Sections.Single(v=>v.Name=="ENTITIES").Records.Single(v=>v.Name=="LINE");var tags=raw.Tags.ToList();
 if(variant==0)tags.InsertRange(record.StartTagIndex,new[]{new DxfTag(0,"UNSUPPORTED_BOUNDARY"),new DxfTag(5,boundary.Handle),new DxfTag(330,boundary.Owner.Record.Handle),new DxfTag(100,"AcDbEntity"),new DxfTag(8,"0"),new DxfTag(100,"PrivateBoundary")});
 else {int pos=record.StartTagIndex+record.Tags.ToList().FindIndex(t=>t.Code==5);tags.Insert(pos,new DxfTag(5,variant==1?"000"+boundary.Handle:"EEEEEE"));}
 using var mutated=new MemoryStream();DxfRawDocument.Create(tags).Save(mutated,binary);mutated.Position=0;bool accepted=false;string? error=null;string[]? refs=null;
 try{var loaded=DxfDocument.Load(mutated);accepted=loaded!=null;refs=loaded?.Entities.Hatches.Single().BoundaryPaths.Single().Entities.Select(e=>e.Handle).ToArray();}catch(Exception e){error=e.GetType().Name+": "+e.Message;}
 rows.Add(new{scenario=variant==0?"discarded-plus-accepted-physical-duplicate":variant==1?"duplicate-equal-common-source-identity":"duplicate-different-common-source-identity",binary,accepted,error,refs});
}
foreach(bool binary in new[]{false,true}) foreach(bool remove in new[]{false,true}) {
 var boundary=new Line(Vector3.Zero,Vector3.UnitX);var hatch=new Hatch(HatchPattern.Solid,new[]{new HatchBoundaryPath(new EntityObject[]{boundary})},true);var source=new DxfDocument();source.Entities.Add(hatch);
 using var bytes=new MemoryStream();source.Save(bytes,binary);bytes.Position=0;source=DxfDocument.Load(bytes);hatch=source.Entities.Hatches.Single();boundary=source.Entities.Lines.Single();
 int beforeAutomatic=boundary.Reactors.Count,beforePersistent=boundary.PersistentReactors.Count;bool? removed=null;bool saved=false;string? error=null;
 try{if(remove)removed=source.Entities.Remove(hatch);else hatch.UnLinkBoundary();using var output=new MemoryStream();saved=source.Save(output,binary);}catch(Exception e){error=e.GetType().Name+": "+e.Message;}
 rows.Add(new{scenario=remove?"loaded-hatch-removal":"loaded-hatch-unlink",binary,removed,saved,error,before_automatic=beforeAutomatic,before_persistent=beforePersistent,after_automatic=boundary.Reactors.Count,after_persistent=boundary.PersistentReactors.Count,persistent_target_still_registered=boundary.PersistentReactors.All(r=>ReferenceEquals(source.GetObjectByHandle(r.Handle),r))});
}
foreach(bool firstAssociative in new[]{false,true}) {
 var boundary=new Line(Vector3.Zero,Vector3.UnitX);var path=new HatchBoundaryPath(new EntityObject[]{boundary});var first=new Hatch(HatchPattern.Solid,new[]{path},firstAssociative);int beforeSources=path.Entities.Count,beforeReactors=boundary.Reactors.Count;bool rejected=false;string? error=null;
 try{_ = new Hatch(HatchPattern.Solid,new[]{path},!firstAssociative);}catch(Exception e){rejected=true;error=e.GetType().Name+": "+e.Message;}
 rows.Add(new{scenario="constructor-shared-path-preflight",first_associative=firstAssociative,rejected,error,before_sources=beforeSources,after_sources=path.Entities.Count,before_reactors=beforeReactors,after_reactors=boundary.Reactors.Count});
}
Console.WriteLine(JsonSerializer.Serialize(new{library_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(),results=rows},new JsonSerializerOptions{WriteIndented=true}));
