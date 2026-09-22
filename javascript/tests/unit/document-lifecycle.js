import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { ReferenceMap } from '../../runtime/DatabaseReferenceMap.js';
import { DocumentOracleSession } from '../../tools/DocumentOracleSession.mjs';
import { documentOwnershipCorpus } from '../../tools/document-ownership-corpus.mjs';
const doc = () => new api.DxfDocument(api.DxfVersion.AutoCad2018);
const add = (document, entity = new api.Line()) => { document.Entities.Add(entity); return entity; };
const rootState = document => [document.DrawingVariables.HandleSeed, document.Objects.Items.Count, document.AddedObjects.Count];

test('document public collection properties cannot be overwritten', () => {
  const document=doc();
  for(const key of ['SupportFolders','Comments','Classes','Entities']) {
    const value=document[key]; assert.throws(()=>{document[key]=null;},TypeError); assert.equal(document[key],value);
  }
});
test('document standalone entry resolves the same cyclic module identities', async () => {
  const standalone=await import('../../netDxf/DxfDocument.js');
  const database=await import('../../netDxf/Objects/DxfObjectDatabase.js');
  assert.equal(standalone.DxfDocument,api.DxfDocument); assert.ok(new standalone.DxfDocument().Objects instanceof database.DxfObjectDatabase);
});
test('redraw entries validate uniqueness, ownership and index order before mutation', () => {
  const document=doc(),first=add(document),second=add(document),record=first.Owner.Record;
  const table=document.Objects.CreateSortentsTable(record,[new api.DxfSortOrderEntry(first,'FF')]);
  assert.throws(()=>table.Entries.Add(new api.DxfSortOrderEntry(first,'A')),{name:'ArgumentException',ParamName:'item'});
  assert.throws(()=>table.Entries.Insert(-1,null),{name:'ArgumentOutOfRangeException',ParamName:'index'});
  assert.throws(()=>table.Entries.Add(null),{name:'ArgumentNullException',ParamName:'item'});
  table.Entries.Add(new api.DxfSortOrderEntry(second,'10'));table.Entries.RemoveAt(0);table.Entries.Add(new api.DxfSortOrderEntry(first,'11'));
  assert.deepEqual(Array.from(table.Entries,e=>e.Entity),[second,first]);assert.equal(document.Objects.Validate().Count,0);
});
test('redraw allocation rejects foreign-block entities before attaching dictionaries', () => {
  const document=doc(),line=add(document),block=new api.Block('Other'),other=new api.Line();block.Entities.Add(other);document.Blocks.Add(block);
  const database=document.Objects,before=rootState(document);
  assert.throws(()=>database.CreateSortentsTable(line.Owner.Record,[new api.DxfSortOrderEntry(other,'1')]),{name:'ArgumentException'});
  assert.deepEqual(rootState(document),before);assert.equal(line.Owner.Record.ExtensionDictionary,null);
});
test('redraw regeneration keeps boxed Int16 header flags and original variable identity', () => {
  const document=doc(),line=add(document),variable=new api.HeaderVariable('$SORTENTS',280,new BoxedScalar('Int16',3));
  document.DrawingVariables.AddCustomVariable(variable);document.Objects.CreateSortentsTable(line.Owner.Record,[new api.DxfSortOrderEntry(line,'ABC')]);
  const out={};assert.equal(document.DrawingVariables.TryGetCustomVariable('$SORTENTS',out),true);assert.equal(out.value,variable);
  assert.equal(variable.Value.Type,'Int16');assert.equal(variable.Value.Value,19);
});
test('malformed redraw header fails before any graph allocation', () => {
  const document=doc(),line=add(document);void document.Objects;
  document.DrawingVariables.AddCustomVariable(new api.HeaderVariable('$SORTENTS',280,new BoxedScalar('Int32',3)));
  const before=rootState(document);
  assert.throws(()=>document.Objects.CreateSortentsTable(line.Owner.Record,[]),{name:'InvalidOperationException'});
  assert.deepEqual(rootState(document),before);assert.equal(line.Owner.Record.ExtensionDictionary,null);
});
test('redraw disabled regeneration does not create a header variable', () => {
  const document=doc(),line=add(document);document.Objects.CreateSortentsTable(line.Owner.Record,[],false);
  assert.equal(document.DrawingVariables.TryGetCustomVariable('$SORTENTS',{}),false);
});
test('redraw collection iterator invalidates on replacement and erasure treats keys as values', () => {
  const document=doc(),line=add(document),unrelated=new api.DxfPlaceholder();document.NamedObjects.Add('target',unrelated);
  const table=document.Objects.CreateSortentsTable(line.Owner.Record,[new api.DxfSortOrderEntry(line,unrelated.Handle)]);
  const iterator=table.Entries.GetEnumerator();assert.equal(iterator.MoveNext(),true);
  table.Entries.set_Item(0,new api.DxfSortOrderEntry(line,unrelated.Handle));assert.throws(()=>iterator.MoveNext(),{name:'InvalidOperationException'});
  document.Objects.EraseOwnedTree(unrelated);assert.equal(table.Entries.Count,1);assert.equal(unrelated.IsErased,true);
});
test('SUN lifecycle attaches reciprocal ownership and erases without losing the tombstone handle', () => {
  const document=doc(),sun=new api.DxfSun();document.Objects.SetSun(document.Viewport,sun);const handle=sun.Handle;
  assert.equal(sun.Owner,document.Viewport);assert.equal(document.Viewport.Sun,sun);assert.equal(document.Objects.Validate().Count,0);
  document.Objects.EraseOwnedTree(sun);assert.equal(document.Viewport.Sun,null);assert.equal(sun.Handle,handle);assert.equal(document.GetObjectByHandle(handle),null);assert.equal(sun.IsErased,true);
});
test('SUN cannot be resurrected and occupied hosts reject replacement before allocation', () => {
  const document=doc(),sun=new api.DxfSun();document.Objects.SetSun(document.Viewport,sun);const before=rootState(document);
  assert.throws(()=>document.Objects.SetSun(document.Viewport,new api.DxfSun()),{name:'InvalidOperationException'});assert.deepEqual(rootState(document),before);
  document.Objects.EraseOwnedTree(sun);assert.throws(()=>document.Objects.SetSun(document.Viewport,sun),{name:'ArgumentException',ParamName:'sun'});
});
test('SUN profile validation is retained instead of upgrading the default document', () => {
  const document=new api.DxfDocument(),sun=new api.DxfSun();
  assert.throws(()=>document.Objects.SetSun(document.Viewport,sun),{name:'NotSupportedException'});assert.equal(sun.Database,null);
});
test('SUN cloning maps source host and keeps source/destination identities distinct', () => {
  const first=doc(),second=doc(),sun=new api.DxfSun();first.Objects.SetSun(first.Viewport,sun);
  const clone=second.Objects.CloneSun(sun,second.Viewport);assert.notEqual(clone,sun);assert.equal(clone.Owner,second.Viewport);assert.equal(first.Viewport.Sun,sun);assert.equal(second.Objects.Validate().Count,0);
});
test('extension cloning rejects owner mapping conflicts without allocating handles', () => {
  const document=doc(),first=add(document),second=add(document),extension=new api.DxfDictionary();extension.Add('x',new api.DxfPlaceholder());document.Objects.SetExtensionDictionary(first,extension);
  const before=rootState(document);
  assert.throws(()=>document.Objects.CloneExtensionDictionary(first,second,new Map([[first,first]])),{name:'ArgumentException',ParamName:'externalReferences'});
  assert.deepEqual(rootState(document),before);assert.equal(second.ExtensionDictionary,null);
});
test('ownership clone accepts a side-effect-free explicit reference mapping snapshot', () => {
  const document=doc(),first=add(document),second=add(document),extension=new api.DxfDictionary(),record=new api.DxfXRecord();
  extension.Add('x',record);document.Objects.SetExtensionDictionary(first,extension);record.Data.Add(new api.DxfTag(330,first.Handle));
  const clone=document.Objects.CloneExtensionDictionary(first,second);assert.equal(clone.get_Item('x').Data.get_Item(0).Value,second.Handle);
  assert.equal(record.Data.get_Item(0).Value,first.Handle);
});
test('reference-map transport retains identity, duplicate errors and enumerable exceptions', () => {
  const key={},other={};assert.equal(ReferenceMap([[key,other]]).get(key),other);
  assert.throws(()=>ReferenceMap([[null,other]]),{name:'ArgumentNullException',ParamName:'key'});
  assert.throws(()=>ReferenceMap([[key,other],[key,other]]),{name:'ArgumentException'});
  const error=new Error('enumerable');assert.throws(()=>ReferenceMap({*[Symbol.iterator](){yield [key,other];throw error;}}),e=>e===error);
});
test('spatial-filter registration creates reciprocal dictionary and reactor state', () => {
  const document=doc(),insert=add(document,new api.Insert(new api.Block('B'))),filter=new api.DxfSpatialFilter();document.Objects.SetSpatialFilter(insert,filter);
  const filters=insert.ExtensionDictionary.get_Item('ACAD_FILTER');assert.equal(filters.get_Item('SPATIAL'),filter);assert.equal(filter.Owner,filters);assert.equal(filter.PersistentReactors.Contains(filters),true);
  assert.equal(document.Objects.Validate().Count,0);assert.throws(()=>document.Objects.SetSpatialFilter(insert,new api.DxfSpatialFilter()),{name:'InvalidOperationException'});
});
test('GEODATA attach and erase preserve block registration and reciprocal lookup', () => {
  const document=doc(),record=document.Blocks.get_Item('*Model_Space').Record,data=new api.DxfGeoData(record);document.Objects.SetGeoData(data);
  assert.equal(document.Objects.GetGeoData(record),data);assert.equal(document.Objects.Validate().Count,0);document.Objects.EraseOwnedTree(data);
  assert.equal(document.Objects.GetGeoData(record),null);assert.equal(record.Owner,document.Blocks);
});
test('plot-settings authoring copies input and wipeout updates retain identity', () => {
  const document=doc(),settings=new api.PlotSettings(),plot=document.Objects.AddPlotSettings('Page',settings);
  assert.notEqual(plot.Settings,settings);assert.equal(plot.Settings.PageSetupName,'Page');assert.notEqual(settings.PageSetupName,'Page');
  const variables=document.Objects.SetWipeoutVariables(true),handle=variables.Handle;assert.equal(document.Objects.SetWipeoutVariables(false),variables);assert.equal(variables.Handle,handle);
});
test('database Items snapshots remain read-only and independent of later registrations', () => {
  const document=doc(),items=document.Objects.Items;document.NamedObjects.Add('x',new api.DxfPlaceholder());assert.equal(items.Count,1);assert.equal(document.Objects.Items.Count,2);
  assert.equal(typeof items.Add,'undefined');
});
test('document corpus retains every original input and exact operation count on both newline hosts', () => {
  for(const newline of ['\n','\r\n']) {const corpus=documentOwnershipCorpus(newline);assert.deepEqual(corpus,documentOwnershipCorpus(newline));assert.equal(corpus.length,188);assert.equal(new Set(corpus.map(p=>p.name)).size,188);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),8130);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));}
});
for(const bad of [null,[],[{}],[{result:null,error:null}]])test('document oracle rejects malformed or incomplete observations '+JSON.stringify(bad),async()=>{
  let factories=0,closed=0;const session=new DocumentOracleSession(()=>{const index=++factories;return {request:async()=>index===1?bad:[{result:null,error:'ArgumentException',param:'value'}],close:async()=>{closed++;}};});
  const request={op:'document-ownership',steps:[{method:'get'}]};assert.equal((await session.observe(request)).ok,false);
  const result=await session.observe(request);assert.equal(result.ok,true);assert.equal(result.value[0].error,'ArgumentException');assert.equal(factories,2);await session.close();assert.equal(closed,2);
});
test('document oracle retains process failures and close diagnostics without inventing native values',async()=>{
  const session=new DocumentOracleSession(()=>({request:async()=>{throw new Error('crashed');},close:async()=>{throw new Error('close');}}));
  const result=await session.observe({op:'document-ownership',steps:[{method:'get'}]});assert.equal(result.ok,false);assert.match(result.failure.message,/crashed/);assert.match(result.failure.closeError,/close/);assert.equal(Object.hasOwn(result,'value'),false);await session.close();
});
