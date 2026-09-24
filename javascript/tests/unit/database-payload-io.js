import test from 'node:test';import assert from 'node:assert/strict';
import * as api from '../../index.js';import * as io from '../../runtime/DatabasePayloadIO.js';
import { SourceHandle } from '../../netDxf/IO/DxfReader.SourceIdentity.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { databasePayloadCorpus,spatialPacket,dataPacket } from '../../tools/database-payload-corpus.mjs';
import { sourceMetadataCorpus } from '../../tools/source-metadata-corpus.mjs';
const tags=values=>values.map(([code,value])=>new api.DxfTag(code,value));
const make=()=>{const document=new api.DxfDocument(18),line=new api.Line();document.Entities.Add(line);return {document,line,context:new io.DatabaseIOContext(document)};};
function accept(context,item){const source=new io.SourceRecordIdentity();source.Handle=BigInt('0x'+item.Handle);source.IdentitySeen=true;context.sourceObjectIdentities.add(source.Handle);context.RecordSourceObject(item,source);return source;}
const output=()=>({tags:[],Write(c,v){this.tags.push([c,v]);}});
const fmt=(run,message)=>assert.throws(run,e=>e.name==='FormatException'&&e.message===message);
const header=[[5,'CA0'],[330,'0']];

test('database payload APIs share standalone and DxfTransport function identities',async()=>{
  for(const name of ['Containers','DataTable','LayerIndex','LayerFilterPointer','LightList']){
    const r=await import(`../../netDxf/IO/DxfReader.${name}.js`),w=await import(`../../netDxf/IO/DxfWriter.${name}.js`);
    for(const [key,value]of Object.entries({...r,...w}))if(key in io)assert.equal(value,api.DxfTransport[key]);
  }
});
test('IDBUFFER retains null slots, repeated pointers and already bound prefix after failure',()=>{
  const {context,line}=make(),record=new io.DatabaseRecord();accept(context,line);
  io.ReadContainerPayload(context,record,'IDBUFFER',tags([[100,'AcDbIdBuffer'],[330,line.Handle],[330,'0'],[330,line.Handle],[330,'ABC']]));
  fmt(()=>io.ResolveContainerReferences(context,record),'Unresolved IDBUFFER reference: ABC');assert.deepEqual([...record.Object.References],[line,null,line]);
  assert.equal(record.ContainerReferences.length,4);
});
test('IDBUFFER malformed fields retain its allocated object and parsed reference slots',()=>{
  const {context}=make(),record=new io.DatabaseRecord();fmt(()=>io.ReadContainerPayload(context,record,'IDBUFFER',tags([[100,'AcDbIdBuffer'],[330,'A'],[1,'bad']])),'IDBUFFER contains an unsupported field.');
  assert.ok(record.Object instanceof api.DxfIdBuffer);assert.deepEqual(record.ContainerReferences,['A']);
});
test('IDBUFFER does not conflate a padded null handle with its exact null sentinel',()=>{
  const {context}=make(),record=new io.DatabaseRecord();io.ReadContainerPayload(context,record,'IDBUFFER',tags([[100,'AcDbIdBuffer'],[330,'00']]));
  fmt(()=>io.ResolveContainerReferences(context,record),'Unresolved IDBUFFER reference: 00');
});
test('SORTENTSTABLE keys are retained as values, not resolved object pointers',()=>{
  const {context,line}=make(),record=new io.DatabaseRecord();accept(context,line);accept(context,line.Owner.Record);
  io.ReadContainerPayload(context,record,'SORTENTSTABLE',tags([[100,'AcDbSortentsTable'],[330,line.Owner.Record.Handle],[331,line.Handle],[5,'DEADBEEF']]));io.ResolveContainerReferences(context,record);
  assert.equal(record.Object.Entries.get_Item(0).SortHandle,'DEADBEEF');assert.equal(record.Object.Entries.get_Item(0).Entity,line);
});
test('SORTENTSTABLE rejects duplicate entities through the actual collection without rollback',()=>{
  const {context,line}=make(),record=new io.DatabaseRecord();accept(context,line);accept(context,line.Owner.Record);
  io.ReadContainerPayload(context,record,'SORTENTSTABLE',tags([[100,'AcDbSortentsTable'],[330,line.Owner.Record.Handle],[331,line.Handle],[5,'1'],[331,line.Handle],[5,'2']]));
  assert.throws(()=>io.ResolveContainerReferences(context,record),{name:'ArgumentException'});assert.equal(record.Object.Entries.Count,1);
});
for(const front of [false,true])for(const back of [false,true])test(`SPATIAL_FILTER affine matrices and clip presence ${front}/${back}`,()=>{
  const filter=io.ReadSpatialFilterPayload(tags(spatialPacket(front,back))),out=output();io.WriteContainerPayload(out,filter);
  assert.equal(filter.FrontClippingDistance!==null,front);assert.equal(filter.BackClippingDistance!==null,back);
  if(front)assert.ok(Object.is(filter.FrontClippingDistance,-0));if(back)assert.ok(Object.is(filter.BackClippingDistance,-0));
  assert.ok(filter.InverseInsertTransform.IsIdentity);assert.ok(filter.ClipBoundaryTransform.IsIdentity);
  const copy=io.ReadSpatialFilterPayload(tags(out.tags));assert.deepEqual([...copy.Boundary].map(v=>v.ToArray()),[[-1,-1],[1,1]]);
});
test('SPATIAL_FILTER reports coordinates before assigning or allocating a result object',()=>{
  const {context}=make(),record=new io.DatabaseRecord();fmt(()=>io.ReadContainerPayload(context,record,'SPATIAL_FILTER',tags([[100,'AcDbFilter'],[100,'AcDbSpatialFilter'],[10,2],[10,3]])),'SPATIAL_FILTER boundary point is missing group 20.');assert.equal(record.Object,null);
});
test('SPATIAL_FILTER writer retains the completed prefix when callback rejects a matrix',()=>{
  const filter=io.ReadSpatialFilterPayload(tags(spatialPacket())),out=[],failure=new Error('writer');
  assert.throws(()=>io.WriteContainerPayload({Write(c,v){if(c===40)throw failure;out.push([c,v]);}},filter),e=>e===failure);assert.equal(out.at(-1)[0],73);
});
test('unrecognized container type leaves both record and context untouched',()=>{
  const {context}=make(),record=new io.DatabaseRecord();assert.equal(io.ReadContainerPayload(context,record,'PRIVATE',[],0),false);assert.equal(record.Object,null);
});
test('common stored header separates ownership/reactors/extension without decoding private fields',()=>{
  const packet=tags([[5,'A'],[330,'B'],[102,'{ACAD_REACTORS'],[330,'C'],[330,'D'],[102,'}'],[102,'{ACAD_XDICTIONARY'],[360,'E'],[102,'}'],[102,'{PRIVATE'],[330,'F'],[102,'}'],[100,'Payload']]);
  const r=io.ReadStoredObjectHeader(packet);assert.equal(r.handle,'A');assert.equal(r.payload,12);assert.deepEqual(r.record.Metadata,{Owner:'B',Extension:'E',Reactors:['C','D']});assert.deepEqual(r.opaque.map(t=>t.Value),['{PRIVATE','F','}']);
});
test('common stored header rejects duplicate same owner but preserves a distinct private owner',()=>{
  fmt(()=>io.ReadStoredObjectHeader(tags([[5,'A'],[330,'B'],[330,'b']])),'Duplicate object owner.');
  const parsed=io.ReadStoredObjectHeader(tags([[5,'A'],[330,'B'],[330,'C']]));assert.equal(parsed.opaque[0].Value,'C');
});
test('OBJECT_PTR distinguishes an empty public payload from private data',()=>{
  const {context}=make();assert.ok(io.ReadLayerFilterPointerRecord(context,'OBJECT_PTR',tags(header)).Object instanceof api.DxfObjectPointer);
  const opaque=io.ReadLayerFilterPointerRecord(context,'OBJECT_PTR',tags([...header,[100,'Private'],[340,'DEAD']]));assert.ok(opaque.Object instanceof api.DxfOpaqueObject);assert.equal(opaque.Object.Tags.get_Item(1).Value,'DEAD');
});
test('LAYER_FILTER retains repeated names and one-pass escaped text',()=>{
  const {context}=make(),r=io.ReadLayerFilterPointerRecord(context,'LAYER_FILTER',tags([...header,[100,'AcDbFilter'],[100,'AcDbLayerFilter'],[8,'Layer\\U+005CU+0041'],[8,'Layer']]));
  assert.deepEqual([...r.Object.LayerNames],['Layer\\U+0041','Layer']);const out=output();io.WriteLayerFilterPointerPayload(out,18,r.Object);assert.equal(out.tags[2][1],'Layer\\U+005CU+0041');
});
test('large private headers avoid spread-call stack limits and preserve all tags',()=>{
  const {context}=make(),packet=[...header,...Array.from({length:70000},()=>[300,'private']),[100,'AcDbFilter'],[100,'AcDbLayerFilter']];
  const record=io.ReadLayerFilterPointerRecord(context,'LAYER_FILTER',tags(packet));assert.ok(record.Object instanceof api.DxfOpaqueObject);assert.equal(record.Object.Tags.Count,70002);
});
test('database XData parsing retains application-registry side effects before later failure',()=>{
  const {document,context}=make(),record=new io.DatabaseRecord();fmt(()=>io.ReadContainerPayload(context,record,'IDBUFFER',tags([[100,'AcDbIdBuffer'],[1001,'NEW'],[1000,'value'],[8,'bad']])),'Invalid object XData.');
  assert.ok(document.ApplicationRegistries.Contains('NEW'));assert.equal(record.Object.XData.get_Item('NEW').XDataRecord.Count,1);
});
test('DATATABLE dimension validation runs before name, column allocation or pending-reference admission',()=>{
  const {context}=make();fmt(()=>io.ReadDataTableRecord(context,tags([...header,[100,'AcDbDataTable'],[70,2],[90,1048576],[91,2]])),'DATATABLE dimensions exceed the admitted range.');assert.equal(context.dataTableReferences.length,0);
});
test('DATATABLE unknown version/cell type remains wholly opaque including XData',()=>{
  const {document,context}=make();const packet=tags([...header,...dataPacket([[12,'Private',[[300,'data']]]],1),[1001,'NOT_BOUND'],[1000,'opaque']]);
  const r=io.ReadDataTableRecord(context,packet);assert.ok(r.Object instanceof api.DxfOpaqueObject);assert.equal(document.ApplicationRegistries.Contains('NOT_BOUND'),false);assert.equal(context.dataTableReferences.length,0);
});
test('DATATABLE validates scalar Unicode before retaining deferred columns',()=>{
  const {context}=make();assert.throws(()=>io.ReadDataTableRecord(context,tags([...header,...dataPacket([[3,'Text',[[3,'\\U+D800']]]],1)])),e=>e.name==='FormatException'&&e.InnerException?.name==='ArgumentException');assert.equal(context.dataTableReferences.length,0);
});
for(const [type,cells,expected]of [[1,[[93,-2147483648]],-2147483648],[2,[[40,-0]],-0],[3,[[3,'literal\\U+005CU+0041']],'literal\\U+0041'],[4,[[10,1],[20,2],[30,3]],[1,2,3]],[10,[[71,1]],true],[11,[[11,1],[21,2],[31,3]],[1,2,3]]])test('DATATABLE scalar column '+type+' roundtrips its exact stored type',()=>{
  const {context}=make(),r=io.ReadDataTableRecord(context,tags([...header,...dataPacket([[type,'column',cells]],1)]));assert.equal(r.Object.Columns.Count,0);io.ResolveDataTableReferences(context);
  const value=r.Object.Columns.get_Item(0).Values.get_Item(0);assert.deepEqual(value instanceof api.Vector3?value.ToArray():value,expected);const out=output();assert.equal(io.WriteDataTablePayload(out,18,r.Object),true);assert.equal(out.tags[5][1],type);
});
test('DATATABLE binds exact accepted references and atomically rejects nonreciprocal owned cells',()=>{
  const {document,context}=make(),leaf=new api.DxfPlaceholder();document.NamedObjects.Add('leaf',leaf);accept(context,leaf);
  const r=io.ReadDataTableRecord(context,tags([...header,...dataPacket([[6,'Owner',[[360,leaf.Handle]]]],1)]));r.Object.Owner=document.NamedObjects;document.Objects.Register(r.Object,true);
  assert.throws(()=>io.ResolveDataTableReferences(context),{name:'FormatException'});assert.equal(r.Object.Columns.Count,0);
  document.NamedObjects.Remove('leaf');leaf.Owner=r.Object;io.ResolveDataTableReferences(context);assert.equal(r.Object.Columns.get_Item(0).Values.get_Item(0),leaf);
});
test('DATATABLE writer callback changes do not rewrite an already obtained column snapshot',()=>{
  const table=new api.DxfDataTable();table.SetColumns(1,[new api.DxfDataColumn(1,'original',[7])]);const out=[];
  io.WriteDataTablePayload({Write(c,v){out.push([c,v]);if(c===92)table.SetColumns(1,[new api.DxfDataColumn(1,'new',[9])]);}},18,table);
  assert.deepEqual(out.slice(-2),[[2,'original'],[93,7]]);assert.equal(table.Columns.get_Item(0).Name,'new');
});
test('LIGHTLIST deferred binding preserves repeated targets and partial additions',()=>{
  const {document,context}=make(),light=new api.Light();document.Entities.Add(light);const r=new io.DatabaseRecord();
  io.ReadLightListPayload(context,r,'LIGHTLIST',tags([[100,'AcDbLightList'],[90,-1],[90,3],[5,light.Handle],[1,'A'],[5,light.Handle],[1,'B'],[5,'DEAD'],[1,'C']]));
  fmt(()=>io.ResolveLightListReferences(context),'LIGHTLIST reference does not identify a LIGHT: DEAD');assert.equal(r.Object.Entries.Count,2);assert.equal(r.Object.StoredVersion,-1);
});
test('LIGHTLIST private extension returns false without partial typed admission',()=>{
  const {context}=make(),record=new io.DatabaseRecord();assert.equal(io.ReadLightListPayload(context,record,'LIGHTLIST',tags([[100,'AcDbLightList'],[90,1],[90,0],[300,'private']])),false);assert.equal(record.Object,null);assert.equal(context.lightListReferences.length,0);
});
test('LAYER_INDEX uses reciprocal ownership and dynamic IDBUFFER counts',()=>{
  const {document,line,context}=make(),buffer=new api.DxfIdBuffer();buffer.References.Add(line);document.NamedObjects.Add('B',buffer);accept(context,buffer);
  const r=io.ReadLayerIndexRecord(context,tags([...header,[100,'AcDbIndex'],[40,-0],[100,'AcDbLayerIndex'],[8,'A'],[360,buffer.Handle],[90,1]]));r.Object.Owner=document.NamedObjects;document.Objects.Register(r.Object,true);
  fmt(()=>io.ResolveLayerIndexReferences(context),'LAYER_INDEX ownership target must be a reciprocally owned IDBUFFER: '+buffer.Handle);
  document.NamedObjects.Remove('B');buffer.Owner=r.Object;io.ResolveLayerIndexReferences(context);assert.ok(Object.is(r.Object.Timestamp,-0));assert.equal(r.Object.Entries.get_Item(0).Count,1);
  buffer.References.Clear();const out=output();io.WriteLayerIndexPayload(out,18,r.Object);assert.deepEqual(out.tags.at(-1),[90,0]);
});
test('LAYER_INDEX undocumented leading group 90 remains opaque instead of being guessed',()=>{
  const {context}=make();const r=io.ReadLayerIndexRecord(context,tags([...header,[100,'AcDbIndex'],[40,0],[100,'AcDbLayerIndex'],[90,0]]));assert.ok(r.Object instanceof api.DxfOpaqueObject);
});
test('class preparation preserves private declarations without a typed instance',()=>{
  const {document}=make(),definition=new api.DxfClass('LIGHTLIST','Private','User');definition.InstanceCount=73;document.Classes.Add(definition);
  io.PrepareLightListClass(document,document.Classes);assert.equal(definition.InstanceCount,73);
  document.NamedObjects.Add('Typed',new api.DxfLightList(1));assert.throws(()=>io.PrepareLightListClass(document,document.Classes),{name:'InvalidDataException'});assert.equal(definition.InstanceCount,73);
});
test('class preparation counts opaque instances but only creates a declaration for known typed data',()=>{
  const {document}=make();document.NamedObjects.Add('Opaque',new api.DxfOpaqueObject('DATATABLE',tags([[100,'Private']])));io.PrepareDataTableClass(document,document.Classes);assert.equal(document.Classes.Count,0);
  document.NamedObjects.Add('Typed',new api.DxfDataTable());io.PrepareDataTableClass(document,document.Classes);assert.equal(document.Classes.get_Item('DATATABLE').InstanceCount,2);assert.equal(document.Classes.get_Item('DATATABLE').ApplicationName,'ObjectDBX Classes');
});
test('source identity proof rejects generated defaults, ambiguity and same-handle replacement',()=>{
  const {document,context,line}=make();assert.equal(context.GetObjectBySourceHandle(line.Handle),null);
  const proof=accept(context,line);assert.equal(context.GetObjectBySourceHandle('000'+line.Handle),line);proof.Ambiguous=true;assert.equal(context.GetObjectBySourceHandle(line.Handle),null);assert.throws(()=>context.ValidateSourceIdentityDeclarations(),{name:'FormatException'});
  proof.Ambiguous=false;document.AddedObjects.set_Item(line.Handle,new api.Line());assert.equal(context.GetObjectBySourceHandle(line.Handle),null);
});
test('source-handle parser preserves exact CLR UInt64 and trailing-NUL acceptance',()=>{
  assert.equal(SourceHandle('00000000000000000022\0\0'),34n);assert.equal(SourceHandle('FFFFFFFFFFFFFFFF'),0xffffffffffffffffn);
  for(const value of ['',null,' 22','22 ','22\0G','10000000000000000'])assert.equal(SourceHandle(value),null);
});
test('metadata observer never interprets XRECORD payload as common header metadata',()=>{
  const inner=new TextCodeValueReader('0\nSECTION\n2\nOBJECTS\n0\nXRECORD\n5\nA\n100\nAcDbXrecord\n102\n{ACAD_REACTORS\n330\nB\n102\n}\n0\nENDSEC\n0\nEOF\n'),records=new Map(),identities=new Set(),reader=new io.DatabaseMetadataReader(inner,records,identities);
  for(let i=0;i<10;i++)reader.Next();assert.equal(records.size,0);assert.deepEqual([...identities],[10n]);
});
test('metadata duplicates mark the previously retained SourceRecord object ambiguous',()=>{
  const inner=new TextCodeValueReader('0\nSECTION\n2\nENTITIES\n0\nLINE\n5\nA\n100\nAcDbEntity\n0\nPRIVATE\n5\n000A\n100\nPrivate\n0\nENDSEC\n'),reader=new io.DatabaseMetadataReader(inner,new Map(),new Set());
  for(let i=0;i<5;i++)reader.Next();const first=reader.SourceRecord;assert.equal(first.Ambiguous,false);reader.Next();reader.Next();assert.equal(first.Ambiguous,true);assert.equal(reader.SourceRecord.Ambiguous,true);
});
for(const binary of [false,true])test('reader cast diagnostics expose source and destination primitive types '+binary,()=>{
  let reader;if(binary){const stream=new api.MemoryStream(),writer=new BinaryCodeValueWriter(stream);writer.Write(70,2);stream.Position=0;reader=new BinaryCodeValueReader(stream);}else reader=new TextCodeValueReader('70\n2\n');
  reader.Next();assert.throws(()=>reader.ReadString(),e=>e.name==='InvalidCastException'&&e.message==="Unable to cast object of type 'System.Int16' to type 'System.String'.");assert.equal(reader.ReadShort(),2);
});
test('database and metadata corpora are deterministic input-only coverage',()=>{
  for(const [factory,count,operations]of [[databasePayloadCorpus,1070,4701],[sourceMetadataCorpus,420,12322]]){
    const values=factory();assert.deepEqual(values,factory());assert.equal(values.length,count);assert.equal(new Set(values.map(p=>p.name)).size,count);assert.equal(values.reduce((n,p)=>n+p.request.steps.length,0),operations);assert.ok(values.every(p=>!('expected'in p)));
  }
});
