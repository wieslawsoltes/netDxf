import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, DxfTag, MemoryStream, Underlay, UnderlayPdfDefinition, UnderlayDgnDefinition, UnderlayDwfDefinition, MathHelper } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
const T=(c,v)=>new DxfTag(c,v);
const kinds=[['PDFUNDERLAY',UnderlayPdfDefinition,'a.pdf'],['DGNUNDERLAY',UnderlayDgnDefinition,'a.dgn'],['DWFUNDERLAY',UnderlayDwfDefinition,'a.dwf']];
function fixture(kind,Type,file,binary,edit){const doc=new DxfDocument();doc.Entities.Add(new Underlay(new Type(file)));const stream=new MemoryStream();try{assert.equal(doc.Save(stream,binary),true);stream.Position=0;const raw=DxfRawDocument.Load(stream),record=raw.Sections.flatMap(s=>s.Records).find(r=>r.Name===kind);assert.ok(record);return raw.WithRecord(record,edit(Array.from(record.Tags)));}finally{stream.Dispose();}}
function load(raw,binary,reader=new DxfReader()){const stream=new MemoryStream();try{raw.Save(stream,binary);stream.Position=0;return reader.Read(stream);}finally{stream.Dispose();}}
function set(tags,code,value){const old=tags.findIndex(t=>t.Code===code);if(old>=0)tags[old]=T(code,value);else tags.push(T(code,value));return tags;}
const all=doc=>Array.from(doc.Entities.All).filter(e=>e instanceof Underlay);
for(const binary of [false,true])for(const[kind,Type,file]of kinds){
  for(const identity of [null,'0','000'])test(`${kind} without a retained definition is discarded (${identity}) / ${binary}`,()=>{
    const raw=fixture(kind,Type,file,binary,t=>identity===null?t.filter(x=>x.Code!==340):set(t,340,identity));assert.equal(all(load(raw,binary)).length,0);
  });
  test(`${kind} missing definition does not hide prior field validation / ${binary}`,()=>{
    const raw=fixture(kind,Type,file,binary,t=>set(set(t,340,'0'),281,1));assert.throws(()=>load(raw,binary),ArgumentOutOfRangeException);
  });
  test(`${kind} normalizes negative/near-zero scale without changing public setters / ${binary}`,()=>{
    const raw=fixture(kind,Type,file,binary,t=>set(set(t,41,-2),42,MathHelper.Epsilon/2)),a=all(load(raw,binary));assert.equal(a.length,1);assert.equal(a[0].Scale.X,2);assert.equal(a[0].Scale.Y,1);assert.equal(a[0].CodeName,kind);
  });
  test(`${kind} later fields and XData are read in source order / ${binary}`,()=>{
    const raw=fixture(kind,Type,file,binary,t=>[...t,T(1001,'UNDERLAY_DATA'),T(1000,'retained'),T(10,7),T(20,9),T(50,37)]),a=all(load(raw,binary))[0];assert.equal(a.Position.X,7);assert.equal(a.Position.Y,9);assert.equal(a.Rotation,37);assert.equal(a.XData.get_Item('UNDERLAY_DATA').XDataRecord.get_Item(0).Value,'retained');
  });
  test(`${kind} one clipping vertex is discarded rather than treated as a polygon / ${binary}`,()=>{
    const a=all(load(fixture(kind,Type,file,binary,t=>[...t,T(11,2),T(21,3)]),binary))[0];assert.equal(a.ClippingBoundary,null);
  });
}
