// Port of pinned RawObjectStoreTests.cs. RawObjectBoundaryTests is registered
// independently by Program; its existing identities are not duplicated here.
import { DxfDocument, DxfVersion, DxfRawDocument, DxfRawObjectStore, DxfRawHandleIndex,
  DxfRawHandleRole, DxfRawDictionary, DxfRawXRecord, DxfRawDictionaryVariable,
  DxfRawIdBuffer, DxfRawPlaceholder, DxfRawSortentsTable, DxfRawSortOrderEntry,
  DxfDuplicateRecordCloning, DxfTag, MemoryStream, Line, Circle, Vector3 } from '../../node-entry.js';
import { ArgumentException, InvalidOperationException, KeyNotFoundException, ObjectDisposedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, SameDoubleBits, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { OleSingle, OleWriteArtifact } from './Ole2FrameTests.js';
const A=Array.from;
export function RegisterRawObjectStoreTests() {
  for(const v of SupportedVersions)for(const b of [false,true]) {
    for(let c=0;c<6;c++)Run(`objects/workflow/${VersionName(v)}/${BooleanName(b)}/${c}`,()=>ObjectStoreWorkflow(v,b,c));
    for(const [name,fn]of [['extension',ObjectStoreExtension],['immutability',ObjectStoreImmutability],['deletion',ObjectStoreDeletion],['empty-sections',ObjectStoreEmptySections]])
      Run(`objects/${name}/${VersionName(v)}/${BooleanName(b)}`,()=>fn(v,b));
  }
  for(let i=0;i<18;i++)Run('objects/api/failure/'+i,()=>ObjectStoreFailure(i));
  for(const text of ['Plain','Żółć Ω','emoji 🧪',String.raw`literal \U+0041`,'{ACAD_REACTORS}',' spaced name '])Run('objects/unicode/'+text,()=>ObjectStoreUnicode(text));
  Run('objects/long-owned-chain',ObjectStoreLongChain);
}
export function ObjectStoreSource(version,binary) {
  const doc=new DxfDocument(version),line=new Line(new Vector3(1,2,3),new Vector3(4,5,6)),circle=new Circle(new Vector3(7,8,9),2.5);
  doc.Entities.Add(line);doc.Entities.Add(circle);const stream=new MemoryStream();
  try{Check(doc.Save(stream,binary),'Object source save failed');stream.Position=0;const raw=DxfRawDocument.Load(stream),index=DxfRawHandleIndex.Create(raw);
    const block=OleSingle(A(index.GetOccurrences(OleSingle(index.FindDefinitions(line.Handle)).Record)).filter(o=>o.Role===DxfRawHandleRole.Owner)).CanonicalHandle;
    return {Raw:raw,Line:line.Handle,Circle:circle.Handle,Block:block};
  }finally{stream.Dispose();}
}
function objectOf(store,handle,Type) {const result=store.Get(handle);Check(result instanceof Type,`${handle} is not ${Type.name}: ${result?.Reason}`);return result;}
function ObjectData(target) {return [
  [1,'raw application value'],[10,1.0000000000000002],[20,-0],[30,-13.25],[70,-32768],[90,2147483647],[160,-9223372036854775808n],[290,true],
  [310,Uint8Array.from({length:127},(_,i)=>i)],[330,target],[320,'DEAD'],[100,'application subclass-like text'],[101,'Embedded Object'],[102,'{application payload'],[280,93]
].map(([c,v])=>new DxfTag(c,v));}
function assertTags(expected,actual,message) {
  expected=A(expected);actual=A(actual);Equal(expected.length,actual.length,message+' count');
  for(let i=0;i<expected.length;i++) {
    Equal(expected[i].Code,actual[i].Code,message+' code');
    if(expected[i].Code>=10&&expected[i].Code<=39)SameDoubleBits(expected[i].Value,actual[i].Value,message+' double');
    else Equal(expected[i].Value,actual[i].Value,message+' value');
  }
}
function ObjectStoreWorkflow(version,binary,cloning) {
  const source=ObjectStoreSource(version,binary),store=DxfRawObjectStore.Open(source.Raw),tx=store.BeginEdit();
  try {
    const root=tx.EnsureRootDictionary(),custom=tx.CreateDictionary(root,'NETDXF_OBJECT_TESTS'),placeholder=tx.CreatePlaceholder(custom,'Placeholder'),data=ObjectData(placeholder);
    const xrecord=tx.CreateXRecord(custom,'Record',data,cloning),variable=tx.CreateVariable(custom,'Variable',String.raw`Żółć Ω 🧪 \U+0041`,0);
    const buffer=tx.CreateIdBuffer(custom,'Buffer',[source.Line,'0',source.Circle,source.Line]),defaults=tx.CreateDictionary(custom,'Fallback',true,true);
    tx.SetDictionaryFlags(custom,true,cloning);tx.SetDictionaryEntry(custom,'Record alias',xrecord,true);tx.RenameEntry(custom,'variable','VARIABLE');
    const clone=tx.CloneDictionaryTree(custom,root,'NETDXF_OBJECT_COPY');let sort=null;
    if(version>=DxfVersion.AutoCad2004)sort=tx.SetDrawOrder(source.Block,[new DxfRawSortOrderEntry(source.Line,'0'),new DxfRawSortOrderEntry(source.Circle,'FFFFFFFFFFFFFFFE')]);
    let doc=tx.Commit();Check(doc!==source.Raw,'Changed transaction returned original');Throws(ObjectDisposedException,()=>tx.CreatePlaceholder(custom,'closed'));
    for(let cycle=0;cycle<3;cycle++) {
      const current=DxfRawObjectStore.Open(doc),dictionary=objectOf(current,custom,DxfRawDictionary);
      Equal(6,dictionary.Entries.Count,'Dictionary alias count');Equal(cloning,dictionary.CloningFlag,'Cloning policy');Equal(xrecord,dictionary.Find('record alias').Handle,'Alias target');Check(dictionary.Find('RECORD ALIAS').IsHardOwner,'Per-entry hard link lost');Equal('VARIABLE',dictionary.Find('variable').Name,'Case-only name lost');
      const record=objectOf(current,xrecord,DxfRawXRecord);assertTags(data,record.Data,'XRECORD exact payload');Equal(cloning,record.CloningFlag,'XRECORD cloning');
      Equal(String.raw`Żółć Ω 🧪 \U+0041`,objectOf(current,variable,DxfRawDictionaryVariable).Value,'Portable variable text');
      Equal([source.Line,'0',source.Circle,source.Line],A(objectOf(current,buffer,DxfRawIdBuffer).Handles),'IDBUFFER order/nulls/duplicates');
      const defaultDictionary=objectOf(current,defaults,DxfRawDictionary);Check(defaultDictionary.HasDefault&&defaultDictionary.DefaultHandle===defaultDictionary.Find('Default').Handle,'Default pointer changed');Check(current.Get(defaultDictionary.DefaultHandle) instanceof DxfRawPlaceholder,'Default placeholder lost');
      const copied=objectOf(current,clone,DxfRawDictionary),copiedRecord=copied.Find('Record').Handle,copiedPlaceholder=copied.Find('Placeholder').Handle;
      Check(copiedRecord!==xrecord&&copiedPlaceholder!==placeholder,'Subtree identities reused');Equal(copiedRecord,copied.Find('Record alias').Handle,'Clone alias target changed');
      const copiedData=A(objectOf(current,copiedRecord,DxfRawXRecord).Data);Equal(copiedPlaceholder,OleSingle(copiedData.filter(t=>t.Code===330)).Value,'XRECORD reference remap');Equal('DEAD',OleSingle(copiedData.filter(t=>t.Code===320)).Value,'Arbitrary handle must not translate');
      if(sort!==null){const sorting=objectOf(current,sort,DxfRawSortentsTable);Equal(source.Block,sorting.BlockRecordHandle,'Draw order block');Equal('0',sorting.Entries[0].SortHandle,'Null sort key');Equal('FFFFFFFFFFFFFFFE',sorting.Entries[1].SortHandle,'Wide sort key');}
      const output=new MemoryStream();try{doc.Save(output,binary);if(cycle===1&&cloning===DxfDuplicateRecordCloning.KeepExisting)OleWriteArtifact(`object-store-${VersionName(version)}-${BooleanName(binary)}.dxf`,output.ToArray());output.Position=0;doc=DxfRawDocument.Load(output);}finally{output.Dispose();}
    }
    Check(store.RootDictionary.Find('NETDXF_OBJECT_TESTS')===null&&source.Raw.HasOriginalBytes,'Transaction mutated original snapshot');
    const edits=DxfRawObjectStore.Open(doc).BeginEdit();try{
      Throws(InvalidOperationException,()=>edits.RemoveEntry(custom,'Record alias',true));Check(edits.Get(custom).Find('Record alias')!==null,'Failed delete did not roll back unlink');Check(edits.RemoveEntry(custom,'Record alias'),'Explicit unlink failed');
      edits.SetXRecord(xrecord,[new DxfTag(1,'edited')],DxfDuplicateRecordCloning.UseClone);edits.SetVariable(variable,null,null);edits.SetIdBuffer(buffer,[]);
      const edited=DxfRawObjectStore.Open(edits.Commit());Equal('edited',OleSingle(objectOf(edited,xrecord,DxfRawXRecord).Data).Value,'Payload editing');Equal(15,objectOf(edited,objectOf(edited,clone,DxfRawDictionary).Find('Record').Handle,DxfRawXRecord).Data.Count,'Clone edit isolation');
      const v=objectOf(edited,variable,DxfRawDictionaryVariable);Check(v.Value===null&&v.SchemaNumber===null,'Optional variable fields invented');
    }finally{edits.Dispose();}
  }finally{tx.Dispose();}
}
function ObjectStoreExtension(version,binary) {
  const source=ObjectStoreSource(version,binary),tx=DxfRawObjectStore.Open(source.Raw).BeginEdit();
  try{
    const ext=tx.EnsureExtensionDictionary(source.Line);Equal(ext,tx.EnsureExtensionDictionary(source.Line),'Ensure extension not idempotent');
    const data=tx.CreateXRecord(ext,'USER_DATA',ObjectData(source.Circle)),doc=tx.Commit(),index=DxfRawHandleIndex.Create(doc),line=OleSingle(index.FindDefinitions(source.Line)).Record;
    Equal(ext,OleSingle(A(index.GetOccurrences(line)).filter(t=>t.Role===DxfRawHandleRole.ExtensionDictionary)).CanonicalHandle,'Common extension pointer');
    const store=DxfRawObjectStore.Open(doc);Equal(source.Line,objectOf(store,ext,DxfRawDictionary).OwnerHandle,'Extension owner');
    const remove=store.BeginEdit();try{Check(remove.RemoveExtensionDictionary(source.Line,true),'Extension removal failed');const result=DxfRawObjectStore.Open(remove.Commit());Check(result.Get(ext)===null&&result.Get(data)===null,'Owned extension tree not deleted');
      const lineRecord=d=>OleSingle(A(d.Sections).flatMap(s=>A(s.Records)).filter(r=>r.Name==='LINE'));assertTags(lineRecord(source.Raw).Tags,lineRecord(result.Document).Tags,'Detach restored unchanged line fields');
    }finally{remove.Dispose();}
  }finally{tx.Dispose();}
}
function ObjectStoreImmutability(version,binary) {
  const source=ObjectStoreSource(version,binary),store=DxfRawObjectStore.Open(source.Raw),empty=store.BeginEdit();try{Check(empty.Commit()===source.Raw,'No-op lost original bytes');}finally{empty.Dispose();}
  const create=store.BeginEdit();try{
    const root=create.EnsureRootDictionary(),dictionary=create.CreateDictionary(root,'TEST'),data=create.CreateXRecord(dictionary,'data',ObjectData(source.Line)),first=create.Commit(),firstStore=DxfRawObjectStore.Open(first),noOp=firstStore.BeginEdit();
    try{const d=objectOf(firstStore,dictionary,DxfRawDictionary),x=objectOf(firstStore,data,DxfRawXRecord);noOp.SetDictionaryFlags(dictionary,d.HardOwnerFlag,d.CloningFlag);noOp.SetDictionaryEntry(dictionary,'data',data);noOp.RenameEntry(dictionary,'data','data');noOp.SetXRecord(data,x.Data,x.CloningFlag);Check(noOp.Commit()===first,'Semantic no-op re-encoded records');}finally{noOp.Dispose();}
    const later=firstStore.BeginEdit();try{later.SetXRecord(data,[new DxfTag(1,'later')]);const second=later.Commit(),line=d=>A(OleSingle(A(d.Sections).flatMap(s=>A(s.Records)).filter(r=>r.Name==='LINE')).Tags),before=line(first),after=line(second);Check(before.length===after.length&&before.every((t,i)=>t===after[i]),'Unrelated raw tags replaced');}finally{later.Dispose();}
    const payload=()=>OleSingle(A(objectOf(firstStore,data,DxfRawXRecord).Data).filter(t=>t.Code===310)).Value,bytes=payload();bytes[0]=201;Equal(0,payload()[0],'Payload getter aliases bytes');
    const rollback=firstStore.BeginEdit();rollback.SetXRecord(data,[new DxfTag(1,'discard')]);rollback.Dispose();assertTags(ObjectData(source.Line),objectOf(firstStore,data,DxfRawXRecord).Data,'Disposed edit changed source');
  }finally{create.Dispose();}
}
function ObjectStoreDeletion(version,binary) {
  const source=ObjectStoreSource(version,binary),create=DxfRawObjectStore.Open(source.Raw).BeginEdit();
  try{const root=create.EnsureRootDictionary(),tree=create.CreateDictionary(root,'TREE'),nested=create.CreateDictionary(tree,'NESTED',true,true),leaf=create.CreateXRecord(nested,'LEAF',[]),external=create.CreateIdBuffer(root,'EXTERNAL',[leaf]),first=create.Commit(),tx=DxfRawObjectStore.Open(first).BeginEdit();
    try{Throws(InvalidOperationException,()=>tx.RemoveEntry(root,'TREE',true));Check(tx.Get(root).Find('TREE')!==null,'Rejected subtree deletion modified parent');tx.SetIdBuffer(external,[]);Check(tx.RemoveEntry(root,'TREE',true),'Safe subtree deletion failed');const result=DxfRawObjectStore.Open(tx.Commit());Check(result.Get(tree)===null&&result.Get(nested)===null&&result.Get(leaf)===null,'Subtree removal incomplete');Check(result.Get(external) instanceof DxfRawIdBuffer,'Unrelated object deleted');}finally{tx.Dispose();}
  }finally{create.Dispose();}
}
function ObjectStoreEmptySections(version,binary) {
  const source=ObjectStoreSource(version,binary),remove=new Set();for(const s of source.Raw.Sections)if(['OBJECTS','CLASSES'].includes(s.Name))for(let i=s.StartTagIndex;i<s.EndTagIndex;i++)remove.add(i);
  const raw=source.Raw.WithTags(A(source.Raw.Tags).filter((t,i)=>!remove.has(i))),tx=DxfRawObjectStore.Open(raw).BeginEdit();
  try{const root=tx.EnsureRootDictionary();tx.CreateDictionary(root,'Defaults',true,true);const result=tx.Commit(),sections=A(result.Sections);Equal(1,sections.filter(s=>s.Name==='OBJECTS').length,'OBJECTS section insertion');Equal(1,sections.filter(s=>s.Name==='CLASSES').length,'CLASSES section insertion');Check(OleSingle(sections.filter(s=>s.Name==='CLASSES')).StartTagIndex<OleSingle(sections.filter(s=>s.Name==='TABLES')).StartTagIndex,'CLASSES after tables');Equal(root,DxfRawObjectStore.Open(result).RootDictionary.Handle,'New root must be first OBJECTS record');}finally{tx.Dispose();}
}
function ObjectStoreFailure(scenario) {
  const source=ObjectStoreSource(DxfVersion.AutoCad2018,false),tx=DxfRawObjectStore.Open(source.Raw).BeginEdit();
  try{const root=tx.EnsureRootDictionary(),dictionary=tx.CreateDictionary(root,'CASE'),leaf=tx.CreatePlaceholder(dictionary,'Leaf');
    const actions=[()=>tx.CreatePlaceholder(dictionary,'leaf'),()=>tx.CreatePlaceholder(dictionary,''),()=>tx.CreatePlaceholder(dictionary,'bad\nname'),()=>tx.CreateVariable(dictionary,'New','\0'),()=>tx.CreateXRecord(dictionary,'New',[new DxfTag(5,'CAFE')]),()=>tx.CreateXRecord(dictionary,'New',[new DxfTag(105,'CAFE')]),()=>tx.CreateXRecord(dictionary,'New',[new DxfTag(310,new Uint8Array(128))]),()=>tx.CreateXRecord(dictionary,'New',[],6),()=>tx.SetDefault(dictionary,leaf),()=>tx.SetDictionaryEntry(dictionary,'Missing','FFFF'),()=>tx.SetDictionaryEntry(dictionary,'Graphic',source.Line),()=>tx.RenameEntry(dictionary,'Missing','New'),()=>tx.CreateVariable(dictionary,'New','\ud800'),()=>tx.CreateIdBuffer(dictionary,'New',['not a handle']),()=>tx.SetDrawOrder(source.Line,[]),()=>tx.SetDrawOrder(source.Block,[new DxfRawSortOrderEntry(source.Line,'1'),new DxfRawSortOrderEntry(source.Line,'2')]),()=>tx.DeleteOwnedTree(root),()=>tx.SetDictionaryEntry(root,'Steal',leaf,true)];
    let failed=false;try{actions[scenario]();}catch(e){if(!(e instanceof ArgumentException||e instanceof InvalidOperationException||e instanceof KeyNotFoundException))throw e;failed=true;}
    Check(failed,'Invalid operation unexpectedly accepted');const current=tx.Get(dictionary);Equal(1,current.Entries.Count,'Failed operation changed entry count');Equal(leaf,current.Find('Leaf').Handle,'Failed operation changed target');Check(tx.Get(leaf) instanceof DxfRawPlaceholder,'Failed operation deleted leaf');tx.CreateVariable(dictionary,'New','valid');Check(DxfRawObjectStore.Open(tx.Commit()).Get(leaf)!==null,'Transaction unusable after rejection');
  }finally{tx.Dispose();}
}
function ObjectStoreUnicode(text) {
  Equal(text,DxfRawObjectStore.DecodeText(DxfRawObjectStore.EncodeText(text)),'One-pass Unicode escape round trip');const source=ObjectStoreSource(DxfVersion.AutoCad2000,false),tx=DxfRawObjectStore.Open(source.Raw).BeginEdit(),output=new MemoryStream();
  try{const root=tx.EnsureRootDictionary(),value=tx.CreateVariable(root,text,text);tx.Commit().Save(output);output.Position=0;const store=DxfRawObjectStore.Open(DxfRawDocument.Load(output));Equal(value,store.RootDictionary.Find(text).Handle,'Unicode dictionary lookup');Equal(text,objectOf(store,value,DxfRawDictionaryVariable).Value,'Unicode variable decoding');}finally{output.Dispose();tx.Dispose();}
}
function ObjectStoreLongChain() {
  const source=ObjectStoreSource(DxfVersion.AutoCad2018,false),tx=DxfRawObjectStore.Open(source.Raw).BeginEdit();
  try{const root=tx.EnsureRootDictionary(),first=tx.CreateDictionary(root,'Long');let last=first;for(let i=0;i<1200;i++)last=tx.CreateDictionary(last,'next');const copy=tx.CloneDictionaryTree(first,root,'Long copy'),result=DxfRawObjectStore.Open(tx.Commit());Check(result.Get(copy) instanceof DxfRawDictionary&&result.Get(last) instanceof DxfRawDictionary,'Iterative subtree clone failed');const erase=result.BeginEdit();try{Check(erase.RemoveEntry(root,'Long',true),'Iterative subtree deletion failed');const final=DxfRawObjectStore.Open(erase.Commit());Check(final.Get(last)===null&&final.Get(copy)!==null,'Subtree deletion affected clone');}finally{erase.Dispose();}}finally{tx.Dispose();}
}
