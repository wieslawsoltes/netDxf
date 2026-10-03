// Port of pinned HatchSourceRelationTests.cs; complete original identities/assertions.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { DxfDocument,DxfRawDocument,DxfTag,DxfVersion,MemoryStream,Hatch,HatchPattern,HatchBoundaryPath,Block,Layout,Circle,Line,Vector2 } from '../../index.js';
import { ArgumentException,InvalidDataException,NotSupportedException,FormatException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run,Check,Equal,Throws,SupportedVersions,VersionName,BooleanName } from './TestHarness.js';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..'),artifacts=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
const single=items=>{const a=Array.from(items);Equal(1,a.length,'Expected one item');return a[0];};
const records=raw=>Array.from(raw.Sections).flatMap(s=>Array.from(s.Records));
const sourceList=h=>Array.from(h.BoundaryPaths).flatMap(p=>Array.from(p.Entities));
const tagsOf=pairs=>pairs.map(([c,v])=>new DxfTag(c,v));
export function RegisterHatchSourceRelationTests(){
  for(const v of SupportedVersions)for(const input of [false,true])for(const kind of ['native','producer']){
    const suffix=`${kind}/${VersionName(v)}/${BooleanName(input)}`;
    for(const output of [false,true])Run(`hatch-source/retained/${suffix}/${BooleanName(output)}`,()=>HatchSourceRetained(kind,v,input,output));
    Run(`hatch-source/normalized/${suffix}`,()=>HatchSourceNormalized(kind,v,input));
    for(const defect of ['missing','zero','wrong-owner','self','unsupported-proxy','unsupported-aggregate','missing-identity','private-identity','nonassociative','short-count','long-count','duplicate-physical','duplicate-common-equal','duplicate-common-different'])
      Run(`hatch-source/reject/${suffix}/${defect}`,()=>HatchSourceReject(kind,v,input,defect));
  }
  for(const b of [false,true])for(let placement=0;placement<3;placement++)Run(`hatch-source/shared/${BooleanName(b)}/${placement}`,()=>HatchSourceShared(b,placement));
  for(let scenario=0;scenario<15;scenario++)Run(`hatch-source/api/${scenario}`,()=>HatchSourceApi(scenario));
}
export function HatchSourceInput(kind,version,binary){
  const name=`${kind}-R${VersionName(version).slice(7)}-${binary?'binary':'ascii'}.dxf`,directory=path.join(root,'tests/fixtures/hatch-source-relations'),input=fs.readFileSync(path.join(directory,name));
  const manifest=JSON.parse(fs.readFileSync(path.join(directory,'manifest.json'),'utf8')),item=single(manifest.fixtures.filter(f=>f.file===name));
  Equal(item.sha256,crypto.createHash('sha256').update(input).digest('hex'),'Pinned HATCH producer/native fixture');
  Equal(binary,input.subarray(0,18).equals(Buffer.from('AutoCAD Binary DXF')),'Actual HATCH source input transport');return input;
}
export function HatchSourceLoad(bytes){const stream=new MemoryStream(bytes);try{const doc=DxfDocument.Load(stream);Check(doc!==null,'HATCH source load returned null');return doc;}finally{stream.Dispose();}}
export function HatchSourceSave(doc,binary,artifact=null){const stream=new MemoryStream();try{Check(doc.Save(stream,binary),'HATCH source save');const bytes=stream.ToArray();Equal(binary,Buffer.from(bytes).subarray(0,18).equals(Buffer.from('AutoCAD Binary DXF')),'Actual HATCH source output transport');if(artifact!==null){fs.mkdirSync(artifacts,{recursive:true});fs.writeFileSync(path.join(artifacts,artifact),bytes);}return bytes;}finally{stream.Dispose();}}
export function HatchSourceRaw(bytes){const stream=new MemoryStream(bytes);try{return DxfRawDocument.Load(stream);}finally{stream.Dispose();}}
export function HatchSourceRawBytes(raw,binary){const stream=new MemoryStream();try{raw.Save(stream,binary);return stream.ToArray();}finally{stream.Dispose();}}
export function HatchSourceSnapshot(doc){return Array.from(doc.Entities.Hatches,h=>`${h.Handle}:${BooleanName(h.Associative)}:${Array.from(h.BoundaryPaths,p=>`${p.PathType}:${p.Edges.Count}:${Array.from(p.Entities,e=>e.Handle).join(',')}`).join('/')}`).join('|');}
export function HatchSourceRetained(kind,version,inputBinary,outputBinary){
  let doc=HatchSourceLoad(HatchSourceInput(kind,version,inputBinary));const expected=HatchSourceSnapshot(doc);
  Equal(kind==='native'?1:10,Array.from(doc.Entities.Hatches).reduce((n,h)=>n+sourceList(h).length,0),'Source counts and duplicates');
  for(const hatch of doc.Entities.Hatches)for(const source of sourceList(hatch)){Check(source.Owner===hatch.Owner&&doc.GetObjectByHandle(source.Handle)===source,'Source identity and containing block');Check(!doc.Entities.Remove(source),'Live HATCH source removal must be refused');}
  for(let cycle=0;cycle<3;cycle++){doc=HatchSourceLoad(HatchSourceSave(doc,cycle===1?!outputBinary:outputBinary,cycle===2?`hatch-source-${kind}-${VersionName(version)}-${BooleanName(inputBinary)}-${BooleanName(outputBinary)}.dxf`:null));Equal(expected,HatchSourceSnapshot(doc),'Stored source order/count/identity and associativity');Equal(0,doc.Objects.Validate().Count,'HATCH source graph validation');}
}
export function HatchSourceRecord(raw,handle){return single(records(raw).filter(r=>Array.from(r.Tags).some(t=>t.Code===5&&t.Value===handle)));}
export function HatchSourceNormalized(kind,version,binary){
  const input=HatchSourceInput(kind,version,binary);let raw=HatchSourceRaw(input);
  for(const record of records(raw).filter(r=>r.Name==='HATCH')){
    const tags=Array.from(record.Tags),start=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbHatch');
    for(let i=start;i<tags.length;i++)if(tags[i].Code===330)tags[i]=new DxfTag(330,'000'+tags[i].Value.toLowerCase());
    const current=HatchSourceRecord(raw,Array.from(record.Tags).find(t=>t.Code===5).Value);raw=raw.WithRecord(current,tags);
  }
  Equal(HatchSourceSnapshot(HatchSourceLoad(input)),HatchSourceSnapshot(HatchSourceLoad(HatchSourceRawBytes(raw,binary))),'Numeric source identity ignores spelling');
  const source=HatchSourceRecord(raw,kind==='native'?'8E':'3A0'),tags=Array.from(source.Tags);tags.splice(tags.findIndex(t=>t.Code===100),0,...tagsOf([[102,'{PRIVATE'],[102,'{NESTED'],[102,'}'],[5,'FFFF'],[102,'}']]));raw=raw.WithRecord(source,tags);
  Equal(HatchSourceSnapshot(HatchSourceLoad(input)),HatchSourceSnapshot(HatchSourceLoad(HatchSourceRawBytes(raw,binary))),'Nested private handle does not replace physical source identity');
}
export function HatchSourceReject(kind,version,binary,defect){
  let raw=HatchSourceRaw(HatchSourceInput(kind,version,binary));const hatch=records(raw).find(r=>r.Name==='HATCH'),tags=Array.from(hatch.Tags),start=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbHatch'),reference=tags.findIndex((t,i)=>i>=start&&t.Code===330),target=tags[reference].Value;
  if(defect.startsWith('duplicate-')){
    const source=HatchSourceRecord(raw,target),sourceTags=Array.from(source.Tags);
    if(defect==='duplicate-physical'){
      const all=Array.from(raw.Tags),owner=HatchSourceLoad(HatchSourceInput(kind,version,binary)).GetObjectByHandle(target).Owner.Record.Handle;
      raw=DxfRawDocument.Create([...all.slice(0,source.StartTagIndex),...tagsOf([[0,'FUTURE_BOUNDARY_CURVE'],[5,target],[330,owner],[100,'AcDbEntity'],[8,'0'],[100,'AcDbFutureBoundaryCurve']]),...all.slice(source.StartTagIndex)]);
    }else{sourceTags.splice(sourceTags.findIndex(t=>t.Code===5),0,new DxfTag(5,defect==='duplicate-common-equal'?target:'FFFF'));raw=raw.WithRecord(source,sourceTags);}
  }else if(['unsupported-proxy','unsupported-aggregate','missing-identity','private-identity'].includes(defect)){
    const source=HatchSourceRecord(raw,target),sourceTags=Array.from(source.Tags);
    if(defect==='unsupported-proxy')sourceTags[0]=new DxfTag(0,'ACAD_PROXY_ENTITY');
    else if(defect==='unsupported-aggregate'){sourceTags[0]=new DxfTag(0,'FUTURE_BOUNDARY_CURVE');const xdata=sourceTags.findIndex(t=>t.Code===1001);sourceTags.splice(xdata<0?sourceTags.length:xdata,0,new DxfTag(66,1));}
    else{sourceTags.splice(sourceTags.findIndex(t=>t.Code===5),1);if(defect==='private-identity')sourceTags.splice(1,0,...tagsOf([[102,'{PRIVATE'],[5,target],[102,'}']]));}
    raw=raw.WithRecord(source,sourceTags);
  }else{
    switch(defect){
      case 'missing':tags[reference]=new DxfTag(330,'FFFF');break;case 'zero':tags[reference]=new DxfTag(330,'000');break;
      case 'wrong-owner':{const foreign=Array.from(single(Array.from(raw.Sections).filter(s=>s.Name==='BLOCKS')).Records).find(r=>r.Name==='LINE');tags[reference]=new DxfTag(330,Array.from(foreign.Tags).find(t=>t.Code===5).Value);break;}
      case 'self':tags[reference]=new DxfTag(330,tags.find(t=>t.Code===5).Value);break;
      case 'nonassociative':{const at=tags.findIndex((t,i)=>i>=start&&t.Code===71);tags[at]=new DxfTag(71,0);break;}
      case 'short-count':case 'long-count':{const at=tags.findIndex((t,i)=>i>=start&&t.Code===97);tags[at]=new DxfTag(97,defect==='short-count'?0:tags[at].Value+1);break;}
    }
    raw=raw.WithRecord(hatch,tags);
  }
  const stream=new MemoryStream(HatchSourceRawBytes(raw,binary));try{
    if(GetTypedIOConfiguration()==='Debug'){
      let rejected=false;try{DxfDocument.Load(stream);}catch(error){
        if(error instanceof InvalidDataException)rejected=error.message.includes('HATCH')||(['missing-identity','private-identity'].includes(defect)&&error.message.includes('source handle identity'));
        else if(error instanceof NotSupportedException)rejected=defect==='unsupported-proxy'&&error.message==='Unsupported standalone, aggregate or proxy entity: ACAD_PROXY_ENTITY'||defect==='unsupported-aggregate'&&error.message==='Unknown aggregate or embedded entity framing is unsupported.';
        else if(error instanceof FormatException){rejected=['missing-identity','private-identity','duplicate-common-equal','duplicate-common-different'].includes(defect)&&error.message.startsWith('A retained DXF entity ')&&error.message.includes('common handle');if(defect==='duplicate-physical')rejected=error.message==='A retained DXF object has an ambiguous physical source identity: '+target;}
        else throw error;
      }
      Check(rejected,'Unretained or unsupported HATCH source did not reject contextually: '+defect);
    }else Check(DxfDocument.Load(stream)===null,'Release HATCH source rejection returns null: '+defect);
  }finally{stream.Dispose();}
}
export function HatchSourceShared(binary,placement){
  const doc=new DxfDocument();let block=doc.Blocks.get_Item('*Model_Space');if(placement===1){const layout=new Layout('HatchPaper');doc.Layouts.Add(layout);block=layout.AssociatedBlock;}if(placement===2){block=new Block('HatchNested');doc.Blocks.Add(block);}
  const source=new Circle(Vector2.Zero,3);block.Entities.Add(source);
  const first=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([source,source]),new HatchBoundaryPath([source])],true),second=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([source])],true);
  block.Entities.Add(first);block.Entities.Add(second);Equal(4,source.Reactors.Count,'Shared sources count each list occurrence');
  const clone=first.Clone();Check(!clone.Associative&&Array.from(clone.BoundaryPaths).every(p=>p.Entities.Count===0)&&source.Reactors.Count===4,'Clone detaches association without changing source');
  first.BoundaryPaths.Remove(first.BoundaryPaths.get_Item(0));Equal(2,source.Reactors.Count,'Removing one path releases precisely its duplicate uses');Check(source.Owner===block,'Source shared by remaining paths is retained');
  Check(block.Entities.Remove(second),'Second hatch can be removed');Equal(1,source.Reactors.Count,'Removing second hatch releases its source use');Check(!block.Entities.Remove(source),'Final source association prevents removal');HatchSourceLoad(HatchSourceSave(doc,binary));
  first.BoundaryPaths.Remove(first.BoundaryPaths.get_Item(0));Check(source.Owner===null&&!block.Entities.Contains(source),'Last source path removal uses its owning block rather than active layout');Check(block.Entities.Remove(first),'Remove emptied hatch before export');HatchSourceLoad(HatchSourceSave(doc,binary));
}
export function HatchSourceApi(scenario){
  let doc=new DxfDocument();const block=new Block('SourceOwner');doc.Blocks.Add(block);const foreign=new Circle(Vector2.Zero,2);block.Entities.Add(foreign);let hatch=new Hatch(HatchPattern.Solid,true);doc.Entities.Add(hatch);
  if(scenario===0){const count=hatch.BoundaryPaths.Count,reactors=foreign.Reactors.Count,seed=doc.DrawingVariables.HandleSeed;Throws(ArgumentException,()=>hatch.BoundaryPaths.Add(new HatchBoundaryPath([foreign])));Check(hatch.BoundaryPaths.Count===count&&foreign.Reactors.Count===reactors&&seed===doc.DrawingVariables.HandleSeed,'Rejected wrong-block path mutated collection/reactors/handles');}
  else if(scenario===1){const incoming=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([foreign])],true),seed=doc.DrawingVariables.HandleSeed,count=Array.from(doc.Entities.All).length;Throws(ArgumentException,()=>doc.Entities.Add(incoming));Check(incoming.Owner===null&&incoming.Handle===null&&count===Array.from(doc.Entities.All).length&&seed===doc.DrawingVariables.HandleSeed,'Rejected wrong-block hatch adoption mutated destination');}
  else if(scenario===2){const nested=new Hatch(HatchPattern.Solid,true);block.Entities.Add(nested);const circle=new Circle(Vector2.Zero,4);nested.BoundaryPaths.Add(new HatchBoundaryPath([circle]));Check(circle.Owner===block,'Added nested-block source did not use actual owner');Check(doc.Entities.Remove(hatch),'Remove unused empty control hatch');HatchSourceLoad(HatchSourceSave(doc,false));}
  else if(scenario===3){const circle=new Circle(Vector2.Zero,4),boundary=new HatchBoundaryPath([circle]);hatch.BoundaryPaths.Add(boundary);const another=new Hatch(HatchPattern.Solid,true);doc.Entities.Add(another);const reactors=circle.Reactors.Count;Throws(ArgumentException,()=>another.BoundaryPaths.Add(boundary));Check(another.BoundaryPaths.Count===0&&circle.Reactors.Count===reactors,'Shared path-instance rejection changed either hatch');hatch.UnLinkBoundary();Check(boundary.Entities.Count===0&&circle.Reactors.Count===0&&circle.Owner!==null,'Explicit unlink clears association but retains source');}
  else if(scenario===4){const circle=new Circle(Vector2.Zero,4),boundary=new HatchBoundaryPath([circle]);hatch.BoundaryPaths.Add(boundary);Throws(ArgumentException,()=>hatch.BoundaryPaths.Add(boundary));Equal(1,circle.Reactors.Count,'Repeated path rejection preserves the original source use');Equal(1,hatch.UnLinkBoundary().Count,'Unlink returns the unique attached path source');Equal(0,circle.Reactors.Count,'Unlink releases the original source use');const detached=new HatchBoundaryPath([circle]);Throws(ArgumentException,()=>new Hatch(HatchPattern.Solid,[detached,detached],true));Equal(0,circle.Reactors.Count,'Constructor repeated path rejection precedes mutations');}
  else if(scenario===5){const count=hatch.BoundaryPaths.Count;Throws(ArgumentException,()=>hatch.BoundaryPaths.Add(null));Equal(count,hatch.BoundaryPaths.Count,'Null path is not inserted');}
  else if(scenario===6){hatch.BoundaryPaths.Add(new HatchBoundaryPath([Object.assign(new HatchBoundaryPath.Line(),{Start:Vector2.Zero,End:Vector2.UnitX})]));const loaded=single(HatchSourceLoad(HatchSourceSave(doc,true)).Entities.Hatches);Check(loaded.Associative&&single(loaded.BoundaryPaths).Entities.Count===0,'Associative path with zero sources remains supported');}
  else if(scenario===7||scenario===8){const boundary=scenario===7?new HatchBoundaryPath([new Circle(Vector2.Zero,3)]):new HatchBoundaryPath([Object.assign(new HatchBoundaryPath.Line(),{Start:Vector2.Zero,End:Vector2.UnitX})]);hatch.BoundaryPaths.Add(boundary);const count=boundary.Entities.Count,reactors=Array.from(boundary.Entities).reduce((n,e)=>n+e.Reactors.Count,0);Throws(ArgumentException,()=>new Hatch(HatchPattern.Solid,[boundary],scenario===8));Check(boundary.Entities.Count===count&&Array.from(boundary.Entities).reduce((n,e)=>n+e.Reactors.Count,0)===reactors&&hatch.BoundaryPaths.Contains(boundary),'Rejected constructor mutated already attached source path');}
  else if([9,10,11].includes(scenario)){
    const sourceDoc=DxfDocument.Load(path.join(root,'tests/fixtures/polyline3d-records/producer-R2018-ascii.dxf')),retained=sourceDoc.GetObjectByHandle('3B');Check(sourceDoc.Entities.Remove(retained),'Plain retained source can detach');
    const line=new Line(Vector2.Zero,Vector2.UnitX),boundary=new HatchBoundaryPath([line,retained]),count=Array.from(doc.Entities.All).length,blocks=doc.Blocks.Count,seed=doc.DrawingVariables.HandleSeed,paths=hatch.BoundaryPaths.Count;
    if(scenario===9){const incoming=new Hatch(HatchPattern.Solid,[boundary],true);Throws(NotSupportedException,()=>doc.Entities.Add(incoming));Check(incoming.Owner===null&&incoming.Handle===null,'Rejected retained-source hatch acquired identity');}
    else if(scenario===10)Throws(NotSupportedException,()=>hatch.BoundaryPaths.Add(boundary));
    else{const incoming=new Block('ForeignSourceContainer');incoming.Entities.Add(new Hatch(HatchPattern.Solid,[boundary],true));Throws(NotSupportedException,()=>doc.Blocks.Add(incoming));Check(incoming.Record.Owner===null&&incoming.Handle===null,'Rejected retained-source block acquired identity');}
    Check(count===Array.from(doc.Entities.All).length&&blocks===doc.Blocks.Count&&seed===doc.DrawingVariables.HandleSeed&&paths===hatch.BoundaryPaths.Count&&line.Handle===null,'Rejected source adoption mutated destination membership/handles/paths');
  }else{
    doc=HatchSourceLoad(HatchSourceInput('producer',DxfVersion.AutoCad2018,false));hatch=doc.GetObjectByHandle('3A2');const other=doc.GetObjectByHandle('3A3'),sources=[...new Set(sourceList(hatch))];
    if(scenario===12)hatch.UnLinkBoundary();else if(scenario===13)Check(doc.Entities.Remove(hatch),'Loaded hatch removal');
    else{hatch.BoundaryPaths.Remove(hatch.BoundaryPaths.get_Item(0));Check(sources.every(e=>Array.from(e.PersistentReactors).includes(hatch)),'Loaded backlink must remain for surviving path uses');hatch.BoundaryPaths.Remove(hatch.BoundaryPaths.get_Item(0));}
    Check(sources.every(e=>!Array.from(e.PersistentReactors).includes(hatch)&&Array.from(e.PersistentReactors).includes(other)&&e.Owner!==null),'Loaded backlink cleanup must retain other hatch/source relationships');
    if(scenario===14)Check(doc.Entities.Remove(hatch),'Remove emptied loaded hatch before export');HatchSourceLoad(HatchSourceSave(doc,false));HatchSourceLoad(HatchSourceSave(doc,true));
  }
}
