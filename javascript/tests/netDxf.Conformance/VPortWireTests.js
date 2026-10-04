// Port of pinned VPortWireTests.cs; packets are authored independently of the typed writer.
import fs from 'node:fs';
import { DxfDocument, DxfRawDocument, DxfTag, VPort, Vector3, MemoryStream } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, Near, SameDoubleBits, Throws, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
const app='VPORT_TEST', configuration='Plan Żółć';
const tagsOf=rows=>rows.map(([c,v])=>new DxfTag(c,v));
const single=values=>{const a=Array.from(values);Equal(1,a.length,'Expected one item');return a[0];};
function artifact(name,bytes){const dir=new URL('../../artifacts/conformance/fixtures/',import.meta.url);fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(new URL(name,dir),bytes);}
export function RegisterVPortWireTests(){for(const v of SupportedVersions)for(const b of [false,true]){
  const suffix=`${VersionName(v)}/${BooleanName(b)}`;
  Run(`vport/wire/${suffix}/canonical`,()=>VPortWire(v,b,false));Run(`vport/wire/${suffix}/reordered`,()=>VPortWire(v,b,true));
  Run(`vport/defaults/${suffix}`,()=>VPortDefaults(v,b));Run(`vport/active-clone/${suffix}`,()=>VPortActiveClone(v,b));
  for(let f=0;f<14;f++)Run(`vport/invalid/${suffix}/${f}`,()=>VPortInvalid(v,b,f));
  for(const c of [10,11,12,13,14,15,16,17,110,111,112])Run(`vport/incomplete-point/${suffix}/${c}`,()=>VPortIncompletePoint(v,b,c));
}}
export function VPortPacket(index){return tagsOf([
  [2,index<2?(index===0?'*ACTIVE':'*aCtIvE'):'Plan \\U+017B\\U+00F3\\U+0142\\U+0107'],
  [70,64],[10,index%2*.5],[20,0],[11,(index%2+1)*.5],[21,1],[12,7.25+index],[22,-3.5-index],[13,.125],[23,-.25],
  [14,1.25],[24,2.5],[15,5],[25,7.5],[16,2+index],[26,3],[36,4],[17,-5],[27,6],[37,-7],
  [40,12.500000000000002+index],[41,1.75],[42,85.5],[43,-2],[44,500],[50,13.75],[51,-42.5],[71,31],[72,1234],
  [73,0],[74,2],[75,1],[76,0],[77,1],[78,2],[281,6],[65,1],
  [110,1],[120,2],[130,3],[111,0],[121,1],[131,0],[112,-1],[122,0],[132,0],[79,3],[146,5.25]
]);}
export function VPortFixtureTags(version,reverse=false,count=4){const tags=tagsOf([
  [0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[9,'$HANDSEED'],[5,'1000'],[0,'ENDSEC'],
  [0,'SECTION'],[2,'TABLES'],[0,'TABLE'],[2,'VPORT'],[5,'A'],[330,'0'],[100,'AcDbSymbolTable'],[70,count],[1001,app],[1000,'table metadata']]);
  for(let i=0;i<count;i++){tags.push(...tagsOf([[0,'VPORT'],[5,(0x100+i).toString(16).toUpperCase()],[330,'A'],[100,'AcDbSymbolTableRecord'],[100,'AcDbViewportTableRecord']]));const packet=VPortPacket(i);if(reverse)packet.reverse();tags.push(...packet,...tagsOf([[1001,app],[1000,'tile '+i],[1004,new Uint8Array([i,0,255])]]));}
  tags.push(...tagsOf([[0,'ENDTAB'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'LINE'],[5,'200'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,20],[20,30],[30,40],[11,50],[21,60],[31,70],[0,'ENDSEC'],[0,'EOF']]));return tags;
}
export function VPortWire(version,binary,reverse){const tags=VPortFixtureTags(version,reverse);
  if(!binary&&reverse){const stop=tags.findIndex(t=>t.Code===0&&t.Value==='ENDTAB');for(let i=stop-1;i>20;i--)tags.splice(i,0,new DxfTag(999,'VPORT comment 0 ENDTAB'));}
  const input=new MemoryStream(RawFixtureBytes(tags,binary));try{let doc=DxfDocument.Load(input);Check(doc!==null,'VPORT input rejected.');Check(input.CanRead,'VPORT reader closed caller stream.');
    for(let cycle=0;cycle<3;cycle++){
      Near(7.25,doc.Viewport.ViewCenter.X,'First active viewport was discarded');SameDoubleBits(12.500000000000002,doc.Viewport.ViewHeight,'View height precision');Equal(new Vector3(2,3,4),doc.Viewport.ViewDirection,'View direction must retain magnitude');Equal('100',doc.Viewport.Handle,'First active handle');Equal('tile 0',doc.Viewport.XData.get_Item(app).XDataRecord.get_Item(0).Value,'Active record XData');Check(doc.Viewport===doc.GetObjectByHandle('100'),'Active handle is not indexed.');Equal('table metadata',doc.VPorts.XData.get_Item(app).XDataRecord.get_Item(0).Value,'Table XData');
      for(let index=0;index<4;index++){const record=doc.GetObjectByHandle((0x100+index).toString(16).toUpperCase());Check(record instanceof VPort&&record.Owner===doc.VPorts,'VPORT record identity/owner lost.');Equal('tile '+index,record.XData.get_Item(app).XDataRecord.get_Item(0).Value,'Repeated-name XData aliased');}
      Equal(configuration,doc.GetObjectByHandle('102').Name,'Unicode configuration');const output=new MemoryStream();try{
        Check(doc.Save(output,cycle===1?!binary:binary),'VPORT save failed.');output.Position=0;const raw=DxfRawDocument.Load(output),records=Array.from(raw.Sections).flatMap(s=>Array.from(s.Records)).filter(r=>r.Name==='VPORT');Equal(4,records.length,'Physical VPORT record count');
        for(let index=0;index<records.length;index++){const rows=Array.from(records[index].Tags),start=rows.findIndex(t=>t.Code===100&&t.Value==='AcDbViewportTableRecord')+1,stop=rows.findIndex((t,i)=>i>=start&&t.Code===1001),packet=rows.slice(start,stop<0?undefined:stop);
          for(const expected of VPortPacket(index).filter(t=>t.Code!==2)){const actual=single(packet.filter(t=>t.Code===expected.Code));if(expected.Code>=10&&expected.Code<=59||expected.Code>=110&&expected.Code<=149)SameDoubleBits(expected.Value,actual.Value,'Exact VPORT group '+expected.Code);else Equal(expected.Value,actual.Value,'Exact VPORT group '+expected.Code);}
          Equal('A',single(rows.filter(t=>t.Code===330)).Value,'Table owner edge');}
        if(cycle===0&&!reverse)artifact(`vport-${VersionName(version)}-${binary?'binary':'text'}.dxf`,output.ToArray());output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'VPORT output failed to reload.');Equal(new Vector3(20,30,40),single(doc.Entities.Lines).StartPoint,'Following entity consumed');
      }finally{output.Dispose();}}
  }finally{input.Dispose();}
}
export function VPortDefaults(version,binary){const input=new MemoryStream(RawFixtureBytes(VPortFixtureTags(version,false,0),binary)),output=new MemoryStream();try{const doc=DxfDocument.Load(input);Check(doc!==null,'Empty VPORT table rejected.');Equal(1,doc.VPorts.Count,'Empty table default configuration');Equal(Vector3.UnitZ,doc.Viewport.ViewDirection,'Default direction');Check(doc.GetObjectByHandle(doc.Viewport.Handle)===doc.Viewport,'Default active identity missing');Check(doc.Save(output,binary),'Default VPORT failed to export');output.Position=0;Check(DxfDocument.Load(output)!==null,'Default VPORT failed to reload');}finally{output.Dispose();input.Dispose();}}
export function VPortActiveClone(version,binary){const input=new MemoryStream(RawFixtureBytes(VPortFixtureTags(version),binary));try{const doc=DxfDocument.Load(input);Check(doc!==null,'VPORT input rejected.');const clone=doc.Viewport.Clone();Check(clone.SnapMode&&!clone.ShowGrid,'Active viewport clone lost snap/grid state');Check(clone.Owner===null&&clone.Handle===null&&clone.IsReserved,'Active clone identity/flags');clone.XData.get_Item(app).XDataRecord.get_Item(1).Value[0]=201;Equal(0,doc.Viewport.XData.get_Item(app).XDataRecord.get_Item(1).Value[0],'Clone binary XData aliases source');}finally{input.Dispose();}}
export function VPortInvalid(version,binary,scenario){const tags=VPortFixtureTags(version),start=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbViewportTableRecord')+1,end=tags.findIndex((t,i)=>i>=start&&t.Code===1001),set=(c,v)=>{const i=tags.findIndex((t,i)=>i>=start&&i<end&&t.Code===c);Check(i>=0,'Invalid fixture field missing');tags[i]=new DxfTag(c,v);};
  switch(scenario){case 0:tags.splice(tags.findIndex((t,i)=>i>=start&&t.Code===2),1);break;case 1:set(2,'Invalid/Name');break;case 2:tags.splice(start,0,new DxfTag(2,'Duplicate'));break;case 3:tags.splice(start,0,new DxfTag(40,1));break;case 4:set(40,0);break;case 5:set(41,-1);break;case 6:set(42,0);break;case 7:set(75,2);break;case 8:set(281,7);break;case 9:set(78,3);break;case 10:set(16,0);set(26,0);set(36,0);break;case 11:set(111,0);set(121,0);set(131,0);break;case 12:set(79,7);break;case 13:set(72,0);break;}ExpectVPortInvalid(tags,binary);
}
export function VPortIncompletePoint(version,binary,first){const tags=VPortFixtureTags(version),start=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbViewportTableRecord')+1;tags.splice(tags.findIndex((t,i)=>i>=start&&t.Code===first),1);ExpectVPortInvalid(tags,binary);}
export function ExpectVPortInvalid(tags,binary){const input=new MemoryStream(RawFixtureBytes(tags,binary));try{if(GetTypedIOConfiguration()==='Debug')Throws(InvalidDataException,()=>DxfDocument.Load(input));else Check(DxfDocument.Load(input)===null,'Malformed VPORT accepted.');Check(input.CanRead,'Invalid input closed caller stream.');}finally{input.Dispose();}}
