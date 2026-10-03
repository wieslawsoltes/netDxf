// Port of the complete pinned tests/netDxf.Conformance/OleVersionPresenceTests.cs.
import { DxfDocument, DxfRawDocument, DxfTag, Vector3, MemoryStream } from '../../index.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { LegacyOleTags } from './OleFrameTests.js';
import { OlePayload, OleSingle, OleRecords, OleWriteArtifact } from './Ole2FrameTests.js';
export function RegisterOleVersionPresenceTests(){for(const v of SupportedVersions)for(const b of [false,true])for(const p of [false,true])for(const n of [0,1,7,32767])Run(`oleframe/version-presence/${VersionName(v)}/${BooleanName(b)}/${BooleanName(p)}/${n}`,()=>OleVersionPresence(v,b,p,n));}
export function OleVersionPresence(v,b,p,n){
  const tags=LegacyOleTags(v,128,true),at=tags.findIndex(t=>t.Code===70);if(p)tags[at]=new DxfTag(70,n);else tags.splice(at,1);const input=new MemoryStream(RawFixtureBytes(tags,b));
  try{let doc=DxfDocument.Load(input);Check(doc!==null,'Version-presence input rejected.');const frame=OleSingle(doc.Entities.OleFrames);Equal(p?n:1,frame.OleVersion,'Existing version getter contract changed');doc.Entities.Add(frame.Clone());
    for(let cycle=0;cycle<3;cycle++){const output=new MemoryStream();try{
      Check(doc.Save(output,cycle%2===0?!b:b),'Version-presence save failed.');output.Position=0;for(const record of OleRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='OLEFRAME')){const t=Array.from(record.Tags),versions=t.filter(t=>t.Code===70);Equal(p?1:0,versions.length,'OLE version presence changed');if(p)Equal(n,versions[0].Value,'Explicit OLE version changed');Equal(128,OleSingle(t.filter(t=>t.Code===90)).Value,'Payload length changed');Equal(OlePayload(128),Uint8Array.from(t.filter(t=>t.Code===310).flatMap(t=>Array.from(t.Value))),'Opaque payload changed.');Equal('OLE',OleSingle(t.filter(t=>t.Code===1)).Value,'Required terminator lost');}
      if(cycle===1&&n===1)OleWriteArtifact(`oleversion-${VersionName(v)}-${BooleanName(b)}-${BooleanName(p)}.dxf`,output.ToArray());output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'Version-presence reload failed.');Equal(2,Array.from(doc.Entities.OleFrames).length,'Clone/frame count changed');for(const current of doc.Entities.OleFrames){Equal(p?n:1,current.OleVersion,'Reload getter contract changed');Equal('after legacy bytes',OleSingle(current.XData.get_Item('LEGACY_OLE').XDataRecord).Value,'XData changed');}Check(new Vector3(10,20,30).Equals(OleSingle(doc.Entities.Lines).StartPoint),'Following LINE changed');
    }finally{output.Dispose();}}Check(input.CanRead,'Reader closed caller stream.');
  }finally{input.Dispose();}
}
