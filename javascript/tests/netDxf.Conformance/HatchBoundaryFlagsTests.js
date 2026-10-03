// Port of the pinned C# conformance module; original case identities and assertions retained.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single=items=>{const a=Array.from(items);Equal(1,a.length,'Expected one item');return a[0];};
const artifacts=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
const saveFixture=(name,bytes)=>{fs.mkdirSync(artifacts,{recursive:true});fs.writeFileSync(path.join(artifacts,name),bytes);};
import { DxfDocument, DxfRawDocument, DxfTag, Hatch, HatchBoundaryPath, HatchType, Block, Insert, Polyline2D, Vector2, Vector3, Matrix3, MemoryStream, DxfVersion } from '../../index.js';
import { HatchDoubleTags } from './HatchDoublePatternTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { ClosurePolyline } from './HatchPolylineClosureTests.js';
export function RegisterHatchBoundaryFlagsTests(){
  for(const v of SupportedVersions)for(const b of [false,true]){const suffix=`${VersionName(v)}/${BooleanName(b)}`;
    for(let flags=0;flags<32;flags++)Run(`hatch/path-flags/read-clone-transform/${suffix}/${flags}`,()=>HatchFlagsRoundTrip(v,b,flags));
    Run(`hatch/path-flags/open-polyline/${suffix}`,()=>HatchFlagsOpenPolyline(v,b));Run(`hatch/path-flags/output-corpus/${suffix}`,()=>HatchFlagsCorpus(v,b));Run(`hatch/path-flags/unknown-bits/${suffix}`,()=>HatchFlagsRoundTrip(v,b,0x40000002));
  }
  Run('hatch/path-flags/associative-update',HatchFlagsUpdate);Run('hatch/path-flags/constructor-defaults',HatchFlagsDefaults);
  Run('hatch/path-flags/independent-clone',()=>HatchFlagsIndependent(false));Run('hatch/path-flags/independent-transform',()=>HatchFlagsIndependent(true));
}
export function HatchFlagsTags(version,flags,closed=true){
  const tags=HatchDoubleTags(version,HatchType.UserDefined,0),start=tags.findIndex(t=>t.Code===92),end=tags.findIndex(t=>t.Code===97);
  if(flags&2){tags[start]=new DxfTag(92,flags);tags[tags.findIndex(t=>t.Code===73)]=new DxfTag(73,closed?1:0);}
  else{const edges=[new DxfTag(92,flags),new DxfTag(93,4)],points=[new Vector2(0,0),new Vector2(10,0),new Vector2(10,10),new Vector2(0,10)];
    for(let i=0;i<points.length;i++){const a=points[i],b=points[(i+1)%points.length];edges.push(...[[72,1],[10,a.X],[20,a.Y],[11,b.X],[21,b.Y]].map(([c,v])=>new DxfTag(c,v)));}tags.splice(start,end-start,...edges);
  }return tags;
}
export function ReadFlagsHatch(version,binary,flags,closed=true){const input=new MemoryStream(RawFixtureBytes(HatchFlagsTags(version,flags,closed),binary));try{const doc=DxfDocument.Load(input);Check(doc!==null,'Boundary flag fixture failed to load.');Check(input.CanRead,'Path flags closed caller stream.');return single(doc.Entities.Hatches);}finally{input.Dispose();}}
export function CheckPathFlags(hatch,flags){const path=single(hatch.BoundaryPaths);Equal(flags,path.PathType,'Boundary classification bits were rewritten');Equal(!!(flags&2),path.Edges.Count===1&&path.Edges.get_Item(0) instanceof HatchBoundaryPath.Polyline,'Polyline marker does not describe the actual edge representation');Equal(flags&2?1:4,path.Edges.Count,'Flag preservation changed closed-boundary geometry');}
export function HatchFlagsRoundTrip(version,binary,flags){
  const original=ReadFlagsHatch(version,binary,flags);CheckPathFlags(original,flags);const clone=original.Clone();CheckPathFlags(clone,flags);
  const block=new Block('FlagBlock');block.Entities.Add(clone);const insert=new Insert(block,new Vector3(4,5,0)),copied=insert.Clone();CheckPathFlags(single(Array.from(copied.Block.Entities).filter(e=>e instanceof Hatch)),flags);
  const exploded=single(Array.from(insert.Explode()).filter(e=>e instanceof Hatch));CheckPathFlags(exploded,flags);exploded.TransformBy(Matrix3.Scale(2),new Vector3(1,2,0));CheckPathFlags(exploded,flags);
  let document=new DxfDocument(version);document.Entities.Add(exploded);
  for(let cycle=0;cycle<2;cycle++){const output=new MemoryStream();try{Check(document.Save(output,cycle===0?!binary:binary),'Flagged HATCH save failed.');output.Position=0;const raw=DxfRawDocument.Load(output),record=single(Array.from(single(Array.from(raw.Sections).filter(s=>s.Name==='ENTITIES')).Records).filter(r=>r.Name==='HATCH'));
    Equal(flags,single(Array.from(record.Tags).filter(t=>t.Code===92)).Value,'Writer changed group 92');output.Position=0;document=DxfDocument.Load(output);Check(document!==null,'Flagged HATCH reload failed.');CheckPathFlags(single(document.Entities.Hatches),flags);
  }finally{output.Dispose();}}CheckPathFlags(original,flags);
}
export function HatchFlagsOpenPolyline(version,binary){
  const original=ReadFlagsHatch(version,binary,26,false);Equal(26,single(original.BoundaryPaths).PathType,'Open input flags');const copy=original.Clone();copy.TransformBy(Matrix3.Identity,new Vector3(1,2,0));const path=single(copy.BoundaryPaths),polyline=single(path.Edges);
  Equal(26,path.PathType,'Similarity altered stored classification bits');Equal(4,polyline.Vertexes.length,'Open transformed vertex count');Check(!polyline.IsClosed,'Open transformed boundary acquired a closing segment.');
  const doc=new DxfDocument(version);doc.Entities.Add(copy);const output=new MemoryStream();try{Check(doc.Save(output,!binary),'Transformed edge-path save failed.');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Transformed edge-path reload failed.');Equal(26,single(single(loaded.Entities.Hatches).BoundaryPaths).PathType,'Transformed flags lost on reload');Equal(26,single(original.BoundaryPaths).PathType,'Transform changed source flags');}finally{output.Dispose();}
}
export function HatchFlagsUpdate(){const contour=new Polyline2D([Vector2.Zero,Vector2.UnitX,new Vector2(1,1),Vector2.UnitY],true),path=new HatchBoundaryPath([contour]);path.PathType=19;contour.IsClosed=false;path.Update();Equal(17,path.PathType,'Update retained a stale Polyline flag');Equal(3,path.Edges.Count,'Updated open boundary edge count');contour.IsClosed=true;path.Update();Equal(19,path.PathType,'Update failed to restore the Polyline flag');Equal(1,path.Edges.Count,'Updated closed boundary representation');const clone=path.Clone();Equal(path.PathType,clone.PathType,'Direct path clone classification');Equal(0,clone.Entities.Count,'Path cloning changed existing contour-reference policy');}
export function HatchFlagsIndependent(transform){for(let flags=0;flags<32;flags++){const hatch=ReadFlagsHatch(DxfVersion.AutoCad2018,false,flags);single(hatch.BoundaryPaths).PathType=flags;if(transform){hatch.TransformBy(Matrix3.Identity,Vector3.Zero);CheckPathFlags(hatch,flags);}else{CheckPathFlags(hatch.Clone(),flags);Equal(flags,single(hatch.BoundaryPaths).Clone().PathType,'Direct path clone classification');}}}
export function HatchFlagsDefaults(){Equal(7,new HatchBoundaryPath([ClosurePolyline(true)]).PathType,'New polyline constructor default changed');const line=new HatchBoundaryPath.Line();line.Start=Vector2.Zero;line.End=Vector2.UnitX;Equal(5,new HatchBoundaryPath([line]).PathType,'New edge constructor default changed');}
export function HatchFlagsCorpus(version,binary){const document=new DxfDocument(version);for(let flags=0;flags<32;flags++)document.Entities.Add(ReadFlagsHatch(version,binary,flags).Clone());const output=new MemoryStream();try{Check(document.Save(output,binary),'Flag corpus save failed.');saveFixture(`hatch-path-flags-${VersionName(version)}-${BooleanName(binary)}.dxf`,output.ToArray());}finally{output.Dispose();}}
