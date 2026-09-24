import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import * as io from '../../runtime/DatabasePayloadIO.js';
import { PeekDocumentObjects } from '../../netDxf/DxfDocument.Objects.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { objectGraphIOCorpus } from '../../tools/object-graph-io-corpus.mjs';
import { ValidateObjectGraphObservation } from '../../tools/object-graph-io-observation.mjs';
const root=entries=>[[0,'DICTIONARY'],[5,'C0'],[330,'0'],[100,'AcDbDictionary'],[281,1],...entries.flatMap(([name,handle,hard=true])=>[[3,name],[hard?360:350,handle]])];
const record=(code,tags=[],handle='C1',owner='C0',header=[])=>[[0,code],[5,handle],[330,owner],...header,...tags];
function writer(mode='text'){
  const stream=new api.MemoryStream(),host={text:'',WriteLine(value){this.text+=String(value??'')+'\n';},Flush(){}},chunk=mode==='text'?new TextCodeValueWriter(host):new BinaryCodeValueWriter(stream,mode==='legacy');
  return {chunk,finish(){chunk.Flush();if(mode==='text')return host.text;stream.Position=0;return stream;}};
}
function input(tags,mode='text',doc=new api.DxfDocument(18)){
  const output=writer(mode);for(const [c,v]of [[0,'SECTION'],[2,'OBJECTS'],...tags,[0,'ENDSEC'],[0,'EOF']])output.chunk.Write(c,v);
  return load(output.finish(),mode,doc);
}
function load(data,mode='text',doc=new api.DxfDocument(18)){
  const context=new io.DatabaseIOContext(doc),inner=mode==='text'?new TextCodeValueReader(data):new BinaryCodeValueReader(data,undefined,mode==='legacy');
  context.Chunk=new io.DatabaseMetadataReader(inner,context.entityDatabaseMetadata,context.sourceObjectIdentities);
  for(let i=0;i<3;i++)context.Chunk.Next();return context;
}
function readAll(context){
  while(context.Chunk.Value!=='ENDSEC'){
    if(context.Chunk.Value==='DICTIONARY'){
      const value=io.ReadDictionaryDatabaseRecord(context);context.dictionaries.Add(value.Handle,value);if(context.namedDictionary===null&&context.databaseRecords.get_Item(context.databaseRecords.Count-1).Metadata.Owner==='0')context.namedDictionary=value;
    }else io.ReadDatabaseRecord(context);
  }
  return context;
}
function graph(tags,mode='text',doc){const context=readAll(input(tags,mode,doc));io.ImportDatabaseObjects(context);return context;}
const capture=()=>({tags:[],Write(code,value){this.tags.push([code,value]);}});
const simple=()=>graph([...root([['Data','C1']]),...record('XRECORD',[[100,'AcDbXrecord'],[280,1],[1,'stored']])]);
const version=()=>new api.DxfDocument(18);

test('OBJECTS and legacy projections retain standalone/export identities',async()=>{
  for(const [file,names]of [['IO/DxfReader.Objects',['ReadDatabaseRecord','ImportDatabaseObjects']],['IO/DxfWriter.Objects',['WriteDatabaseObject','PrepareDatabaseClasses']],['Objects/DictionaryObject',['DictionaryObject']],['Objects/XRecord',['XRecord']],['Objects/XRecordEntry',['XRecordEntry']]]){
    const module=await import(`../../netDxf/${file}.js`);for(const name of names)assert.equal(module[name],name in api?api[name]:io[name]);
  }
});
test('legacy record entries retain unvalidated code and boxed object identity',()=>{
  const value={},entry=new api.XRecordEntry(-1,value),legacy=new api.XRecord();legacy.Entries.Add(entry);assert.equal(entry.Value,value);assert.equal(entry.Code,-1);assert.equal(legacy.Codename,'XRECORD');assert.equal(legacy.Handle,'');assert.equal(legacy.Flags,1);assert.throws(()=>{entry.Value=null;},TypeError);
});
test('legacy dictionary entries are mutable case-sensitive handle keys',()=>{
  const projection=new api.DictionaryObject(null);projection.Entries.Add('a','first');projection.Entries.Add('A','second');assert.equal(projection.IsHardOwner,true);assert.equal(projection.Cloning,1);assert.equal(projection.Entries.Count,2);
});
for(const mode of ['text','binary','legacy'])test('OBJECTS graph '+mode+' stream roundtrip binds aliases, extension trees, XData and repeated references',()=>{
  const tags=[...root([['Data','C1'],['Alias','C1',false],['Buffer','C4']]),
    ...record('XRECORD',[[100,'AcDbXrecord'],[280,1],[1,'literal\\U+005CU+0041'],[10,-0],[310,Uint8Array.of(0,255)],[1001,'GRAPH_APP'],[1000,'Unicode Ω']],'C1','C0',[[102,'{ACAD_XDICTIONARY'],[360,'C2'],[102,'}']]),
    ...record('DICTIONARY',[[100,'AcDbDictionary'],[3,'Leaf'],[360,'C3']],'C2','C1'),...record('ACDBPLACEHOLDER',[],'C3','C2'),
    ...record('IDBUFFER',[[100,'AcDbIdBuffer'],[330,'C1'],[330,'C1'],[330,'0']],'C4')];
  const context=graph(tags,mode),doc=context.Document,data=doc.NamedObjects.get_Item('Data');
  assert.equal(doc.NamedObjects.get_Item('Alias'),data);assert.equal(data.ExtensionDictionary.get_Item('Leaf').Owner,data.ExtensionDictionary);assert.equal(data.Data.get_Item(0).Value,'literal\\U+0041');assert.equal(Object.is(data.Data.get_Item(1).Value,-0),true);
  assert.deepEqual([...doc.NamedObjects.get_Item('Buffer').References],[data,data,null]);assert.equal(doc.Objects.Validate().Count,0);assert.equal(context.Chunk.Value,'ENDSEC');
  const out=writer(mode);out.chunk.Write(0,'SECTION');out.chunk.Write(2,'OBJECTS');for(const item of doc.Objects.Items)io.WriteDatabaseObject(out.chunk,doc,item);out.chunk.Write(0,'ENDSEC');out.chunk.Write(0,'EOF');
  const copyContext=readAll(load(out.finish(),mode));io.ImportDatabaseObjects(copyContext);const copy=copyContext.Document,cloned=copy.NamedObjects.get_Item('Data');
  assert.equal(copy.GetObjectByHandle('C1'),cloned);assert.notEqual(cloned,data);assert.equal(copy.NamedObjects.get_Item('Alias'),cloned);assert.equal(cloned.Data.get_Item(0).Value,'literal\\U+0041');assert.equal(cloned.XData.get_Item('GRAPH_APP').XDataRecord.get_Item(0).Value,'Unicode Ω');assert.equal(copy.Objects.Validate().Count,0);
});
test('import registers child-first records before attaching links',()=>{
  const context=graph([...record('ACDBPLACEHOLDER'),...root([['Child','C1']])]);assert.equal(context.Document.NamedObjects.get_Item('Child').Owner,context.Document.NamedObjects);
});
test('duplicate physical identities fail before lazy root allocation',()=>{
  const context=readAll(input([...root([['Child','C1']]),...record('ACDBPLACEHOLDER'),...record('ACDBPLACEHOLDER',[],'00c1')]));const seed=context.Document.NumHandles;
  assert.throws(()=>io.ImportDatabaseObjects(context),{name:'FormatException'});assert.equal(PeekDocumentObjects(context.Document),null);assert.equal(context.Document.NumHandles,seed);
});
test('pending records preserve captured physical identities when the reader advances',()=>{
  const context=readAll(input([...root([['Child','C1']]),...record('ACDBPLACEHOLDER')])),records=[...context.databaseRecords];
  assert.equal(records[0].SourceIdentity.Handle,0xc0n);assert.equal(records[1].SourceIdentity.Handle,0xc1n);assert.notEqual(records[0].SourceIdentity,records[1].SourceIdentity);assert.notEqual(records[1].SourceIdentity,context.CurrentSourceRecord);
});
test('source pointer cannot silently resolve a generated default',()=>{
  const doc=version(),handle=doc.TextStyles.get_Item('Standard').Handle,context=readAll(input([...root([['Buffer','C1']]),...record('IDBUFFER',[[100,'AcDbIdBuffer'],[330,handle]])],'text',doc));
  assert.throws(()=>io.ImportDatabaseObjects(context),{name:'FormatException'});assert.equal(context.GetObjectBySourceHandle(handle),null);assert.equal(doc.NamedObjects.Handle,'C0');
});
test('unresolved dictionary entries keep completed registration and owner state',()=>{
  const context=readAll(input([...root([['Missing','FFF']]),...record('ACDBPLACEHOLDER')]));assert.throws(()=>io.ImportDatabaseObjects(context),{name:'FormatException'});
  assert.equal(context.Document.GetObjectByHandle('C1').Owner,context.Document.NamedObjects);assert.equal(context.Document.NamedObjects.Count,0);
});
for(const name of ['constructor','__proto__','toString'])test('dispatch treats '+name+' as an opaque DXF code, not an inherited property',()=>{
  const context=graph([...root([['Child','C1']]),...record(name,[[100,'Vendor'],[1,'literal\\U+0041']])]),item=context.Document.NamedObjects.get_Item('Child');assert.ok(item instanceof api.DxfOpaqueObject);assert.equal(item.CodeName,name);assert.equal(item.Tags.get_Item(1).Value,'literal\\U+0041');
});
test('dictionary legacy projection keeps first alias per handle while typed import retains all',()=>{
  const context=input([...root([['First','C1'],['Second','C1',false]]),...record('ACDBPLACEHOLDER')]),projection=io.ReadDictionaryDatabaseRecord(context);assert.equal(projection.Entries.Count,1);assert.equal(projection.Entries.get_Item('C1'),'First');assert.equal(context.databaseRecords.get_Item(0).Entries.length,2);
});
test('XRECORD payload control tags are not reinterpreted as common ownership metadata',()=>{
  const context=graph([...root([['Data','C1']]),...record('XRECORD',[[100,'AcDbXrecord'],[102,'{ACAD_REACTORS'],[330,'C0'],[102,'}']])]),item=context.Document.NamedObjects.get_Item('Data');assert.equal(item.PersistentReactors.Count,0);assert.equal(item.Data.get_Item(0).Value,'{ACAD_REACTORS');assert.equal(item.Owner,context.Document.NamedObjects);
});
test('legacy XRECORD projection preserves payload values and owner',()=>{
  const context=input(record('XRECORD',[[100,'AcDbXrecord'],[280,1],[10,-0],[1,'text']],'C1','0')),projection=io.ReadXRecordDatabaseRecord(context);assert.ok(projection instanceof api.XRecord);assert.equal(projection.OwnerHandle,'0');assert.equal(Object.is(projection.Entries.get_Item(0).Value,-0),true);assert.equal(projection.Entries.get_Item(1).Value,'text');
});
test('XData registration survives a subsequent invalid object XData tag',()=>{
  const context=input(record('XRECORD',[[100,'AcDbXrecord'],[1001,'SIDE_EFFECT'],[1000,'value'],[90,7]]));assert.throws(()=>io.ReadDatabaseRecord(context),{name:'FormatException'});assert.equal(context.Document.ApplicationRegistries.Contains('SIDE_EFFECT'),true);assert.equal(context.databaseRecords.Count,0);
});
test('persistent reactors bind to accepted source objects with duplicate suppression',()=>{
  const context=graph([...root([['Child','C1']]),...record('ACDBPLACEHOLDER',[],'C1','C0',[[102,'{ACAD_REACTORS'],[330,'C0'],[330,'00c0'],[102,'}']])]),item=context.Document.GetObjectByHandle('C1');assert.deepEqual([...item.PersistentReactors],[context.Document.NamedObjects]);
});
test('managed reactor exemptions remain case-insensitive but do not create fake objects',()=>{
  const context=readAll(input([...root([['Child','C1']]),...record('ACDBPLACEHOLDER',[],'C1','C0',[[102,'{ACAD_REACTORS'],[330,'AB'],[102,'}']])]));context.managedReactorHandles.add('ab');io.ImportDatabaseObjects(context);assert.equal(context.Document.GetObjectByHandle('C1').PersistentReactors.Count,0);
});
test('extension ownership mismatch rejects the relationship after source registration',()=>{
  const context=readAll(input([...root([['Child','C1'],['Wrong','C2']]),...record('ACDBPLACEHOLDER',[],'C1','C0',[[102,'{ACAD_XDICTIONARY'],[360,'C2'],[102,'}']]),...record('DICTIONARY',[[100,'AcDbDictionary']],'C2','C0')]));assert.throws(()=>io.ImportDatabaseObjects(context),{name:'FormatException'});assert.equal(context.Document.GetObjectByHandle('C1').ExtensionDictionary,context.Document.GetObjectByHandle('C2'));
});
test('metadata output uses the canonical registered object rather than a stale same-handle shell',()=>{
  const context=simple(),doc=context.Document,canonical=doc.GetObjectByHandle('C1'),old=new api.DxfPlaceholder();old.Handle=canonical.Handle;const ext=new api.DxfDictionary();doc.Objects.SetExtensionDictionary(canonical,ext);const out=capture();io.WriteDatabaseMetadata(out,doc,old);assert.deepEqual(out.tags,[[102,'{ACAD_XDICTIONARY'],[360,ext.Handle],[102,'}']]);
});
test('metadata output preserves automatic order and distinguishes padded lexical handles',()=>{
  const context=simple(),out=capture();io.WriteDatabaseMetadata(out,context.Document,context.Document.NamedObjects,['C1','c1','00C1','C0']);assert.deepEqual(out.tags,[[102,'{ACAD_REACTORS'],[330,'C1'],[330,'00C1'],[330,'C0'],[102,'}']]);
});
test('null persistent reactor fails after a completed extension envelope without emitting a reactor block',()=>{
  const context=simple(),doc=context.Document,item=doc.GetObjectByHandle('C1'),ext=new api.DxfDictionary();doc.Objects.SetExtensionDictionary(item,ext);item.PersistentReactors.Add(null);const out=capture();assert.throws(()=>io.WriteDatabaseMetadata(out,doc,item),{name:'InvalidOperationException'});assert.equal(out.tags.length,3);assert.equal(out.tags[0][1],'{ACAD_XDICTIONARY');
});
test('outer object writer completes identity before a callback rejects the owner group',()=>{
  const context=simple(),out=[],failure=new Error('caller');assert.throws(()=>io.WriteDatabaseObject({Write(c,v){if(c===330)throw failure;out.push([c,v]);}},context.Document,context.Document.GetObjectByHandle('C1')),error=>error===failure);assert.deepEqual(out,[[0,'XRECORD'],[5,'C1']]);
});
test('outer writer encoding observes version changes between output callbacks',()=>{
  const context=graph([...root([['Ω','C1']]),...record('ACDBPLACEHOLDER')]),doc=context.Document,out=[];io.WriteDatabaseObject({Write(c,v){out.push([c,v]);if(c===281)doc.DrawingVariables.AcadVer=13;}},doc,doc.NamedObjects);assert.equal(out.find(([c])=>c===3)[1],'\\U+03A9');
});
test('generated legacy root names precede typed names and use their distinct escaping rules',()=>{
  const context=graph([...root([['literal\\U+005CU+0041','C1']]),...record('ACDBPLACEHOLDER')]),generated=new api.DictionaryObject(null);generated.Entries.Add('AB','literal\\U+0041');const out=capture();io.WriteDatabaseObject(out,context.Document,context.Document.NamedObjects,generated);assert.deepEqual(out.tags.filter(([c])=>c===3).map(([,v])=>v),['literal\\U+0041','literal\\U+005CU+0041']);
});
test('database text preflight rejects decoded line breaks but binary output remains allowed',()=>{
  const context=graph([...root([['Data','C1']]),...record('XRECORD',[[100,'AcDbXrecord'],[1,'a\\U+000Ab']])]);assert.throws(()=>io.ValidateDatabaseTransport(context.Document,false),{name:'InvalidDataException'});io.ValidateDatabaseTransport(context.Document,true);
});
test('FIELD source guard runs even when binary transport avoids text scanning',()=>{
  const context=graph([...root([['Field','C1']]),...record('FIELD',[[100,'AcDbField'],[1,'AcVar'],[2,'stored'],[90,0],[97,0]])]);context.Document.DrawingVariables.AcadVer=17;assert.throws(()=>io.ValidateDatabaseTransport(context.Document,true),{name:'NotSupportedException'});
});
test('common class preparation updates count in place and preserves unrelated metadata',()=>{
  const context=graph([...root([['Child','C1']]),...record('ACDBPLACEHOLDER')]),doc=context.Document,definition=new api.DxfClass('ACDBPLACEHOLDER','AcDbPlaceHolder','Custom');definition.IsEntity=false;definition.ProxyFlags=27;doc.Classes.Add(definition);io.PrepareDatabaseClasses(doc,doc.Classes);assert.equal(doc.Classes.get_Item('ACDBPLACEHOLDER'),definition);assert.equal(definition.InstanceCount,1);assert.equal(definition.ApplicationName,'Custom');assert.equal(definition.ProxyFlags,27);
});
test('standard database class conflicts are rejected even with no matching instance',()=>{
  const doc=version(),definition=new api.DxfClass('IDBUFFER','Vendor','Custom');doc.Classes.Add(definition);assert.throws(()=>io.PrepareDatabaseClasses(doc,doc.Classes),{name:'InvalidDataException'});assert.equal(doc.Classes.get_Item('IDBUFFER'),definition);
});
test('private MLEADERSTYLE grammar falls back without leaking pending style references',()=>{
  const context=graph([...root([['Style','C1']]),...record('MLEADERSTYLE',[[100,'AcDbMLeaderStyle'],[179,2],[340,'C0'],[301,'private']])]);assert.ok(context.Document.NamedObjects.get_Item('Style') instanceof api.DxfOpaqueObject);assert.equal(context.mleaderReferences.length,0);
});
test('known MLEADERSTYLE packets use real typed storage and defer style dependencies',()=>{
  const context=readAll(input([...root([['Style','C1']]),...record('MLEADERSTYLE',[[100,'AcDbMLeaderStyle'],[179,2],[340,'AB']])]));assert.ok(context.databaseRecords.get_Item(1).Object instanceof api.DxfMLeaderStyle);assert.equal(context.mleaderReferences.length,1);
});
test('object-graph corpus is complete deterministic and contains no expected observations',()=>{
  const corpus=objectGraphIOCorpus();assert.deepEqual(corpus,objectGraphIOCorpus());assert.equal(corpus.length,937);assert.equal(new Set(corpus.map(p=>p.name)).size,937);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),8715);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
});
for(const bad of [null,[],[{}],[{ok:false}],[{ok:true,value:{}}]])test('object observation rejects malformed transport '+JSON.stringify(bad),()=>assert.throws(()=>ValidateObjectGraphObservation(bad,1)));

test('reader state uses case-insensitive handle maps while legacy entry dictionaries remain case-sensitive',()=>{
  const context=new io.DatabaseIOContext(version()),projection=new api.DictionaryObject(null);context.dictionaries.Add('aB',projection);assert.equal(context.dictionaries.get_Item('AB'),projection);
  const before={Owner:'old'},after={Owner:'new'};context.entityDatabaseMetadata.set('aB',before);context.entityDatabaseMetadata.set('AB',after);assert.deepEqual([...context.entityDatabaseMetadata],[['aB',after]]);
});
