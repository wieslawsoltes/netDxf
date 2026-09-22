import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { MLeaderData } from '../../netDxf/Entities/MLeaderData.js';
import { registeredAnnotationsCorpus } from '../../tools/registered-annotations-corpus.mjs';
const {DxfDocument,Section,DxfSectionSettings,DxfSectionTypeSettings,DxfSectionGeometrySettings,
  DxfDictionary,DxfXRecord,DxfTag,Line,Block,Layer,Linetype,XData,XDataRecord,XDataCode,ApplicationRegistry,
  DxfMLeaderStyle,MultiLeader,MLeaderMTextContent,View}=api;
const snapshot=doc=>({seed:doc.DrawingVariables.HandleSeed,objects:Array.from(doc.AddedObjects,p=>[p.Key,p.Value,p.Value.Owner]),entities:[...doc.Entities.All]});
function leaderGraph(){
  const doc=new DxfDocument(18),style=new DxfMLeaderStyle();style.Properties.TextStyle=doc.TextStyles.get_Item('Standard');doc.Objects.AddMLeaderStyle('Style',style);
  const leader=new MultiLeader();leader.Properties.Style=style;leader.Properties.TextStyle=doc.TextStyles.get_Item('Standard');leader.Properties.LeaderLinetype=doc.Linetypes.get_Item('Continuous');leader.Properties.ContentType=0;
  doc.Entities.Add(leader);return {doc,leader,style};
}
function sectionGraph(){
  const doc=new DxfDocument(18),section=new Section(),line=new Line();doc.Entities.Add(line);doc.Entities.Add(section);
  const settings=new DxfSectionSettings();settings.SetTypeSettings([new DxfSectionTypeSettings(4,17,[section,line,line,null,settings],section.Owner.Record,'never-open.dwg',[new DxfSectionGeometrySettings()])]);
  doc.Objects.SetSectionSettings(section,settings);
  const extension=new DxfDictionary(),note=new DxfXRecord();note.Data.Add(new DxfTag(330,settings.Handle));extension.Add('note',note);extension.Add('alias',note,false);doc.Objects.SetExtensionDictionary(section,extension);
  settings.PersistentReactors.Add(section);
  for(const [owner,target] of [[section,settings],[settings,section]]){const data=new XData(new ApplicationRegistry('SECTION_APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,target.Handle));owner.XData.Add(data);}
  return {doc,section,line,settings,note};
}
test('registered-document inspection does not allocate the lazy named-object database',()=>{
  const doc=new DxfDocument(18),line=new Line();doc.Entities.Add(line);const before=snapshot(doc);
  assert.equal(MLeaderData.RegisteredDocument(line),doc);assert.deepEqual(snapshot(doc),before);
  assert.equal(MLeaderData.RegisteredDocument(new Line()),null);
});
test('registered MULTILEADER model is enumerated and validates its real owner profile',()=>{
  const {doc,leader}=leaderGraph();assert.deepEqual([...doc.Entities.MultiLeaders],[leader]);leader.Validate();
  leader.Properties.LeaderExtendToText=true;doc.DrawingVariables.AcadVer=16;
  assert.throws(()=>leader.Validate(),{name:'NotSupportedException'});doc.DrawingVariables.AcadVer=18;leader.Validate();
});
test('foreign MULTILEADER child insertion checks references before assigning parent',()=>{
  const {doc,leader}=leaderGraph(),foreign=new DxfDocument(18),content=new MLeaderMTextContent();content.Style=foreign.TextStyles.get_Item('Standard');
  const before=snapshot(doc);assert.throws(()=>{leader.Context.MText=content;},{name:'ArgumentException'});
  assert.equal(content.Parent,null);assert.equal(leader.Context.MText,null);assert.deepEqual(snapshot(doc),before);
});
test('style duplicates and incompatible dictionaries preserve detached ownership',()=>{
  const {doc,style}=leaderGraph(),copy=new DxfMLeaderStyle();copy.Properties.TextStyle=style.Properties.TextStyle;const before=snapshot(doc);
  assert.throws(()=>doc.Objects.AddMLeaderStyle('Style',copy),{name:'ArgumentException'});assert.equal(copy.Owner,null);assert.equal(copy.Database,null);assert.deepEqual(snapshot(doc),before);
  const other=new DxfDocument(18);other.NamedObjects.Add('ACAD_MLEADERSTYLE',new api.DxfPlaceholder());const incoming=new DxfMLeaderStyle();incoming.Properties.TextStyle=other.TextStyles.get_Item('Standard');
  assert.throws(()=>other.Objects.AddMLeaderStyle('Style',incoming),{name:'InvalidOperationException'});assert.equal(incoming.Owner,null);
});
test('live reference scan retains duplicate model uses and combines graphics references once',()=>{
  const {doc,leader}=leaderGraph(),line=doc.Linetypes.get_Item('Continuous');leader.Linetype=line;
  const rows=[...doc.Linetypes.GetReferences(line)].filter(r=>r.Reference===leader);assert.equal(rows.length,1);assert.equal(rows[0].Uses,2);
  doc.Entities.Remove(leader);assert.equal([...doc.Linetypes.GetReferences(line)].some(r=>r.Reference===leader),false);
});
test('ordinary SECTION removal refuses settings sources and every owned descendant',()=>{
  const {doc,section,line}=sectionGraph();const before=snapshot(doc);assert.equal(doc.Entities.Remove(section),false);assert.equal(doc.Entities.Remove(line),false);assert.deepEqual(snapshot(doc),before);
});
test('SECTION graph clone preserves aliases, cyclic sources, metadata and reciprocal owners',()=>{
  const {doc,section,line,settings,note}=sectionGraph(),copy=doc.Objects.CloneSection(section,section.Owner),cloned=copy.GeometrySettings;
  assert.notEqual(cloned,settings);assert.equal(cloned.Owner,copy);
  assert.deepEqual([...cloned.TypeSettings.get_Item(0).SourceObjects],[copy,line,line,null,cloned]);
  assert.equal(cloned.TypeSettings.get_Item(0).DestinationBlock,copy.Owner.Record);assert.equal(cloned.PersistentReactors.get_Item(0),copy);
  assert.equal(copy.ExtensionDictionary.get_Item('note'),copy.ExtensionDictionary.get_Item('alias'));assert.notEqual(copy.ExtensionDictionary.get_Item('note'),note);
  assert.equal(copy.ExtensionDictionary.get_Item('note').Data.get_Item(0).Value,cloned.Handle);
  assert.equal(copy.XData.get_Item('SECTION_APP').XDataRecord.get_Item(0).Value,cloned.Handle);
  assert.equal(cloned.XData.get_Item('SECTION_APP').XDataRecord.get_Item(0).Value,copy.Handle);assert.equal(doc.Objects.Validate().Count,0);
});
test('SECTION erasure is terminal, preserves tombstone handles and keeps external geometry',()=>{
  const {doc,section,line,settings,note}=sectionGraph(),handles=[section,settings,note].map(x=>x.Handle);doc.Objects.EraseSection(section);
  for(const [i,item] of [section,settings,note].entries()){assert.equal(item.IsErased,true);assert.equal(item.Handle,handles[i]);assert.equal(doc.GetObjectByHandle(handles[i]),null);}
  assert.equal(doc.GetObjectByHandle(line.Handle),line);assert.equal(doc.Entities.All.Count,1);assert.equal(settings.Owner,section);
  assert.throws(()=>doc.Entities.Add(section),{name:'InvalidOperationException'});assert.throws(()=>section.Clone(),{name:'InvalidOperationException'});
  assert.equal(doc.ApplicationRegistries.GetReferences('SECTION_APP').Count,0);
});
test('VIEW live-section reference blocks both removal paths until explicitly cleared',()=>{
  const doc=new DxfDocument(18),section=new Section();doc.Entities.Add(section);const view=doc.Views.Add(new View('View'));view.LiveSection=section;
  void doc.Objects;const before=snapshot(doc);assert.equal(doc.Entities.Remove(section),false);assert.throws(()=>doc.Objects.EraseSection(section),{name:'InvalidOperationException'});assert.deepEqual(snapshot(doc),before);
  view.ClearLiveSectionReference();doc.Objects.EraseSection(section);assert.equal(section.IsErased,true);assert.throws(()=>{view.LiveSection=section;},{name:'ArgumentException'});
});
test('SECTION settings cannot be erased separately from their reciprocal owner slot',()=>{
  const {doc,settings}=sectionGraph(),before=snapshot(doc);assert.throws(()=>doc.Objects.EraseOwnedTree(settings),{name:'NotSupportedException'});assert.deepEqual(snapshot(doc),before);
});
test('nested invalid SECTION adoption rejects before assigning any graph handle',()=>{
  const doc=new DxfDocument(14),inner=new Block('Inner'),outer=new Block('Outer'),section=new Section();inner.Entities.Add(section);outer.Entities.Add(new api.Insert(inner));
  const insert=new api.Insert(outer),before=snapshot(doc);assert.throws(()=>doc.Entities.Add(insert),{name:'NotSupportedException'});assert.deepEqual(snapshot(doc),before);
  for(const item of [insert,outer,inner,section])assert.equal(item.Handle,null);
});
test('SECTION cross-document cloning requires resources and external geometry mappings',()=>{
  const {doc,section,line}=sectionGraph(),dest=new DxfDocument(18),destLine=new Line();dest.Entities.Add(destLine);void dest.Objects;
  const block=dest.Blocks.get_Item('*Model_Space'),before=snapshot(dest);assert.throws(()=>dest.Objects.CloneSection(section,block),{name:'InvalidOperationException'});assert.deepEqual(snapshot(dest),before);
  const mappings=new Map([[section.Layer,dest.Layers.get_Item('0')],[section.Linetype,dest.Linetypes.get_Item('ByLayer')],[line,destLine]]);
  const copy=dest.Objects.CloneSection(section,block,mappings);assert.equal(copy.Layer,dest.Layers.get_Item('0'));assert.equal(copy.GeometrySettings.TypeSettings.get_Item(0).SourceObjects.get_Item(1),destLine);
  assert.equal(doc.Objects.Validate().Count,0);assert.equal(dest.Objects.Validate().Count,0);
});
test('SECTION clone mapping conflicts reject without destination or source mutation',()=>{
  const {doc,section}=sectionGraph(),other=doc.Blocks.Add(new Block('Other')),before=snapshot(doc);
  assert.throws(()=>doc.Objects.CloneSection(section,other,new Map([[section.Owner,section.Owner]])),{name:'ArgumentException',ParamName:'externalReferences'});assert.deepEqual(snapshot(doc),before);
});
test('SECTION clone does not invoke public resource Clone overrides',()=>{
  class GuardLayer extends Layer{Clone(){throw new Error('must not call Layer.Clone');}}
  class GuardLinetype extends Linetype{Clone(){throw new Error('must not call Linetype.Clone');}}
  const doc=new DxfDocument(18),section=new Section();section.Layer=new GuardLayer('L');section.Linetype=new GuardLinetype('P');doc.Entities.Add(section);
  const copy=doc.Objects.CloneSection(section,section.Owner);assert.equal(copy.Layer,section.Layer);assert.equal(copy.Linetype,section.Linetype);
});
test('SECTION cloning checks the live source after caller mapping iteration',()=>{
  const {doc,section}=sectionGraph(),destination=new DxfDocument(18);void destination.Objects;const before=snapshot(destination);
  const mapping={*[Symbol.iterator](){doc.Objects.EraseSection(section);}};
  assert.throws(()=>destination.Objects.CloneSection(section,destination.Blocks.get_Item('*Model_Space'),mapping),{name:'ArgumentException'});assert.deepEqual(snapshot(destination),before);assert.equal(section.IsErased,true);
});
test('SECTION clone preflights numeric handle exhaustion before registration',()=>{
  const {doc,section}=sectionGraph();doc.NumHandles=9223372036854775806n;const before=snapshot(doc);
  assert.throws(()=>doc.Objects.CloneSection(section,section.Owner),{name:'InvalidOperationException'});assert.deepEqual(snapshot(doc),before);
});
test('private opaque SECTION ownership rejects cloning and erasure without mutations',()=>{
  const {doc,section}=sectionGraph(),opaque=new api.DxfOpaqueObject('PRIVATE',[new DxfTag(100,'AcDbPrivate')]);section.ExtensionDictionary.Add('private',opaque);const before=snapshot(doc);
  assert.throws(()=>doc.Objects.CloneSection(section,section.Owner),{name:'NotSupportedException'});assert.throws(()=>doc.Objects.EraseSection(section),{name:'NotSupportedException'});assert.deepEqual(snapshot(doc),before);
});
test('SECTION erasure keeps caller event subscriptions but releases document XData handlers',()=>{
  const {doc,section}=sectionGraph();let events=0;section.XDataAddAppReg.Add(()=>events++);doc.Objects.EraseSection(section);
  section.XData.Add(new XData(new ApplicationRegistry('DEAD')));assert.equal(events,1);assert.equal(doc.ApplicationRegistries.Contains('DEAD'),false);
});
test('registered annotation inputs are deterministic and retain every declared operation',()=>{
  const corpus=registeredAnnotationsCorpus();assert.deepEqual(corpus,registeredAnnotationsCorpus());assert.equal(corpus.length,112);assert.equal(new Set(corpus.map(p=>p.name)).size,112);assert.equal(corpus.reduce((sum,p)=>sum+p.request.steps.length,0),6633);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
});
