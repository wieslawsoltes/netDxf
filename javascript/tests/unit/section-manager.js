import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { sectionManagerCorpus } from '../../tools/section-manager-corpus.mjs';
const setup=()=>{const doc=new api.DxfDocument(18),first=new api.Section(),second=new api.Section();doc.Entities.Add(first);doc.Entities.Add(second);void doc.Objects;return {doc,first,second};};
const state=doc=>({seed:doc.NumHandles,objects:[...doc.Objects.Items],entries:[...doc.NamedObjects.Entries]});
function enumerable(values,callbacks={}) {return {GetEnumerator(){callbacks.get?.();let at=-1;return {MoveNext(){callbacks.move?.();return ++at<values.length;},get Current(){callbacks.current?.();return values[at];},Dispose(){callbacks.dispose?.();}};}};}
test('section manager exports the original-path identity and read-only properties',async()=>{
  const standalone=await import('../../netDxf/Objects/DxfStoredSectionManager.js'),{doc,first}=setup();
  const manager=doc.Objects.CreateSectionManager([first],false);assert.equal(standalone.DxfStoredSectionManager,api.DxfStoredSectionManager);
  for(const name of ['SourceVersion','RequiresFullUpdate','Tags','Sections'])assert.throws(()=>{manager[name]=null;},TypeError);
});
test('creation preserves actual repeated identities, root flags and a single handle allocation',()=>{
  const {doc,first,second}=setup(),root=doc.NamedObjects;root.IsHardOwner=false;root.Cloning=3;
  const seed=doc.NumHandles,values=[first,second,first],manager=doc.Objects.CreateSectionManager(values,true);values.length=0;
  assert.deepEqual([...manager.Sections],[first,second,first]);assert.equal(doc.NumHandles,seed+1n);assert.equal(manager.Owner,root);
  assert.deepEqual([...manager.PersistentReactors],[root]);assert.equal(root.IsHardOwner,false);assert.equal(root.Cloning,3);
  const entry=[...root.Entries].find(e=>e.Target===manager);assert.equal(entry.Name,'ACAD_SECTION_MANAGER');assert.equal(entry.IsHardOwner,false);
  assert.equal(doc.Objects.Validate().Count,0);
});
test('packet replacement retains immutable historical containers and live section references',()=>{
  const {doc,first,second}=setup(),manager=doc.Objects.CreateSectionManager([first,second,first],true),old=manager.Sections,tags=manager.Tags,seed=doc.NumHandles;
  manager.ReplaceSections([second],false);assert.deepEqual([...old],[first,second,first]);assert.equal(tags.Count,6);assert.equal(manager.Tags.Count,4);
  assert.equal(tags.get_Item(1).Value,1);assert.equal(manager.Tags.get_Item(1).Value,0);assert.equal(doc.NumHandles,seed);
  first.Name='changed';assert.equal(old.get_Item(0).Name,'changed');assert.equal(typeof old.Add,'undefined');
});
test('members block ordinary section removal until explicit replacement releases them',()=>{
  const {doc,first,second}=setup(),manager=doc.Objects.CreateSectionManager([first,first],true);
  assert.equal(doc.Entities.Remove(first),false);manager.ReplaceSections([second],false);assert.equal(doc.Entities.Remove(first),true);
  assert.equal(doc.Entities.Remove(second),false);doc.Objects.EraseSectionManager(manager);assert.equal(doc.Entities.Remove(second),true);
});
test('create and replace honor the complete 65536-entry bound without collapsing repetitions',()=>{
  const {doc,first}=setup(),manager=doc.Objects.CreateSectionManager(Array(65536).fill(first),false);
  assert.equal(manager.Sections.Count,65536);assert.equal(manager.Tags.Count,65539);const before=manager.Sections;
  assert.throws(()=>manager.ReplaceSections(Array(65537).fill(first),true),{name:'ArgumentException',ParamName:'sections'});assert.equal(manager.Sections.Count,before.Count);assert.equal(manager.RequiresFullUpdate,false);
});
for(const stage of ['get','move','current','dispose'])test('caller '+stage+' failure finishes cleanup and leaves no partially created manager',()=>{
  const {doc,first}=setup(),before=state(doc),error=new Error(stage);let disposed=0;
  const callbacks={dispose(){disposed++;},[stage](){if(stage==='dispose')disposed++;throw error;}};
  assert.throws(()=>doc.Objects.CreateSectionManager(enumerable([first],callbacks),true),e=>e===error);
  assert.equal(disposed,stage==='get'?0:1);assert.deepEqual(state(doc),before);assert.ok(doc.Objects.CreateSectionManager([first],false));
});
for(const stage of ['get','dispose'])test('caught recursive creation during '+stage+' invalidates the outer request',()=>{
  const {doc,first}=setup(),before=state(doc);let caught=0;
  const values=enumerable([first],{[stage](){try{doc.Objects.CreateSectionManager(null,true);}catch(error){assert.equal(error.name,'InvalidOperationException');caught++;}}});
  assert.throws(()=>doc.Objects.CreateSectionManager(values,false),{name:'InvalidOperationException'});assert.equal(caught,1);assert.deepEqual(state(doc),before);
  assert.ok(doc.Objects.CreateSectionManager([first],false));
});
test('caught recursive replacement keeps the outer replacement valid, unlike creation',()=>{
  const {doc,first,second}=setup(),manager=doc.Objects.CreateSectionManager([first],false);let caught=0;
  manager.ReplaceSections(enumerable([second],{dispose(){try{manager.ReplaceSections([first],true);}catch(error){assert.equal(error.name,'InvalidOperationException');caught++;}}}),true);
  assert.equal(caught,1);assert.deepEqual([...manager.Sections],[second]);
});
test('disposal mutation is validated after caller work without rolling it back',()=>{
  const {doc,first}=setup(),before=state(doc);
  assert.throws(()=>doc.Objects.CreateSectionManager(enumerable([first],{dispose(){doc.DrawingVariables.AcadVer=14;}}),false),{name:'NotSupportedException'});
  assert.equal(doc.DrawingVariables.AcadVer,14);assert.deepEqual(state(doc),before);doc.DrawingVariables.AcadVer=18;assert.ok(doc.Objects.CreateSectionManager([first],false));
});
test('unrelated invalid entity metadata does not block creation; required root metadata does',()=>{
  const {doc,first}=setup(),line=new api.Line();doc.Entities.Add(line);line.PersistentReactors.Add(new api.DxfXRecord());
  const manager=doc.Objects.CreateSectionManager([first],false);assert.equal(manager.Sections.get_Item(0),first);line.PersistentReactors.Clear();doc.Objects.EraseSectionManager(manager);
  doc.NamedObjects.PersistentReactors.Add(new api.DxfXRecord());const before=state(doc);
  assert.throws(()=>doc.Objects.CreateSectionManager([first],false),{name:'InvalidOperationException'});assert.deepEqual(state(doc),before);
});
test('foreign and detached members reject without registration or reference rebinding',()=>{
  const {doc,first}=setup(),other=setup(),before=state(doc);
  for(const member of [null,new api.Section(),other.first])assert.throws(()=>doc.Objects.CreateSectionManager([first,member],true),{name:'ArgumentException',ParamName:'sections'});
  assert.deepEqual(state(doc),before);
});
test('occupied anchors are case insensitive and orphan manager objects still prohibit creation',()=>{
  const {doc,first}=setup(),manager=doc.Objects.CreateSectionManager([first],false);doc.NamedObjects.Remove('ACAD_SECTION_MANAGER');
  assert.throws(()=>doc.Objects.CreateSectionManager([],false),{name:'InvalidOperationException'});
  doc.NamedObjects.AddLoaded('ACAD_SECTION_MANAGER',manager,false);doc.Objects.EraseSectionManager(manager);
  doc.NamedObjects.Add('acad_section_manager',new api.DxfPlaceholder());assert.throws(()=>doc.Objects.CreateSectionManager([],false),{name:'InvalidOperationException'});
});
test('allocation accounts for owner-held ATTRIB handles absent from the registry',()=>{
  const {doc,first}=setup(),block=new api.Block('B');block.AttributeDefinitions.Add(new api.AttributeDefinition('T'));const insert=new api.Insert(block);doc.Entities.Add(insert);
  const attribute=insert.Attributes.get_Item(0);assert.equal(doc.GetObjectByHandle(attribute.Handle),null);doc.NumHandles=BigInt('0x'+attribute.Handle);
  const manager=doc.Objects.CreateSectionManager([first],false);assert.notEqual(manager.Handle,attribute.Handle);assert.equal(attribute.Owner,insert);
});
test('exhausted and invalid handle seeds reject without allocating a manager',()=>{
  const {doc,first}=setup();for(const seed of [0n,-1n,9223372036854775807n]){doc.NumHandles=seed;const before=state(doc);assert.throws(()=>doc.Objects.CreateSectionManager([first],false),{name:'InvalidOperationException'});assert.deepEqual(state(doc),before);}
});
test('CLASS count presence and identity survive creation and erasure without synthesis',()=>{
  for(const count of [null,37]){const {doc,first}=setup(),definition=new api.DxfClass('SECTION_MANAGER','AcDbSectionManager','ObjectDBX Classes');definition.ProxyFlags=1024;definition.InstanceCount=count;doc.Classes.Add(definition);
    const manager=doc.Objects.CreateSectionManager([first],false);assert.equal(doc.Classes.get_Item('SECTION_MANAGER'),definition);assert.equal(definition.InstanceCount,count===null?null:1);
    definition.ApplicationName='retained';definition.ProxyFlags=123;doc.Objects.EraseSectionManager(manager);assert.equal(definition.InstanceCount,count===null?null:0);assert.equal(definition.ApplicationName,'retained');assert.equal(definition.ProxyFlags,123);
  }
  const {doc}=setup();const manager=doc.Objects.CreateSectionManager([],false);doc.Objects.EraseSectionManager(manager);assert.equal(doc.Classes.Contains('SECTION_MANAGER'),false);
});
test('erasure detaches aliases and owned metadata but retains section targets and tombstone handles',()=>{
  const {doc,first}=setup(),manager=doc.Objects.CreateSectionManager([first],false),extension=new api.DxfDictionary(),note=new api.DxfXRecord();extension.Add('N',note);
  doc.Objects.SetExtensionDictionary(manager,extension);doc.NamedObjects.Add('ALIAS',manager);const handle=manager.Handle;
  doc.Objects.EraseSectionManager(manager);assert.equal(manager.Handle,handle);assert.equal(manager.IsErased,true);assert.equal(extension.IsErased,true);assert.equal(note.IsErased,true);
  assert.equal(doc.GetObjectByHandle(first.Handle),first);assert.equal(doc.NamedObjects.Contains('ALIAS'),false);assert.equal(manager.Owner,null);
  assert.throws(()=>doc.NamedObjects.Add('REVIVE',manager),{name:'InvalidOperationException'});assert.throws(()=>manager.ReplaceSections([],false),{name:'InvalidOperationException'});
});
test('incoming pointers reject erasure before state changes; arbitrary handles do not',()=>{
  const {doc,first}=setup(),manager=doc.Objects.CreateSectionManager([first],false),record=new api.DxfXRecord();doc.NamedObjects.Add('REF',record);record.Data.Add(new api.DxfTag(340,manager.Handle));
  const before=state(doc);assert.throws(()=>doc.Objects.EraseSectionManager(manager),{name:'InvalidOperationException'});assert.deepEqual(state(doc),before);
  record.Data.Clear();record.Data.Add(new api.DxfTag(320,manager.Handle));doc.Objects.EraseSectionManager(manager);assert.equal(record.IsErased,false);
});
test('ordinary ownership erase and clone do not bypass the explicit manager lifecycle',()=>{
  const {doc,first}=setup(),manager=doc.Objects.CreateSectionManager([first],false);
  assert.throws(()=>doc.Objects.EraseOwnedTree(manager),{name:'NotSupportedException'});assert.throws(()=>doc.Objects.CloneObject(manager,doc.NamedObjects,'Clone'),{name:'NotSupportedException'});
  assert.equal(manager.IsErased,false);
});
test('managed enumeration disposes even when MoveNext throws and disposal errors retain precedence',()=>{
  const move=new Error('move'),dispose=new Error('dispose');assert.throws(()=>ConsumeManagedEnumerable(enumerable([],{move(){throw move;},dispose(){throw dispose;}}),()=>{}),error=>error===dispose);
});
test('JavaScript iterable cleanup runs before manager validation and after early failure',()=>{
  const {doc,first}=setup();let closed=0;
  const source={*[Symbol.iterator](){try{yield first;}finally{closed++;doc.DrawingVariables.AcadVer=14;}}};
  assert.throws(()=>doc.Objects.CreateSectionManager(source,false),{name:'NotSupportedException'});assert.equal(closed,1);
  let returned=0;ConsumeManagedEnumerable({[Symbol.iterator](){return {next:()=>({done:true}),return(){returned++;return {done:true};}};}},()=>{});assert.equal(returned,1);
});
test('manager input corpus is deterministic, unique and contains no expected result table',()=>{
  const corpus=sectionManagerCorpus();assert.deepEqual(corpus,sectionManagerCorpus());assert.equal(corpus.length,174);assert.equal(new Set(corpus.map(p=>p.name)).size,174);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),5406);assert.ok(corpus.every(p=>!('expected'in p)));
});
