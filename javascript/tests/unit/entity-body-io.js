import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import * as io from '../../runtime/DxfTransport.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { entityBodyIOCorpus } from '../../tools/entity-body-io-corpus.mjs';
import { ValidateEntityBodyObservation } from '../../tools/entity-body-io-observation.mjs';
function reader(tags) {
  const host={text:'',WriteLine(v){this.text+=String(v??'')+'\n';},Flush(){}};
  const writer=new TextCodeValueWriter(host);
  for(const [code,value]of tags)writer.Write(code,value);
  const result=new TextCodeValueReader(host.text);result.Next();return result;
}
const document=()=>new api.DxfDocument(18);
const packet=(name,body)=>reader([[100,name],...body,[0,'ENDSEC'],[0,'EOF']]);
const throws=(action,message)=>assert.throws(action,e=>e.name==='InvalidDataException'&&e.message===message);
const capture=()=>({items:[],Write(code,value){this.items.push([code,value]);}});

test('entity body exports retain standalone function identities',async()=>{
  for(const [file,names] of [['DxfLight',['ReadLight','WriteLight','ValidateLightVersions']],['DxfAcisSat',['ReadAcisEntity','WriteAcisEntity','ValidateAcisEntities']],['DxfOleFrame',['ReadOleFrame','WriteOleFrame']],['DxfOle2Frame',['ReadOle2Frame','WriteOle2Frame']],['DxfLwPolyline',['ReadLwPolyline','WriteLwPolyline','ValidateLwPolylineFidelity']]]){
    const module=await import(`../../netDxf/IO/${file}.js`);for(const name of names)assert.equal(module[name],io[name]);
  }
});
for(const [kind,name] of [['OleFrame','AcDbOleFrame'],['Ole2Frame','AcDbOle2Frame']]){
  test(kind+' zero-length packet retains absent metadata and leaves the next record current',()=>{
    const input=packet(name,[[90,0],[1,'OLE']]),value=io['Read'+kind](input,document());
    assert.equal(value.BinaryDataLength,0);assert.equal(input.Code,0);assert.equal(input.Value,'ENDSEC');input.Next();assert.equal(input.Value,'EOF');
    assert.equal(kind==='OleFrame'?value.HasOleVersion:value.MetadataFields,kind==='OleFrame'?false:0);
  });
  test(kind+' does not allocate from an untrusted declared count',()=>{
    const input=packet(name,[[90,2147483647],[1,'OLE']]);assert.throws(()=>io['Read'+kind](input,document()),{name:'InvalidDataException'});assert.equal(input.Value,'ENDSEC');
  });
  test(kind+' emits independent Node Buffer chunks and stops on callback failure',()=>{
    const bytes=Buffer.alloc(256,7),value=kind==='OleFrame'?new api.OleFrame(bytes,1,false):new api.Ole2Frame(bytes,api.Vector3.Zero,api.Vector3.UnitX,'',2,2,0,false),chunks=[];
    const failure=new Error('writer failed');
    const writer={Write(code,data){if(code!==310)return;chunks.push(data);if(chunks.length===1)bytes.fill(9);else if(chunks.length===2)throw failure;}};
    assert.throws(()=>io['Write'+kind](writer,18,value),e=>e===failure);assert.equal(chunks.length,2);assert.equal(chunks[0].length,127);assert.ok(chunks[0].every(v=>v===7));assert.ok(chunks[1].every(v=>v===9));
    bytes.fill(1);assert.ok(chunks[1].every(v=>v===9));
  });
  test(kind+' writes data in packets of no more than 127 bytes',()=>{
    const bytes=Uint8Array.from({length:256},(_,i)=>i),value=kind==='OleFrame'?new api.OleFrame(bytes):new api.Ole2Frame(bytes,api.Vector3.Zero,api.Vector3.UnitX);
    const output=capture();io['Write'+kind](output,18,value);const parts=output.items.filter(([c])=>c===310).map(([,p])=>p);
    assert.deepEqual(parts.map(p=>p.length),[127,127,2]);assert.deepEqual(Uint8Array.from(parts.flatMap(p=>[...p])),bytes);
  });
}
test('OLE2 metadata flags distinguish omitted and explicit default values',()=>{
  const implicit=io.ReadOle2Frame(packet('AcDbOle2Frame',[[90,0],[1,'OLE']]),document());
  const explicit=io.ReadOle2Frame(packet('AcDbOle2Frame',[[70,2],[3,''],[71,2],[72,0],[90,0],[1,'OLE']]),document());
  assert.equal(implicit.MetadataFields,0);assert.equal(explicit.MetadataFields,51);assert.equal(explicit.OleVersion,implicit.OleVersion);
});
test('OLE2 validates coordinate completeness before constructing metadata',()=>{
  const input=packet('AcDbOle2Frame',[[70,-1],[10,1],[90,0],[1,'OLE']]);
  throws(()=>io.ReadOle2Frame(input,document()),'Incomplete OLE2FRAME corner.');
});
test('OLE2 preserves native wrapped metadata error',()=>{
  const input=packet('AcDbOle2Frame',[[70,-1],[90,0],[1,'OLE']]);
  assert.throws(()=>io.ReadOle2Frame(input,document()),e=>e.name==='InvalidDataException'&&e.InnerException?.name==='ArgumentOutOfRangeException'&&e.InnerException.ParamName==='oleVersion');
});
test('LIGHT error includes position and nested property validation error',()=>{
  const input=packet('AcDbLight',[[70,4]]);
  assert.throws(()=>io.ReadLight(input,document()),e=>e.name==='InvalidDataException'&&e.message==='Invalid LIGHT group 70 at position 4.'&&e.InnerException?.ParamName==='value');assert.equal(input.Code,70);
});
test('LIGHT detects duplicate fields before invoking their second setter',()=>{
  throws(()=>io.ReadLight(packet('AcDbLight',[[90,0],[90,-1]]),document()),'Duplicate LIGHT group 90.');
});
test('LIGHT ignores unknown scalar groups but rejects extension markers and orphan XData',()=>{
  assert.equal(io.ReadLight(packet('AcDbLight',[[300,'opaque scalar']]),document()).Intensity,1);
  throws(()=>io.ReadLight(packet('AcDbLight',[[102,'{APP']]),document()),'Unsupported or duplicate LIGHT subclass/payload marker.');
  throws(()=>io.ReadLight(packet('AcDbLight',[[1000,'orphan']]),document()),'LIGHT XData must start with an application registry.');
});
test('entity XData is bound to document registries and preserves resolver side effects after later failure',()=>{
  const doc=document(),input=packet('AcDbLight',[[1001,'NEW_APP'],[1000,'value'],[70,4]]),before=doc.DrawingVariables.HandleSeed;
  assert.throws(()=>io.ReadLight(input,doc),{name:'InvalidDataException'});assert.ok(doc.ApplicationRegistries.Contains('NEW_APP'));assert.notEqual(doc.DrawingVariables.HandleSeed,before);
});
test('entity XData decoding is one-pass and repeated application groups retain record order',()=>{
  const doc=document(),value=io.ReadLight(packet('AcDbLight',[[1001,'APP'],[1000,'\\U+005CU+0041'],[1001,'APP'],[1070,4]]),doc);
  const data=value.XData.get_Item('APP');assert.equal(data.ApplicationRegistry,doc.ApplicationRegistries.get_Item('APP'));assert.equal(data.XDataRecord.get_Item(0).Value,'\\U+0041');assert.equal(data.XDataRecord.get_Item(1).Value,4);
});
test('XData writer independently copies even its final zero-length and Buffer-backed packets',()=>{
  const data=new api.XData(new api.ApplicationRegistry('APP'));data.XDataRecord.Add(new api.XDataRecord(api.XDataCode.BinaryData,Buffer.alloc(127,6)));data.XDataRecord.Add(new api.XDataRecord(api.XDataCode.BinaryData,Buffer.alloc(127,6)));data.XDataRecord.Add(new api.XDataRecord(api.XDataCode.BinaryData,new Uint8Array()));
  const list=new api.Line().XData;list.Add(data);const out=capture();WriteXData(out,18,list);const parts=out.items.filter(([c])=>c===1004).map(([,v])=>v);
  assert.deepEqual(parts.map(p=>p.length),[127,127,0]);assert.notEqual(parts[0].buffer,parts[1].buffer);
});
test('SAT read/write retains encoded text without generic Unicode decoding',()=>{
  const doc=new api.DxfDocument(16),input=packet('AcDbModelerGeometry',[[70,1],[1,'abc\\U+0042']]),value=io.ReadAcisEntity(input,doc,'BODY');
  assert.equal(value.EncodedSatChunks.get_Item(0).Text,'abc\\U+0042');const out=capture();io.WriteAcisEntity(out,16,value);assert.equal(out.items[2][1],'abc\\U+0042');
});
test('SAT refusal occurs before reading the subclass for SAB profiles',()=>{
  const input={Code:100,ReadString(){throw new Error('Should not read');}};
  assert.throws(()=>io.ReadAcisEntity(input,document(),'BODY'),{name:'NotSupportedException'});
});
test('SAT keeps separate encoded-chunk and encoded-payload validation errors',()=>{
  const doc=new api.DxfDocument(16);
  assert.throws(()=>io.ReadAcisEntity(packet('AcDbModelerGeometry',[[70,1],[1,'Ω']]),doc,'BODY'),e=>e.message==='Invalid ACIS SAT chunk.'&&e.InnerException?.name==='ArgumentException');
  assert.throws(()=>io.ReadAcisEntity(packet('AcDbModelerGeometry',[[70,1],[1,'^']]),doc,'BODY'),e=>e.message==='Invalid encoded ACIS SAT payload.'&&e.InnerException?.name==='ArgumentException');
});
test('SAT private history rejection does not consume the invalid handle',()=>{
  const input=packet('AcDbModelerGeometry',[[70,1],[1,'abc'],[100,'AcDb3dSolid'],[350,'C0']]);
  assert.throws(()=>io.ReadAcisEntity(input,new api.DxfDocument(16),'3DSOLID'),{name:'NotSupportedException'});assert.equal(input.Code,350);assert.equal(input.Value,'C0');
});
test('LWPOLYLINE retains negative-zero widths, identifiers and repeated bulge last-write state',()=>{
  const value=io.ReadLwPolyline(packet('AcDbPolyline',[[90,1],[43,-0],[10,-0],[20,1],[91,7],[40,-0],[41,0],[42,.25],[42,.5]]),document()),vertex=value.Vertexes.get_Item(0);
  assert.ok(Object.is(value.ConstantWidth,-0));assert.ok(Object.is(vertex.StartWidthOverride,-0));assert.equal(vertex.EndWidthOverride,0);assert.equal(vertex.VertexIdentifier,7);assert.equal(vertex.Bulge,.5);
});
test('LWPOLYLINE empty huge-count and incomplete-point errors never synthesize vertices',()=>{
  throws(()=>io.ReadLwPolyline(packet('AcDbPolyline',[[90,2147483647]]),document()),'LWPOLYLINE group 90 does not match its actual vertex count.');
  throws(()=>io.ReadLwPolyline(packet('AcDbPolyline',[[90,1],[10,1]]),document()),'LWPOLYLINE final vertex is missing group 20.');
});
test('output version validators visit unused block definitions',()=>{
  const doc=new api.DxfDocument(13),block=new api.Block('Unused'),light=new api.Light();block.Entities.Add(light);doc.Blocks.Add(block);
  assert.throws(()=>io.ValidateLightVersions(doc),{name:'NotSupportedException'});
  const poly=new api.Polyline2D([new api.Polyline2DVertex(0,0)]);poly.Vertexes.get_Item(0).VertexIdentifier=1;block.Entities.Add(poly);assert.throws(()=>io.ValidateLwPolylineFidelity(doc),{name:'NotSupportedException'});
});
test('nested synchronous OLE writes cannot overwrite outer payload packets',()=>{
  const outer=new api.OleFrame(Buffer.alloc(130,3)),inner=new api.OleFrame(Buffer.alloc(129,9)),out=[];let entered=false;
  const writer={Write(code,value){if(code===310&&!entered){entered=true;io.WriteOleFrame(writer,18,inner);}out.push([code,value]);}};io.WriteOleFrame(writer,18,outer);
  const packets=out.filter(([code])=>code===310).map(([,v])=>v);assert.deepEqual(packets.map(p=>[p.length,p[0]]),[[127,9],[2,9],[127,3],[3,3]]);
});
test('entity body corpus is complete deterministic and input-only',()=>{
  const corpus=entityBodyIOCorpus();assert.deepEqual(corpus,entityBodyIOCorpus());assert.equal(corpus.length,1107);assert.equal(new Set(corpus.map(p=>p.name)).size,1107);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),3714);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
});
for(const invalid of [null,[],[{}],[{ok:true,value:{}}]])test('entity body observation rejects incomplete response '+JSON.stringify(invalid),()=>assert.throws(()=>ValidateEntityBodyObservation(invalid,1)));
