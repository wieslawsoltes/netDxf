import { retainedRecordIOCall } from '../../tools/retained-record-io-wire.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import * as io from '../../runtime/DatabasePayloadIO.js';
import { sectionSettingsPacket, sectionGeometryPacket, retainedRecordIOCorpus } from '../../tools/retained-record-io-corpus.mjs';
import { ValidateDatabaseObservation } from '../../tools/database-io-observation.mjs';
import { WireInput } from '../../tools/database-payload-values.mjs';
const doc=()=>new api.DxfDocument(18);
const tag=(code,value)=>new api.DxfTag(code,value);
function fixture() {
  const document=doc(),line=new api.Line(),section=new api.Section();document.Entities.Add(line);document.Entities.Add(section);
  const context=new io.DatabaseIOContext(document),root=document.NamedObjects,resources={line,section,block:line.Owner.Record,root};
  const source=item=>{const identity=new io.SourceRecordIdentity();identity.IdentitySeen=true;identity.Handle=BigInt('0x'+item.Handle);context.sourceObjectIdentities.add(identity.Handle);context.RecordSourceObject(item,identity);};
  for(const item of document.AddedObjects.Values)if(item.Handle!=='0')source(item);
  const payloadTags=packet=>packet.map(([c,v])=>tag(c,typeof v==='number'?v:WireInput(v,name=>resources[name])));
  const tags=packet=>[tag(5,'C00'),...payloadTags(packet)];
  const register=(item,name='VALUE')=>{item.Owner=root;document.Objects.Register(item,item.Handle!==null);root.AddLoaded(name,item,true);source(item);};
  return {document,context,resources,tags,payloadTags,register,source};
}
const capture=()=>({values:[],Write(code,value){this.values.push([code,value]);}});
test('section codecs retain standalone export identity',async()=>{
  for(const name of ['DxfReader.SectionSettings','DxfWriter.SectionSettings','DxfReader.SectionManager','DxfWriter.SectionManager']) {
    const module=await import(`../../netDxf/IO/${name}.js`);for(const key of Object.keys(module))assert.equal(io[key],module[key]);
  }
});
for(const alias of ['SECTIONSETTINGS','SECTION_SETTINGS'])test(alias+' binds repeated and null source references after parsing',()=>{
  const f=fixture(),record=io.ReadSectionSettingsRecord(f.context,alias,f.tags(sectionSettingsPacket()));
  assert.equal(record.Object.CodeName,alias);assert.equal(record.Object.TypeSettings.Count,0);assert.equal(f.context.pendingSectionSettings.size,1);
  f.register(record.Object);io.ResolveSectionSettingsReferences(f.context);
  const type=record.Object.TypeSettings.get_Item(0);assert.deepEqual([...type.SourceObjects],[f.resources.line,null,f.resources.line]);assert.equal(type.DestinationBlock,f.resources.block);
  assert.equal(type.GeometrySettings.Count,2);assert.ok(Object.is(type.GeometrySettings.get_Item(0).LinetypeScale,-0));
});
for(const count of [0,1,2,3])for(const repeat of [false,true])test(`section geometry marker preservation ${count}/${repeat}`,()=>{
  const f=fixture(),record=io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',f.tags(sectionSettingsPacket({count,repeat})));
  f.register(record.Object);io.ResolveSectionSettingsReferences(f.context);const output=capture();io.WriteSectionSettingsPayload(output,18,record.Object);
  const markers=output.values.filter(([c,v])=>c===2&&v==='SectionGeometrySettings').length;
  // With one geometry bundle, native admission chooses canonical repeated markers.
  assert.equal(markers,count===0?1:repeat||count===1?count:1);
});
test('section settings private geometry color ambiguity retains the whole opaque payload',()=>{
  const f=fixture(),packet=sectionSettingsPacket({count:1});packet.splice(packet.findIndex(([c])=>c===63),0,[62,7]);
  const input=f.tags(packet),record=io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',input);
  assert.ok(record.Object instanceof api.DxfOpaqueObject);assert.deepEqual([...record.Object.Tags],input.slice(1));assert.equal(f.context.pendingSectionSettings.size,0);
});
test('section known duplicate field remains a public grammar error rather than opaque fallback',()=>{
  const f=fixture(),packet=sectionSettingsPacket();packet.splice(2,0,[90,1]);
  assert.throws(()=>io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',f.tags(packet)),error=>error.name==='FormatException'&&error.message.includes('repeats group 90'));
  assert.equal(f.context.pendingSectionSettings.size,0);
});
test('section mixed geometry marker grammars are rejected',()=>{
  const f=fixture(),packet=sectionSettingsPacket({count:3}),markers=packet.flatMap(([c,v],i)=>c===2&&v==='SectionGeometrySettings'?[i]:[]);packet.splice(markers[2],1);
  assert.throws(()=>io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',f.tags(packet)),error=>error.name==='FormatException'&&error.message.includes('mixes repeated and sequence'));
});
test('section huge declared counts do not allocate absent bundles',()=>{
  const f=fixture();assert.throws(()=>io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',f.tags([[100,'AcDbSectionSettings'],[90,1],[91,2147483647]])),{name:'FormatException'});
  assert.equal(f.context.pendingSectionSettings.size,0);
});
test('section wrapped geometry setter errors preserve inner argument name',()=>{
  const f=fixture(),packet=sectionSettingsPacket({count:1});packet[packet.findIndex(([c])=>c===63)]=[63,257];
  assert.throws(()=>io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',f.tags(packet)),error=>error.name==='FormatException'&&error.InnerException?.ParamName==='value');
});
test('unaccepted generated resources do not satisfy section references',()=>{
  const f=fixture(),record=io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',f.tags(sectionSettingsPacket()));f.register(record.Object);
  f.context.acceptedSourceObjects.delete(BigInt('0x'+f.resources.line.Handle));
  assert.throws(()=>io.ResolveSectionSettingsReferences(f.context),error=>error.message.includes('absent from the source file'));
  assert.equal(record.Object.TypeSettings.Count,0);
});
test('failed section destination binding is retryable without partially applied type settings',()=>{
  const f=fixture(),record=io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',f.tags(sectionSettingsPacket({types:2})));f.register(record.Object);
  const key=BigInt('0x'+f.resources.block.Handle),old=f.context.acceptedSourceObjects.get(key);f.context.acceptedSourceObjects.delete(key);
  assert.throws(()=>io.ResolveSectionSettingsReferences(f.context),error=>error.message.includes('actual source BLOCK_RECORD'));assert.equal(record.Object.TypeSettings.Count,0);
  f.context.acceptedSourceObjects.set(key,old);io.ResolveSectionSettingsReferences(f.context);assert.equal(record.Object.TypeSettings.Count,2);
});
test('section XData admission before a later failure retains the source registry effect',()=>{
  const f=fixture(),input=f.tags([...sectionSettingsPacket(),[1001,'EARLY_APP'],[1000,'value'],[1,'invalid after xdata']]);
  assert.throws(()=>io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',input),{name:'FormatException'});assert.ok(f.document.ApplicationRegistries.Contains('EARLY_APP'));assert.equal(f.context.pendingSectionSettings.size,0);
});
test('section output preflight rejects unsupported versions before emitting bytes',()=>{
  const output=capture();assert.throws(()=>io.WriteSectionSettingsPayload(output,14,new api.DxfSectionSettings()),{name:'NotSupportedException'});assert.equal(output.values.length,0);
});
test('section writer propagates callback failure and only retains completed output',()=>{
  const f=fixture(),record=io.ReadSectionSettingsRecord(f.context,'SECTIONSETTINGS',f.tags(sectionSettingsPacket()));f.register(record.Object);io.ResolveSectionSettingsReferences(f.context);
  const output=[],failure=new Error('sink');assert.throws(()=>io.WriteSectionSettingsPayload({Write(c,v){if(c===331)throw failure;output.push([c,v]);}},18,record.Object),e=>e===failure);
  assert.equal(output.at(-1)[0],330);assert.equal(record.Object.TypeSettings.Count,1);
});
for(const alias of ['SECTION_MANAGER','SECTIONMANAGER'])test(alias+' binds repeated actual section identities to the named root entry',()=>{
  const f=fixture(),record=io.ReadSectionManagerRecord(f.context,alias,f.tags([[100,'AcDbSectionManager'],[70,1],[90,2],[330,{h:'section'}],[330,{h:'section',pad:2,lower:true}]]));
  assert.equal(record.Object.Sections.Count,0);f.register(record.Object,'ACAD_SECTION_MANAGER');io.ResolveSectionManagerReferences(f.context);
  assert.deepEqual([...record.Object.Sections],[f.resources.section,f.resources.section]);assert.equal(f.document.Entities.Remove(f.resources.section),false);
});
test('section manager maximum membership parses without resolving null pointers',()=>{
  const f=fixture(),count=api.DxfStoredSectionManager.MaximumSections;
  const input=[tag(5,'C00'),tag(100,'AcDbSectionManager'),tag(70,0),tag(90,count),...Array.from({length:count},()=>tag(330,'0'))];
  const record=io.ReadSectionManagerRecord(f.context,'SECTION_MANAGER',input);assert.equal(record.Object.Tags.Count,count+3);assert.equal(record.Object.Sections.Count,0);
  assert.throws(()=>io.ReadSectionManagerRecord(f.context,'SECTION_MANAGER',[tag(5,'C00'),tag(100,'AcDbSectionManager'),tag(70,0),tag(90,count+1)]),{name:'FormatException'});
});
test('section manager unsupported flags and old profiles remain wholly opaque',()=>{
  const f=fixture();for(const [version,flag]of [[18,2],[14,0]]){f.document.DrawingVariables.AcadVer=version;const record=io.ReadSectionManagerRecord(f.context,'SECTION_MANAGER',f.tags([[100,'AcDbSectionManager'],[70,flag],[90,0]]));assert.ok(record.Object instanceof api.DxfOpaqueObject);}
  assert.equal(f.context.storedSectionManagers.length,0);
});
test('manager duplicate padded reactor identities are rejected numerically',()=>{
  const f=fixture(),input=f.tags([[102,'{ACAD_REACTORS'],[330,{h:'root'}],[330,{h:'root',pad:2,lower:true}],[102,'}'],[100,'AcDbSectionManager'],[70,0],[90,0]]);
  assert.throws(()=>io.ReadSectionManagerRecord(f.context,'SECTION_MANAGER',input),error=>error.message.includes('repeats a persistent-reactor'));
});
test('manager CLASS preparation preserves compatible identity and rejects live conflicts',()=>{
  const f=fixture(),record=io.ReadSectionManagerRecord(f.context,'SECTION_MANAGER',f.tags([[100,'AcDbSectionManager'],[70,0],[90,0]]));f.register(record.Object,'ACAD_SECTION_MANAGER');io.ResolveSectionManagerReferences(f.context);
  const entry=new api.DxfClass('SECTION_MANAGER','AcDbSectionManager','Custom');entry.InstanceCount=7;f.document.Classes.Add(entry);io.PrepareSectionManagerClasses(f.document,f.document.Classes);
  assert.equal(entry.InstanceCount,1);assert.equal(entry.ApplicationName,'Custom');entry.IsEntity=true;
  assert.throws(()=>io.PrepareSectionManagerClasses(f.document,f.document.Classes),{name:'InvalidDataException'});assert.equal(f.document.Classes.get_Item('SECTION_MANAGER'),entry);
});
test('manager CLASS preparation ignores private-only names rather than rewriting their declarations',()=>{
  const f=fixture(),opaque=new api.DxfOpaqueObject('SECTION_MANAGER',[tag(100,'Private')]);f.register(opaque);
  const entry=new api.DxfClass('SECTION_MANAGER','Private','Custom');entry.InstanceCount=9;f.document.Classes.Add(entry);io.PrepareSectionManagerClasses(f.document,f.document.Classes);assert.equal(entry.InstanceCount,9);
});
test('FIELD leading-header parser retains partial out values when a count fails',()=>{
  const f=fixture(),out={};assert.throws(()=>io.TryReadStoredFieldHeader(f.payloadTags([[1,'Evaluator'],[2,'text\\U+'],[3,'03A9'],[90,2],[360,'A']]),0,5,out),{name:'FormatException'});
  assert.equal(out.evaluator,'Evaluator');assert.equal(out.code,'textΩ');assert.deepEqual(out.children,['A']);assert.deepEqual(out.objects,[]);
});
test('ordinary XRECORD is not falsely admitted by the private-record fallback',()=>{
  const f=fixture(),out={};assert.equal(io.TryReadPrivateXRecord(f.context,f.tags([[100,'AcDbXrecord'],[280,1],[1,'text']]),out),false);assert.equal(out.value,null);
});
test('private XRECORD nested group-1001 remains payload rather than application metadata',()=>{
  const f=fixture(),out={},input=f.tags([[100,'AcDbXrecord'],[102,'{Private'],[1001,'NOT_APP'],[1000,'value'],[102,'}'],[1001,'APP'],[1000,'actual']]);
  assert.equal(io.TryReadPrivateXRecord(f.context,input,out),true);assert.ok(out.value.Object instanceof api.DxfOpaqueObject);assert.equal(f.document.ApplicationRegistries.Contains('NOT_APP'),false);assert.equal(out.value.Object.XData.Count,1);
});
test('retained VBA envelope snapshots caller buffers and honors its byte count',()=>{
  const f=fixture(),record=new io.DatabaseRecord(),bytes=Buffer.from([1,2,3]);assert.equal(io.ReadStoredEnvelopePayload(f.context,record,'VBA_PROJECT',[tag(100,'AcDbVbaProject'),tag(90,3),tag(310,bytes)],0),true);
  bytes.fill(9);const output=capture();io.WriteStoredEnvelopePayload(output,18,record.Object);assert.deepEqual([...output.values.at(-1)[1]],[1,2,3]);
});
test('reconstructed retained-record corpus contains unique deterministic inputs only',()=>{
  const corpus=retainedRecordIOCorpus();assert.deepEqual(corpus,retainedRecordIOCorpus());assert.equal(corpus.length,2593);assert.equal(new Set(corpus.map(p=>p.name)).size,2593);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),5909);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
});

for(const invalid of [null,[],[{}],[{ok:true,value:{}}]])test('retained-record observation rejects incomplete state '+JSON.stringify(invalid),()=>{
  assert.throws(()=>ValidateDatabaseObservation(invalid,1,'payload'));
});

for(const probe of retainedRecordIOCorpus().filter(p=>p.name.endsWith('profile/text/18')))test(probe.request.kind+' valid record is admitted resolved written and validated',()=>{
  const rows=retainedRecordIOCall(probe.request);assert.equal(rows.length,5);
  assert.ok(rows.every(row=>row.value.error===null));assert.equal(rows[3].value.result,true);assert.deepEqual(rows[4].value.result,[]);
});
