// Port of pinned SourceReferenceIdentityTests.cs. Nested original registrars live in their own mirrored modules.
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, MemoryStream, Line, Vector3, DxfDictionary, DxfPlaceholder, DxfIdBuffer, DxfDictionaryWithDefault, DxfDictionaryVariable } from '../../index.js';
import { FormatException, InvalidDataException } from '../../runtime/Errors.js';
import { Run, Check, Equal, BooleanName } from './TestHarness.js';
const single=values=>{const a=Array.from(values);Equal(1,a.length,'Single');return a[0];};
const beforeSubclass=record=>{const tags=Array.from(record.Tags),end=tags.findIndex(t=>t.Code===100);return end<0?tags:tags.slice(0,end);};
const tagsOf=pairs=>pairs.map(([c,v])=>new DxfTag(c,v));
export function RegisterSourceReferenceIdentityTests(){for(const binary of [false,true]){
  for(const path of ['idbuffer','dictionary-entry','dictionary-default','reactor','extension']){
    for(const decoy of ['absent','unknown-entity','discarded-underlay','dictionary-entity','ignored-section'])Run(`source-reference/reject/${path}/${decoy}/${BooleanName(binary)}`,()=>SourceReferenceReject(path,decoy,binary));
    if(path!=='extension')Run(`source-reference/reject/${path}/generated-table/${BooleanName(binary)}`,()=>SourceReferenceReject(path,'generated-table',binary));
    for(const normalized of [false,true])Run(`source-reference/retained/${path}/${BooleanName(normalized)}/${BooleanName(binary)}`,()=>SourceReferenceRetained(path,normalized,binary));
  }
  Run(`source-reference/consumed-extension-wrong-host/${BooleanName(binary)}`,()=>SourceReferenceConsumedWrongHost(binary));
  Run(`source-reference/null-idbuffer/${BooleanName(binary)}`,()=>SourceReferenceNull(binary));
  Run(`source-reference/consumed-layer-state-extension/${BooleanName(binary)}`,()=>SourceReferenceLayerStates(binary));
}}
export function SourceReferenceSeed(path){
  const doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Comments.Clear();doc.Entities.Add(new Line(Vector3.Zero,Vector3.UnitX));
  const graph=new DxfDictionary(),target=new DxfPlaceholder(),carrier=new DxfPlaceholder(),buffer=new DxfIdBuffer(),fallback=new DxfDictionaryWithDefault();
  graph.Add('TARGET',target);graph.Add('CARRIER',carrier);graph.Add('BUFFER',buffer);graph.Add('DEFAULT',fallback);graph.Add('ENTRY',target,false);buffer.References.Add(target);buffer.References.Add(null);fallback.Default=target;carrier.PersistentReactors.Add(target);doc.NamedObjects.Add('SOURCE_REFERENCES',graph);
  const extension=new DxfDictionary(),variable=new DxfDictionaryVariable();variable.Value='retained extension';extension.Add('VALUE',variable);doc.Objects.SetExtensionDictionary(carrier,extension);
  const stream=new MemoryStream();try{
    Check(doc.Save(stream),'source-reference seed save');stream.Position=0;const raw=DxfRawDocument.Load(stream),seed=single(single(raw.Sections.filter(s=>s.Name==='HEADER')).Records.filter(r=>r.Name==='$HANDSEED')),missing=single(Array.from(seed.Tags).filter(t=>t.Code===5)).Value;
    Check(!raw.Sections.filter(s=>s.Name!=='HEADER').flatMap(s=>s.Records).some(r=>beforeSubclass(r).some(t=>(t.Code===5||t.Code===105)&&t.Value===missing)),'HANDSEED must be absent from physical record identities');
    return {Raw:raw,Carrier:path==='idbuffer'?buffer.Handle:path==='dictionary-entry'?graph.Handle:path==='dictionary-default'?fallback.Handle:carrier.Handle,Target:path==='extension'?extension.Handle:target.Handle,Missing:missing};
  }finally{stream.Dispose();}
}
export function SourceReferenceRecord(raw,handle){return single(raw.Sections.filter(s=>s.Name!=='HEADER').flatMap(s=>s.Records).filter(r=>beforeSubclass(r).some(t=>(t.Code===5||t.Code===105)&&t.Value===handle)));}
export function SourceReferenceReplace(fixture,path,value){
  const record=SourceReferenceRecord(fixture.Raw,fixture.Carrier),tags=Array.from(record.Tags);let index;
  if(path==='dictionary-entry')index=tags.findIndex(t=>t.Code===3&&t.Value==='ENTRY')+1;
  else if(path==='dictionary-default')index=tags.findIndex(t=>t.Code===340);
  else if(path==='idbuffer'){const start=tags.findIndex(t=>t.Code===100);index=tags.findIndex((t,i)=>i>start&&t.Code===330);}
  else index=tags.findIndex(t=>t.Code===102&&t.Value===(path==='reactor'?'{ACAD_REACTORS':'{ACAD_XDICTIONARY'))+1;
  Check(index>0&&tags[index].Value===fixture.Target,'expected reference packet location');tags[index]=new DxfTag(tags[index].Code,value);return fixture.Raw.WithRecord(record,tags);
}
export function SourceReferenceDecoy(raw,handle,decoy){
  if(decoy==='absent')return raw;const tags=Array.from(raw.Tags);
  if(decoy==='ignored-section'){const eof=tags.findIndex(t=>t.Code===0&&t.Value==='EOF');tags.splice(eof,0,...tagsOf([[0,'SECTION'],[2,'FUTURE_SOURCE_SECTION'],[0,'DICTIONARY'],[5,handle],[100,'AcDbDictionary'],[0,'ENDSEC']]));}
  else {const underlay=decoy==='discarded-underlay'||decoy==='generated-table',kind=underlay?'PDFUNDERLAY':decoy==='dictionary-entity'?'DICTIONARY':'FUTURE_ENTITY',subclass=underlay?'AcDbUnderlayReference':decoy==='dictionary-entity'?'AcDbDictionary':'AcDbFutureEntity';
    const packet=tagsOf([[0,kind],[5,handle],[100,'AcDbEntity'],[8,'0'],[100,subclass]]);if(underlay)packet.push(new DxfTag(340,'0'));tags.splice(single(raw.Sections.filter(s=>s.Name==='ENTITIES')).ContentStartTagIndex,0,...packet);
    if(decoy==='generated-table'){const table=single(single(raw.Sections.filter(s=>s.Name==='TABLES')).Records.filter(r=>r.Name==='TABLE'&&Array.from(r.Tags).some(t=>t.Code===2&&t.Value==='LAYER'))),identity=Array.from(table.Tags).findIndex(t=>t.Code===5);Check(identity>=0,'source LAYER table identity');tags.splice(table.StartTagIndex+identity,1);}
  }return raw.WithTags(tags);
}
export function SourceReferenceReject(path,decoy,binary){
  const fixture=SourceReferenceSeed(path),raw=SourceReferenceDecoy(SourceReferenceReplace(fixture,path,fixture.Missing),fixture.Missing,decoy);
  if(decoy==='unknown-entity'||decoy==='dictionary-entity'){SourceReferenceRejectMalformedEntity(raw,binary);return;}
  const stream=new MemoryStream();try{raw.Save(stream,binary);stream.Position=0;let rejected=false;try{rejected=DxfDocument.Load(stream)===null;}catch(e){if(!(e instanceof FormatException))throw e;const expected={'idbuffer':'IDBUFFER','dictionary-entry':'dictionary entry','dictionary-default':'dictionary default','reactor':'persistent reactor'}[path]??'extension dictionary';Check(e.message.toLowerCase().includes(expected.toLowerCase()),'rejection must identify the dangling reference');rejected=true;}Check(rejected,'a missing or discarded source object resolved to a synthesized runtime object');Check(stream.CanRead,'rejection closed the caller stream');}finally{stream.Dispose();}
}
export function SourceReferenceRejectMalformedEntity(raw,binary){
  Check(single(raw.Sections.filter(s=>s.Name==='ENTITIES')).Records.some(r=>(r.Name==='FUTURE_ENTITY'||r.Name==='DICTIONARY')&&!beforeSubclass(r).some(t=>t.Code===330)),'early-admission fixture must retain its deliberately absent entity owner');
  const input=new MemoryStream();try{raw.Save(input,binary);input.Position=0;let rejected=false;try{rejected=DxfDocument.Load(input)===null;}catch(e){if(!(e instanceof InvalidDataException)||e.message!=='Unknown entity requires one physical identity, owner, layer and complete subclass envelope.')throw e;rejected=true;}Check(rejected,'ownerless unknown entity must fail bounded admission before source binding');Check(input.CanRead,'early admission closed the caller stream');}finally{input.Dispose();}
}
export function SourceReferenceOpaqueTarget(raw,handle,normalized){
  const record=single(single(raw.Sections.filter(s=>s.Name==='TABLES')).Records.filter(r=>r.Name==='BLOCK_RECORD'&&Array.from(r.Tags).some(t=>t.Code===2&&t.Value==='*Model_Space'))),owner=single(Array.from(record.Tags).filter(t=>t.Code===5)).Value,spelling=normalized?'000'+handle.toLowerCase():handle;
  const packet=tagsOf([[0,'QUALIFIED_SOURCE_TARGET'],[5,spelling],[330,owner],[100,'AcDbEntity'],[8,'0'],[100,'AcDbQualifiedSourceTarget'],[1,'actual retained source']]),tags=Array.from(raw.Tags),at=single(raw.Sections.filter(s=>s.Name==='ENTITIES')).ContentStartTagIndex;tags.splice(at,0,...packet);raw=raw.WithTags(tags);
  const seed=single(single(raw.Sections.filter(s=>s.Name==='HEADER')).Records.filter(r=>r.Name==='$HANDSEED')),old=BigInt('0x'+single(Array.from(seed.Tags).filter(t=>t.Code===5)).Value),candidate=BigInt('0x'+handle)+1n,next=old>candidate?old:candidate;return raw.WithRecord(seed,Array.from(seed.Tags).map(t=>t.Code===5?new DxfTag(5,next.toString(16).toUpperCase()):t));
}
export function SourceReferenceValue(doc,handle,path){const carrier=doc.GetObjectByHandle(handle);switch(path){case'idbuffer':return carrier.References.get_Item(0);case'dictionary-entry':return carrier.get_Item('ENTRY');case'dictionary-default':return carrier.Default;case'reactor':return single(carrier.PersistentReactors);default:return carrier.ExtensionDictionary;}}
export function SourceReferenceRetained(path,normalized,binary){
  const fixture=SourceReferenceSeed(path),spelling=normalized?'000'+fixture.Target.toLowerCase():fixture.Target;let raw=SourceReferenceReplace(fixture,path,spelling);
  if(normalized){const target=SourceReferenceRecord(raw,fixture.Target),tags=Array.from(target.Tags),identity=tags.findIndex(t=>t.Code===5);tags[identity]=new DxfTag(5,spelling);raw=raw.WithRecord(target,tags);}
  const stream=new MemoryStream(),output=new MemoryStream();try{raw.Save(stream,binary);stream.Position=0;const loaded=DxfDocument.Load(stream);Check(loaded!==null,'retained source load failed');const expected=loaded.GetObjectByHandle(fixture.Target);Check(expected!==null&&expected===SourceReferenceValue(loaded,fixture.Carrier,path),'retained source identity was replaced or lost');Equal(0,loaded.Objects.Validate().Count,'retained source graph');Check(loaded.Save(output,!binary),'retained source cross-transport save');output.Position=0;const again=DxfDocument.Load(output);Check(again!==null,'retained source reload failed');Check(again.GetObjectByHandle(fixture.Target)===SourceReferenceValue(again,fixture.Carrier,path),'retained source identity lost on reload');}finally{output.Dispose();stream.Dispose();}
}
export function SourceReferenceConsumedWrongHost(binary){const fixture=SourceReferenceSeed('extension'),layer=single(single(fixture.Raw.Sections.filter(s=>s.Name==='TABLES')).Records.filter(r=>r.Name==='TABLE'&&Array.from(r.Tags).some(t=>t.Code===2&&t.Value==='LAYER'))),consumed=single(Array.from(layer.Tags).filter(t=>t.Code===360)).Value;Check(SourceReferenceRecord(fixture.Raw,consumed).Name==='DICTIONARY','consumed extension source exists');const stream=new MemoryStream();try{SourceReferenceReplace(fixture,'extension',consumed).Save(stream,binary);stream.Position=0;let rejected=false;try{rejected=DxfDocument.Load(stream)===null;}catch(e){if(!(e instanceof FormatException))throw e;Check(e.message.toLowerCase().includes('extension dictionary'),'wrong host diagnostic');rejected=true;}Check(rejected,"the consumed LAYER dictionary must not silently satisfy an unrelated object's extension reference");Check(stream.CanRead,'wrong host rejection closed the caller stream');}finally{stream.Dispose();}}
export function SourceReferenceNull(binary){const fixture=SourceReferenceSeed('idbuffer'),stream=new MemoryStream();try{SourceReferenceReplace(fixture,'idbuffer','000').Save(stream,binary);stream.Position=0;const doc=DxfDocument.Load(stream);Check(doc!==null,'null reference load failed');Check(Array.from(doc.GetObjectByHandle(fixture.Carrier).References).every(r=>r===null),'null reference acquired an identity');Equal(0,doc.Objects.Validate().Count,'null reference graph');}finally{stream.Dispose();}}
export function SourceReferenceLayerStates(binary){const doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Layers.StateManager.AddNew('SOURCE_SNAPSHOT');const variable=new DxfDictionaryVariable();variable.Value='source metadata';doc.NamedObjects.Add('APP',variable);const stream=new MemoryStream(),output=new MemoryStream();try{Check(doc.Save(stream,binary),'managed source save');stream.Position=0;const raw=DxfRawDocument.Load(stream),layerTable=single(single(raw.Sections.filter(s=>s.Name==='TABLES')).Records.filter(r=>r.Name==='TABLE'&&Array.from(r.Tags).some(t=>t.Code===2&&t.Value==='LAYER'))),extension=single(Array.from(layerTable.Tags).filter(t=>t.Code===360)).Value;Check(SourceReferenceRecord(raw,extension).Name==='DICTIONARY','managed extension must have a real source dictionary');stream.Position=0;const loaded=DxfDocument.Load(stream);Check(loaded!==null,'managed source load failed');Equal(1,loaded.Layers.StateManager.Count,'consumed layer-state mapping');Check(loaded.Layers.StateManager.Contains('SOURCE_SNAPSHOT'),'consumed snapshot name lost');Equal('source metadata',loaded.NamedObjects.get_Item('APP').Value,'ordinary source object beside consumed mapping');Equal(0,loaded.Objects.Validate().Count,'managed source graph');Check(loaded.Save(output,!binary),'managed source cross-transport save');output.Position=0;const again=DxfDocument.Load(output);Check(again!==null,'managed source reload failed');Check(again.Layers.StateManager.Contains('SOURCE_SNAPSHOT'),'consumed mapping lost on reload');}finally{output.Dispose();stream.Dispose();}}
