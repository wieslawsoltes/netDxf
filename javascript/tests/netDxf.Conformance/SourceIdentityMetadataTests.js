// Port of pinned SourceIdentityMetadataTests.cs.
import { DxfDocument, DxfRawDocument, DxfTag, MemoryStream, Line, Circle, Vector3 } from '../../index.js';
import { FormatException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, BooleanName } from './TestHarness.js';
const single=values=>{const a=Array.from(values);Equal(1,a.length,'Single');return a[0];};
export function RegisterSourceIdentityMetadataTests(){for(const binary of [false,true]){
  Run(`source-identity-metadata/retained-reactor/${BooleanName(binary)}`,()=>SourceIdentityMetadata(false,false,true,binary));
  for(const before of [false,true])for(const metadata of [false,true])Run(`source-identity-metadata/reject-duplicate/${BooleanName(binary)}/${BooleanName(before)}/${BooleanName(metadata)}`,()=>SourceIdentityMetadata(true,before,metadata,binary));
}}
export function SourceIdentityMetadata(duplicate,before,metadata,binary){
  const document=new DxfDocument(),source=new Line(Vector3.Zero,Vector3.UnitX),reactor=new Circle(Vector3.Zero,2);document.Entities.Add(source);document.Entities.Add(reactor);if(metadata)source.PersistentReactors.Add(reactor);
  const original=new MemoryStream(),input=new MemoryStream();try{
    Check(document.Save(original,binary),'Source identity metadata fixture export');original.Position=0;const raw=DxfRawDocument.Load(original),record=single(single(raw.Sections.filter(s=>s.Name==='ENTITIES')).Records.filter(r=>r.Name==='LINE')),tags=Array.from(raw.Tags);
    if(duplicate)tags.splice(before?record.StartTagIndex:record.StartTagIndex+record.Tags.Count,0,...[[0,'UNSUPPORTED_CURVE'],[5,source.Handle],[330,source.Owner.Record.Handle],[100,'AcDbEntity'],[8,'0'],[100,'AcDbFutureCurve']].map(([c,v])=>new DxfTag(c,v)));
    DxfRawDocument.Create(tags).Save(input,binary);input.Position=0;
    if(duplicate){if(GetTypedIOConfiguration()==='Debug'){let rejected=false;try{DxfDocument.Load(input);}catch(e){if(!(e instanceof FormatException))throw e;rejected=e.message.includes('ambiguous physical source identity')&&e.message.includes(source.Handle);}Check(rejected,'Unreferenced retained entity with a duplicate physical declaration must reject before metadata can be skipped');}else Check(DxfDocument.Load(input)===null,'Release must reject ambiguous retained source identity without silently discarding metadata');return;}
    let loaded=DxfDocument.Load(input);Check(loaded!==null,'Unique retained entity was rejected');
    for(let cycle=0;cycle<2;cycle++){const retained=loaded.GetObjectByHandle(source.Handle);Check(retained instanceof Line,'Retained LINE type');Equal(1,retained.PersistentReactors.Count,'Unique source retains its common reactor metadata');Check(single(retained.PersistentReactors)===loaded.GetObjectByHandle(reactor.Handle),'Reactor points to its exact registered source object');Check(Vector3.Zero.Equals(retained.StartPoint),'Retained source start geometry');Check(Vector3.UnitX.Equals(retained.EndPoint),'Retained source end geometry');const output=new MemoryStream();try{Check(loaded.Save(output,cycle===0?!binary:binary),'Unique source metadata export');output.Position=0;loaded=DxfDocument.Load(output);Check(loaded!==null,'Unique source metadata reload failed');}finally{output.Dispose();}}
  }finally{input.Dispose();original.Dispose();}
}
