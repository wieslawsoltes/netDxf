import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, Line, Layer, Block, Insert, Layout, AlignedDimension, LayerState,
  LayerStateProperties, LayerPropertiesRestoreFlags, DxfDictionary, DxfPlaceholder, XData, ApplicationRegistry } from '../../index.js';
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
