import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, Line, Layer, Linetype, TextStyle, AttributeDefinition, Block, Insert, Layout, AlignedDimension, LayerState,
  LayerStateProperties, LayerPropertiesRestoreFlags, DxfDictionary, DxfPlaceholder, XData, ApplicationRegistry } from '../../index.js';
import { EventHook } from '../../runtime/EventHook.js';
import { Listen, Unlisten } from '../../runtime/RegisteredTable.js';
import { documentOwnershipCorpus } from '../../tools/document-ownership-corpus.mjs';

test('typed document default handle allocation and model-space registration',()=>{
  const doc=new DxfDocument();assert.equal(doc.Handle,'0');assert.equal(doc.DrawingVariables.HandleSeed,'22');
  assert.equal(doc.Layers.StateManager.Handle,'6');assert.equal(doc.Layouts.get_Item('Model').AssociatedBlock.Record.Owner,doc.Blocks);
  assert.equal(doc.Entities.ActiveLayout,'Model');assert.equal(doc.AddedObjects.Count,32);
});
test('typed line registration canonicalizes resources and resolves canonical handles',()=>{
  const doc=new DxfDocument(),line=new Line();doc.Entities.Add(line);
  assert.equal(line.Handle,'22');assert.equal(doc.GetObjectByHandle('000022'),line);
  assert.equal(line.Owner,doc.Layouts.get_Item('Model').AssociatedBlock);assert.equal(line.Layer,doc.Layers.get_Item('0'));
  assert.equal(doc.Layers.GetReferences('0').ToArray?.()?.length??Array.from(doc.Layers.GetReferences('0')).length,2);
});
test('typed removal initializes named objects and releases entity metadata',()=>{
  const doc=new DxfDocument(),line=new Line();doc.Entities.Add(line);assert.equal(doc.Entities.Remove(line),true);
  assert.equal(line.Owner,null);assert.equal(line.Handle,null);assert.equal(doc.GetObjectByHandle('22'),null);
  assert.equal(doc.NamedObjects.Handle,'23');assert.equal(doc.DrawingVariables.HandleSeed,'24');
});
test('registered resource changes update dependency counts and preserve referenced records',()=>{
  const doc=new DxfDocument(),line=new Line();line.Layer=new Layer('A');doc.Entities.Add(line);
  assert.equal(doc.Layers.Remove('A'),false);line.Layer=new Layer('B');assert.equal(doc.Layers.Remove('A'),true);
  assert.equal(doc.Layers.GetReferences('B').get_Item(0).Reference,line);assert.equal(doc.Layers.Remove('B'),false);
});
test('document ownership rejects cross-document entity insertion before mutation',()=>{
  const first=new DxfDocument(),second=new DxfDocument(),line=new Line();first.Entities.Add(line);
  assert.throws(()=>second.Entities.Add(line),{name:'ArgumentException',ParamName:'entity'});assert.equal(Array.from(second.Entities.All).length,0);
});
test('registered block and INSERT removal retain correct table dependencies',()=>{
  const doc=new DxfDocument(),block=new Block('A');block.Entities.Add(new Line());const insert=new Insert(block);doc.Entities.Add(insert);
  assert.equal(doc.Blocks.Remove(block),false);assert.equal(block.Record.Owner,doc.Blocks);assert.equal(doc.Entities.Remove(insert),true);assert.equal(doc.Blocks.Remove(block),true);
});
test('paper-space entities are owned by the selected associated block',()=>{
  const doc=new DxfDocument(),sheet=doc.Layouts.Add(new Layout('Sheet'));doc.Entities.ActiveLayout='Sheet';const line=new Line();doc.Entities.Add(line);
  assert.equal(line.Owner,sheet.AssociatedBlock);assert.equal(sheet.Viewport.Owner,sheet.AssociatedBlock);
  assert.equal(doc.Layouts.Remove('Model'),false);assert.equal(doc.Layouts.Remove('Sheet'),true);assert.equal(line.Owner,null);
});
test('dimension block regeneration registers the new block and releases the old graph',()=>{
  const doc=new DxfDocument();doc.BuildDimensionBlocks=true;const dimension=new AlignedDimension();doc.Entities.Add(dimension);const old=dimension.Block;
  dimension.Update();assert.notEqual(dimension.Block,old);assert.equal(old.Record.Owner,null);assert.equal(dimension.Block.Record.Owner,doc.Blocks);
});
test('application registry metadata binds by canonical object and blocks premature removal',()=>{
  const doc=new DxfDocument(),line=new Line(),registry=new ApplicationRegistry('APP');line.XData.Add(new XData(registry));doc.Entities.Add(line);
  const canonical=doc.ApplicationRegistries.get_Item('APP');assert.notEqual(canonical,registry);assert.equal(line.XData.get_Item('APP').ApplicationRegistry,canonical);
  assert.equal(doc.ApplicationRegistries.Remove(canonical),false);line.XData.Remove('APP');assert.equal(doc.ApplicationRegistries.Remove(canonical),true);
});
test('layer states restore selected flags and clone independently',()=>{
  const doc=new DxfDocument(),layer=doc.Layers.Add(new Layer('A'));layer.IsVisible=false;doc.Layers.StateManager.AddNew('State');
  layer.IsVisible=true;doc.Layers.StateManager.Options=LayerPropertiesRestoreFlags.Hidden;doc.Layers.StateManager.Restore('State');assert.equal(layer.IsVisible,false);
  const state=doc.Layers.StateManager.get_Item('State');state.PaperSpace=true;const clone=state.Clone('Copy');
  assert.equal(clone.PaperSpace,false);assert.notEqual(clone.Properties.get_Item('A'),state.Properties.get_Item('A'));
});
test('layer state CopyFrom clears nonselected flags exactly as the source does',()=>{
  const layer=new Layer('A');layer.IsVisible=false;layer.IsFrozen=true;const property=new LayerStateProperties(layer);
  property.CopyFrom(layer,LayerPropertiesRestoreFlags.Color);assert.equal(property.Flags,0);
});
test('portable layer-state LAS roundtrip preserves basic state',()=>{
  const state=new LayerState('State',[new Layer('A')]);state.Description='metadata';state.PaperSpace=true;
  const restored=LayerState.LoadText(state.ToLasString());assert.equal(restored.Description,'metadata');assert.equal(restored.PaperSpace,true);assert.equal(restored.Properties.Count,1);
});
test('database registration adopts dictionary graphs and returns immutable snapshots',()=>{
  const doc=new DxfDocument(),root=doc.NamedObjects,folder=new DxfDictionary(),leaf=new DxfPlaceholder();folder.Add('leaf',leaf);root.Add('folder',folder);
  assert.equal(folder.Database,doc.Objects);assert.equal(leaf.Owner,folder);assert.equal(doc.Objects.Validate().Count,0);
  const snapshot=doc.Objects.Items;root.Add('second',new DxfPlaceholder());assert.equal(snapshot.Count,3);assert.equal(doc.Objects.Items.Count,4);
});
test('database ownership clone remaps descendants and preserves the source graph',()=>{
  const doc=new DxfDocument(),folder=new DxfDictionary(),leaf=new DxfPlaceholder();folder.Add('leaf',leaf);doc.NamedObjects.Add('folder',folder);
  const clone=doc.Objects.Clone(folder,doc.NamedObjects,'clone');assert.notEqual(clone,folder);assert.notEqual(clone.get_Item('leaf'),leaf);
  assert.equal(clone.get_Item('leaf').Owner,clone);assert.equal(doc.Objects.Validate().Count,0);
});
test('database extension dictionaries reject the layer-table reserved slot',()=>{
  const doc=new DxfDocument();assert.throws(()=>doc.Objects.SetExtensionDictionary(doc.Layers,new DxfDictionary()),{name:'InvalidOperationException'});
  const line=new Line();doc.Entities.Add(line);const extension=new DxfDictionary();doc.Objects.SetExtensionDictionary(line,extension);assert.equal(extension.Owner,line);assert.equal(line.ExtensionDictionary,extension);
});
test('ownership corpus contains only deterministic inputs, not embedded results',()=>{
  const first=documentOwnershipCorpus();assert.deepEqual(first,documentOwnershipCorpus());assert.equal(new Set(first.map(p=>p.name)).size,first.length);assert.ok(first.every(p=>!('expected'in p)));
});

// Regression expectations follow pinned DxfDocument.cs registration/removal order.
// These are supplemental cases, not replacements for original C# test identities.
const hasAttributeReference=(table,name,attribute)=>Array.from(table.GetReferences(name)).some(item=>item.Reference===attribute);

test('ATTRDEF add observers precede layer/linetype listeners but follow the style listener',()=>{
  const doc=new DxfDocument(),attribute=new AttributeDefinition('TAG');
  const oldLayer=attribute.Layer.Name,oldLinetype=attribute.Linetype.Name;
  const layer=new Layer('ObserverLayer'),linetype=new Linetype('ObserverLinetype'),style=new TextStyle('ObserverStyle','txt.shx');
  let observations=0;
  doc.AddedObjects.AddItem.Add((_,e)=>{
    if(e.Item.Value!==attribute)return;
    observations++;
    assert.equal(doc.GetObjectByHandle(attribute.Handle),attribute);
    attribute.Layer=layer;attribute.Linetype=linetype;attribute.Style=style;
  });
  doc.AddAttributeDefinitionToDocument(attribute);
  assert.equal(observations,1);
  assert.equal(doc.Layers.get_Item(layer.Name),null);
  assert.equal(doc.Linetypes.get_Item(linetype.Name),null);
  assert.equal(attribute.Layer,layer);assert.equal(attribute.Linetype,linetype);
  assert.equal(attribute.Style,doc.TextStyles.get_Item(style.Name));
  assert.equal(hasAttributeReference(doc.Layers,oldLayer,attribute),true);
  assert.equal(hasAttributeReference(doc.Linetypes,oldLinetype,attribute),true);
  assert.equal(hasAttributeReference(doc.TextStyles,style.Name,attribute),true);
});

test('ATTRDEF linetype binding callbacks cannot observe an early layer listener',()=>{
  const doc=new DxfDocument(),attribute=new AttributeDefinition('TAG'),layer=new Layer('DuringLinetypeBinding');
  const originalLayer=attribute.Layer.Name;
  let observations=0;
  attribute.LinetypeChanged.Add(()=>{observations++;attribute.Layer=layer;});
  doc.AddAttributeDefinitionToDocument(attribute);
  assert.equal(observations,1);assert.equal(attribute.Layer,layer);
  assert.equal(doc.Layers.get_Item(layer.Name),null);
  assert.equal(hasAttributeReference(doc.Layers,originalLayer,attribute),true);
});

test('ATTRDEF duplicate handle failure retains only the completed style subscription',()=>{
  const doc=new DxfDocument(),attribute=new AttributeDefinition('TAG');
  attribute.Handle=doc.Handle;
  assert.throws(()=>doc.AddAttributeDefinitionToDocument(attribute,false),{name:'ArgumentException'});
  attribute.Layer=new Layer('AfterDuplicateLayer');attribute.Linetype=new Linetype('AfterDuplicateLinetype');
  attribute.Style=new TextStyle('AfterDuplicateStyle','txt.shx');
  assert.equal(doc.GetObjectByHandle(doc.Handle),doc);
  assert.equal(doc.Layers.get_Item('AfterDuplicateLayer'),null);
  assert.equal(doc.Linetypes.get_Item('AfterDuplicateLinetype'),null);
  assert.equal(attribute.Style,doc.TextStyles.get_Item('AfterDuplicateStyle'));
});

test('ATTRDEF throwing add observer preserves the indexed object without late listeners',()=>{
  const doc=new DxfDocument(),attribute=new AttributeDefinition('TAG'),failure=new Error('add observer');
  doc.AddedObjects.AddItem.Add((_,e)=>{if(e.Item.Value===attribute)throw failure;});
  assert.throws(()=>doc.AddAttributeDefinitionToDocument(attribute),error=>error===failure);
  assert.equal(doc.GetObjectByHandle(attribute.Handle),attribute);
  attribute.Layer=new Layer('AfterAddFailureLayer');attribute.Linetype=new Linetype('AfterAddFailureLinetype');
  attribute.Style=new TextStyle('AfterAddFailureStyle','txt.shx');
  assert.equal(doc.Layers.get_Item('AfterAddFailureLayer'),null);
  assert.equal(doc.Linetypes.get_Item('AfterAddFailureLinetype'),null);
  assert.equal(attribute.Style,doc.TextStyles.get_Item('AfterAddFailureStyle'));
});

test('ATTRDEF successful registration and removal keep ordinary resource updates working',()=>{
  const doc=new DxfDocument(),attribute=new AttributeDefinition('TAG');
  doc.AddAttributeDefinitionToDocument(attribute);
  attribute.Layer=new Layer('RegisteredLayer');attribute.Linetype=new Linetype('RegisteredLinetype');
  attribute.Style=new TextStyle('RegisteredStyle','txt.shx');
  for(const [table,name] of [[doc.Layers,'RegisteredLayer'],[doc.Linetypes,'RegisteredLinetype'],[doc.TextStyles,'RegisteredStyle']])
    assert.equal(hasAttributeReference(table,name,attribute),true);
  const handle=attribute.Handle;
  assert.equal(doc.RemoveAttributeDefinitionFromDocument(attribute),true);
  assert.equal(doc.GetObjectByHandle(handle),null);assert.equal(attribute.Handle,null);assert.equal(attribute.Owner,null);
  for(const [table,name] of [[doc.Layers,'RegisteredLayer'],[doc.Linetypes,'RegisteredLinetype'],[doc.TextStyles,'RegisteredStyle']])
    assert.equal(hasAttributeReference(table,name,attribute),false);
  attribute.Layer=new Layer('DetachedLayer');attribute.Linetype=new Linetype('DetachedLinetype');attribute.Style=new TextStyle('DetachedStyle','txt.shx');
  assert.equal(doc.Layers.get_Item('DetachedLayer'),null);assert.equal(doc.Linetypes.get_Item('DetachedLinetype'),null);assert.equal(doc.TextStyles.get_Item('DetachedStyle'),null);
});

test('ATTRDEF removal observers retain layer/linetype listeners after style detachment',()=>{
  const doc=new DxfDocument(),attribute=new AttributeDefinition('TAG');
  doc.AddAttributeDefinitionToDocument(attribute);
  const handle=attribute.Handle;
  let observations=0;
  doc.AddedObjects.RemoveItem.Add((_,e)=>{
    if(e.Item.Value!==attribute)return;
    observations++;assert.equal(attribute.Handle,handle);assert.equal(doc.GetObjectByHandle(handle),null);
    attribute.Style=new TextStyle('RemovalStyle','txt.shx');
    attribute.Layer=new Layer('RemovalLayer');attribute.Linetype=new Linetype('RemovalLinetype');
  });
  assert.equal(doc.RemoveAttributeDefinitionFromDocument(attribute),true);assert.equal(observations,1);
  assert.equal(doc.TextStyles.get_Item('RemovalStyle'),null);
  assert.equal(attribute.Layer,doc.Layers.get_Item('RemovalLayer'));
  assert.equal(attribute.Linetype,doc.Linetypes.get_Item('RemovalLinetype'));
  assert.equal(hasAttributeReference(doc.Layers,'RemovalLayer',attribute),true);
  assert.equal(hasAttributeReference(doc.Linetypes,'RemovalLinetype',attribute),true);
  attribute.Layer=new Layer('AfterRemovalLayer');attribute.Linetype=new Linetype('AfterRemovalLinetype');
  assert.equal(doc.Layers.get_Item('AfterRemovalLayer'),null);assert.equal(doc.Linetypes.get_Item('AfterRemovalLinetype'),null);
});

test('ATTRDEF throwing removal observer preserves the native partial teardown state',()=>{
  const doc=new DxfDocument(),attribute=new AttributeDefinition('TAG'),failure=new Error('remove observer');
  doc.AddAttributeDefinitionToDocument(attribute);
  const handle=attribute.Handle;
  doc.AddedObjects.RemoveItem.Add((_,e)=>{if(e.Item.Value===attribute)throw failure;});
  assert.throws(()=>doc.RemoveAttributeDefinitionFromDocument(attribute),error=>error===failure);
  assert.equal(attribute.Handle,handle);assert.equal(doc.GetObjectByHandle(handle),null);
  attribute.Style=new TextStyle('AfterRemoveFailureStyle','txt.shx');
  attribute.Layer=new Layer('AfterRemoveFailureLayer');attribute.Linetype=new Linetype('AfterRemoveFailureLinetype');
  assert.equal(doc.TextStyles.get_Item('AfterRemoveFailureStyle'),null);
  assert.equal(attribute.Layer,doc.Layers.get_Item('AfterRemoveFailureLayer'));
  assert.equal(attribute.Linetype,doc.Linetypes.get_Item('AfterRemoveFailureLinetype'));
});

test('registered listener selective removal removes one last subscription and preserves other owners',()=>{
  const owner={},otherOwner={},item={Changed:new EventHook(),Other:new EventHook()},events=[];
  item.Changed.Add(()=>events.push('external'));
  Listen(owner,item,'Changed',()=>events.push('first'));
  Listen(otherOwner,item,'Changed',()=>events.push('other-owner'));
  Listen(owner,item,'Changed',()=>events.push('last'));
  Listen(owner,item,'Other',()=>events.push('other-event'));
  Unlisten(owner,item,'Changed');Unlisten(owner,item,'Missing');
  item.Changed.Invoke(item,{});item.Other.Invoke(item,{});
  assert.deepEqual(events,['external','first','other-owner','other-event']);
  events.length=0;Unlisten(owner,item);item.Changed.Invoke(item,{});item.Other.Invoke(item,{});
  assert.deepEqual(events,['external','other-owner']);
  events.length=0;Unlisten(otherOwner,item,'Changed');Unlisten(otherOwner,item,'Changed');item.Changed.Invoke(item,{});
  assert.deepEqual(events,['external']);
});

test('registered listener selective removal preserves an in-flight multicast snapshot',()=>{
  const owner={},item={Changed:new EventHook()},events=[];
  Listen(owner,item,'Changed',()=>{events.push('first');Unlisten(owner,item,'Changed');});
  Listen(owner,item,'Changed',()=>events.push('last'));
  item.Changed.Invoke(item,{});assert.deepEqual(events,['first','last']);
  events.length=0;item.Changed.Invoke(item,{});assert.deepEqual(events,['first']);
  events.length=0;item.Changed.Invoke(item,{});assert.deepEqual(events,[]);
});
