// Complete original in-memory cases. IO/independent-fixture, MULTILEADER and the
// arbitrary-handle case that ends with typed Save remain unported, not skipped.
import * as api from '../../index.js';
import { ArgumentException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, BooleanName } from './TestHarness.js';
const {DxfDocument,DxfDictionary,DxfDictionaryWithDefault,DxfXRecord,DxfDictionaryVariable,DxfPlaceholder,DxfDatabaseObject,
  DxfIdBuffer,DxfOpaqueObject,DxfTag,XData,XDataRecord,XDataCode,ApplicationRegistry,Line,Block,Insert,AttributeDefinition,Layout}=api;
const refIds=new WeakMap();let nextId=1;const Id=item=>{if(item==null)return 0;if(!refIds.has(item))refIds.set(item,nextId++);return refIds.get(item);};
const sum=items=>Array.from(items,r=>r.Uses).reduce((a,b)=>a+b,0);
export function RegisterTypedObjectErasureTests(){
  for(const flag of [false,true])for(const hard of [false,true])Run(`typed-erasure/ownership/${BooleanName(flag)}/${BooleanName(hard)}`,()=>ErasureOwnership(flag,hard));
  for(const kind of ['idbuffer','dictionary-default','dictionary-entry','reactor-document','reactor-object','reactor-line','reactor-layer','reactor-attribute','reactor-layout-viewport','xdata-document','xdata-object','xdata-line','xdata-layer','xdata-attribute','xdata-attdef','xdata-layout-viewport','layout-shade','standalone-shade','header','header-identity','xrecord330','xrecord339','xrecord340','xrecord349','xrecord350','xrecord359','xrecord360','xrecord369','xrecord390','xrecord399','xrecord480','xrecord481','lowercase-padded'])Run('typed-erasure/incoming/'+kind,()=>ErasureIncoming(kind));
  Run('typed-erasure/terminal-adoption',ErasureTerminal);
  Run('typed-erasure/appid-bookkeeping-and-handlers',ErasureAppIds);
  Run('typed-erasure/renamed-shared-appid-unregistration',ErasureRenamedAppId);
  Run('typed-erasure/unlinked-orphan-and-unrelated-invalid',ErasureOrphan);
  Run('typed-erasure/xrecord-owned-descendants',ErasureXRecordOwner);
  for(const host of ['line','block','record','dictionary'])Run('typed-erasure/extension-host/'+host,()=>ErasureHost(host));
  Run('typed-erasure/extension-plus-owning-aliases',ErasureExtensionAliases);
  Run('typed-erasure/nested-tombstone-rejection',ErasureNestedTombstone);
  Run('typed-erasure/same-name-table-object-identity',ErasureSameNameIdentity);
  Run('typed-erasure/opaque-owned-rejection',()=>ErasureOpaque(true,330));
  for(const code of [5,320,330,340,360,390,480,1005])Run('typed-erasure/opaque-exposed/'+code,()=>ErasureOpaque(false,code));
  Run('typed-erasure/opaque-unrelated-preservation',ErasureOpaqueUnrelated);
  Run('typed-erasure/callback-erases-clone-source',ErasureCloneCallback);
  Run('typed-erasure/deep-ownership-without-recursion',ErasureDeep);
}
export function ErasureGraph(doc){
  const root=new DxfDictionary(),record=new DxfXRecord();root.IsHardOwner=false;
  record.Data.Add(new DxfTag(1,String.raw`retained \U+0041`));record.Data.Add(new DxfTag(310,Uint8Array.of(0,0xfe,0xff)));
  root.Add('primary',record,false);root.Add('alias',record,true);doc.NamedObjects.Add('ERASE',root);doc.NamedObjects.Add('ERASE_ALIAS',root,false);
  const extension=new DxfDictionary(),leaf=new DxfDictionaryVariable();leaf.Value='leaf';extension.Add('child',leaf);doc.Objects.SetExtensionDictionary(record,extension);
  record.Data.Add(new DxfTag(330,root.Handle));record.Data.Add(new DxfTag(340,leaf.Handle));
  for(const item of [root,record,extension,leaf]){
    item.PersistentReactors.Add(item.Owner);const data=new XData(new ApplicationRegistry('ERASURE_APP'));
    data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,root.Handle));data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,3,5)));item.XData.Add(data);
  }
  return root;
}
export function ErasureState(doc){
  const Value=value=>value instanceof Uint8Array?Array.from(value,b=>b.toString(16).padStart(2,'0').toUpperCase()).join(''):value==null?'null':String(value);
  void doc.Objects;
  const carriers=[...doc.AddedObjects.Values,...doc.Objects.Items,...Array.from(doc.Entities.Inserts).flatMap(i=>Array.from(i.Attributes)),...Array.from(doc.Layouts).filter(l=>l.Viewport!==null).map(l=>l.Viewport)];
  const rows=[doc.DrawingVariables.HandleSeed,String(doc.Objects.Items.Count)];
  for(const item of new Set(carriers)){
    rows.push(`${Id(item)}|${item.Handle}|${Id(item.Owner)}|${Id(item.ExtensionDictionary)}|${Id(doc.GetObjectByHandle(item.Handle))}`);
    if(item instanceof DxfDatabaseObject)rows.push(`database:${Id(item.Database)}:${item.IsErased}`);
    for(const reactor of item.PersistentReactors)rows.push('reactor:'+Id(reactor));
    for(const data of item.XData.Values){rows.push(`app:${Id(data.ApplicationRegistry)}:${data.ApplicationRegistry.Name}:${data.ApplicationRegistry.Handle}:${Id(data.ApplicationRegistry.Owner)}`);for(const tag of data.XDataRecord)rows.push(`xdata:${tag.Code}:${Value(tag.Value)}`);}
    if(item instanceof DxfDictionary)for(const entry of item.Entries)rows.push(`entry:${entry.Name}:${Id(entry.Target)}:${entry.IsHardOwner}`);
    if(item instanceof DxfDictionaryWithDefault)rows.push('default:'+Id(item.Default));
    if(item instanceof DxfXRecord)for(const tag of item.Data)rows.push(`tag:${tag.Code}:${Value(tag.Value)}`);
    if(item instanceof DxfIdBuffer)for(const target of item.References)rows.push('buffer:'+Id(target));
    if(item instanceof Layout)rows.push('shade:'+Id(item.PlotSettings.ShadePlotObject));
  }
  for(const app of doc.ApplicationRegistries)rows.push(`registry:${app.Name}:${app.Handle}:${sum(doc.ApplicationRegistries.GetReferences(app))}`);
  for(const variable of doc.DrawingVariables.CustomValues())rows.push(`header:${variable.Name}:${variable.GroupCode}:${Value(variable.Value)}`);
  return rows.join('\n');
}
export function ErasureReject(doc,operation){
  const before=ErasureState(doc);let rejected=false;
  try{operation();}catch(error){if(error instanceof ArgumentException||error instanceof InvalidOperationException||error instanceof NotSupportedException)rejected=true;else throw error;}
  Check(rejected,'Unsafe erasure or resurrection was accepted.');Equal(before,ErasureState(doc),'Rejected operation changed exact graph snapshot');
}
export function ErasureOwnership(flag,hard){const doc=new DxfDocument(),root=new DxfDictionary(),child=new DxfXRecord();root.IsHardOwner=flag;root.Add('x',child,hard);root.Add('alias',child,!hard);doc.NamedObjects.Add('P',root);doc.Objects.EraseOwnedTree(root);Check(root.IsErased&&child.IsErased,'Ownership closure depends on pointer strength.');}
export function ErasureLeaf(){const doc=new DxfDocument(),target=new DxfXRecord();doc.NamedObjects.Add('TARGET',target);return [doc,target];}
export function ErasureIncoming(kind){
  const [doc,target]=ErasureLeaf();let clear;
  if(kind.startsWith('xrecord')||kind==='lowercase-padded'){
    const code=kind==='lowercase-padded'?340:Number(kind.slice(7)),source=new DxfXRecord();doc.NamedObjects.Add('SOURCE',source);
    const tag=new DxfTag(code,'000'+target.Handle.toLowerCase());if(code<=369)source.Data.Add(tag);else source.AddLoadedData(tag);clear=()=>source.Data.Clear();
  }else if(kind==='idbuffer'){const source=new DxfIdBuffer();source.References.Add(target);source.References.Add(target);doc.NamedObjects.Add('SOURCE',source);clear=()=>source.References.Clear();}
  else if(kind==='dictionary-default'){const source=new DxfDictionaryWithDefault();doc.NamedObjects.Add('SOURCE',source);source.Default=target;clear=()=>{source.Default=null;};}
  else if(kind==='dictionary-entry'){const source=new DxfDictionary();doc.NamedObjects.Add('SOURCE',source);source.AddLoaded('foreign alias',target,false);clear=()=>source.Remove('foreign alias');}
  else if(kind==='header'||kind==='header-identity'){doc.DrawingVariables.AddCustomVariable(new api.HeaderVariable('$ERASURE_REF',kind==='header'?347:5,'000'+target.Handle.toLowerCase()));clear=()=>doc.DrawingVariables.RemoveCustomVariable('$ERASURE_REF');}
  else if(kind==='layout-shade'){const settings=Array.from(doc.Layouts)[0].PlotSettings;settings.ShadePlotObject=target;clear=()=>{settings.ShadePlotObject=null;};}
  else if(kind==='standalone-shade'){const plot=new api.PlotSettings();plot.ShadePlotObject=target;const source=doc.Objects.AddPlotSettings('PAGE',plot);clear=()=>{source.Settings.ShadePlotObject=null;};}
  else{
    let source;
    if(kind.endsWith('layout-viewport')){source=doc.Layouts.Add(new Layout('CARRIER')).Viewport;Check(doc.GetObjectByHandle(source.Handle)===null,'Layout viewport test is not exercising a nonregistry carrier.');}
    else if(kind.endsWith('attribute')||kind.endsWith('attdef')){const block=new Block('ATTR_BLOCK'),definition=new AttributeDefinition('TAG');definition.Value='sentinel';block.AttributeDefinitions.Add(definition);const insert=new Insert(block);doc.Entities.Add(insert);source=kind.endsWith('attdef')?definition:Array.from(insert.Attributes)[0];if(source instanceof api.Attribute)Check(doc.GetObjectByHandle(source.Handle)===null,'ATTRIB test is not exercising a nonregistry carrier.');}
    else if(kind.endsWith('line')){source=new Line();doc.Entities.Add(source);}
    else if(kind.endsWith('layer'))source=doc.Layers.get_Item('0');
    else if(kind.endsWith('document'))source=doc;
    else{source=new DxfXRecord();doc.NamedObjects.Add('SOURCE',source);}
    if(kind.startsWith('reactor')){source.PersistentReactors.Add(target);clear=()=>source.PersistentReactors.Clear();}
    else{const data=new XData(new ApplicationRegistry('INCOMING'));data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,'00'+target.Handle.toLowerCase()));source.XData.Add(data);clear=()=>source.XData.Remove('INCOMING');}
  }
  ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(target));clear();doc.Objects.EraseOwnedTree(target);Check(target.IsErased,'Corrected dependency did not permit retry.');
}
export function ErasureTerminal(){
  const doc=new DxfDocument(),root=ErasureGraph(doc),child=root.get_Item('primary');doc.Objects.EraseOwnedTree(root);const line=new Line();doc.Entities.Add(line);const other=new DxfDocument();void other.Objects;
  for(const item of [root,child]){ErasureReject(doc,()=>doc.NamedObjects.Add('RESURRECT',item));ErasureReject(other,()=>other.NamedObjects.Add('RESURRECT',item));ErasureReject(doc,()=>new DxfDictionary().Add('RESURRECT',item));ErasureReject(doc,()=>doc.Objects.CloneObject(item,doc.NamedObjects,'RESURRECT'));}
  ErasureReject(doc,()=>doc.Objects.SetExtensionDictionary(line,root));ErasureReject(doc,()=>doc.Objects.Clone(root,doc.NamedObjects,'RESURRECT'));
  const fresh=new DxfXRecord();ErasureReject(doc,()=>root.Add('RESURRECT',fresh));Check(fresh.Owner===null&&fresh.Database===null&&fresh.Handle===null,'Dead dictionary adopted a fresh object.');
  const buffer=new DxfIdBuffer();doc.NamedObjects.Add('BUFFER',buffer);ErasureReject(doc,()=>buffer.References.Add(child));
  ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(doc.NamedObjects));ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(new DxfXRecord()));ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(null));
  const foreign=new DxfXRecord();other.NamedObjects.Add('FOREIGN',foreign);const state=ErasureState(other);ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(foreign));Equal(state,ErasureState(other),'Foreign source changed');
}
export function ErasureAppIds(){
  const doc=new DxfDocument(),root=ErasureGraph(doc),registry=doc.ApplicationRegistries.get_Item('ERASURE_APP');Equal(4,sum(doc.ApplicationRegistries.GetReferences(registry)),'APPID initial uses');
  let userEvents=0;root.XDataAddAppReg.Add(()=>userEvents++);doc.Objects.EraseOwnedTree(root);Equal(0,sum(doc.ApplicationRegistries.GetReferences(registry)),'APPID erase uses');Check(doc.ApplicationRegistries.Remove(registry),'Erased APPID use retained.');
  root.XData.Remove('ERASURE_APP');root.XData.Add(new XData(new ApplicationRegistry('AFTER_ERASE')));Equal(1,userEvents,'External object event subscription changed');Check(!doc.ApplicationRegistries.Contains('AFTER_ERASE'),'Erased XData still registers application IDs.');
  const retained=new DxfXRecord();doc.NamedObjects.Add('KEEP',retained);retained.XData.Add(new XData(new ApplicationRegistry('SHARED')));const dead=new DxfXRecord();doc.NamedObjects.Add('DEAD',dead);dead.XData.Add(new XData(new ApplicationRegistry('SHARED')));doc.Objects.EraseOwnedTree(dead);
  Equal(1,sum(doc.ApplicationRegistries.GetReferences('SHARED')),'Shared APPID count');Check(!doc.ApplicationRegistries.Remove('SHARED'),'Live APPID use removed.');
}
export function ErasureOrphan(){const [doc,target]=ErasureLeaf();doc.NamedObjects.Remove('TARGET');const unrelated=new DxfXRecord();doc.NamedObjects.Add('INVALID',unrelated);unrelated.Data.Add(new DxfTag(340,'FFFF'));Check(doc.Objects.Validate().Count>0,'Orphan test must start invalid.');doc.Objects.EraseOwnedTree(target);Check(target.IsErased,'Registered orphan not erased.');Equal('FFFF',unrelated.Data.get_Item(0).Value,'Unrelated invalid payload changed');}
export function ErasureRenamedAppId(){
  const [doc,first]=ErasureLeaf(),second=new DxfXRecord();doc.NamedObjects.Add('SECOND',second);first.XData.Add(new XData(new ApplicationRegistry('SHARED')));second.XData.Add(new XData(new ApplicationRegistry('SHARED')));
  const registry=doc.ApplicationRegistries.get_Item('SHARED');registry.Name='RENAMED';Equal(2,sum(doc.ApplicationRegistries.GetReferences(registry)),'Renamed shared initial uses');
  doc.Objects.EraseOwnedTree(first);Equal(1,sum(doc.ApplicationRegistries.GetReferences(registry)),'Renamed APPID first erase count');Check(!doc.ApplicationRegistries.Remove(registry),'Live renamed APPID use was lost.');doc.Objects.EraseOwnedTree(second);
  Equal(0,sum(doc.ApplicationRegistries.GetReferences(registry)),'Renamed APPID final erase count');Check(doc.ApplicationRegistries.Remove(registry),'Renamed APPID could not be removed.');
  for(const dead of [first,second]){dead.XData.Clear();dead.XData.Add(new XData(new ApplicationRegistry('AFTER_RENAME_ERASE')));}Check(!doc.ApplicationRegistries.Contains('AFTER_RENAME_ERASE'),'Renamed tombstone retained registration handlers.');
}
export function ErasureXRecordOwner(){const [doc,root]=ErasureLeaf(),child=new DxfXRecord();doc.NamedObjects.Add('TEMP',child);doc.NamedObjects.Remove('TEMP');child.Owner=root;root.Data.Add(new DxfTag(360,child.Handle));const grandchild=new DxfDictionary();doc.Objects.SetExtensionDictionary(child,grandchild);grandchild.Add('VAR',new DxfDictionaryVariable());doc.Objects.EraseOwnedTree(root);Check(root.IsErased&&child.IsErased&&grandchild.IsErased&&grandchild.get_Item('VAR').IsErased,'Non-dictionary owner descendants survived.');Check(child.Owner===root,'Internal XRECORD ownership changed.');}
export function ErasureHost(kind){
  const doc=new DxfDocument();let host;if(kind==='block')host=doc.Blocks.Add(new Block('HOST')).Record;else if(kind==='line'){host=new Line();doc.Entities.Add(host);}else{host=kind==='record'?new DxfXRecord():new DxfDictionary();doc.NamedObjects.Add('HOST',host);}
  const extension=new DxfDictionary();extension.Add('CHILD',new DxfXRecord());doc.Objects.SetExtensionDictionary(host,extension);const incoming=new DxfXRecord();doc.NamedObjects.Add('INCOMING',incoming);incoming.Data.Add(new DxfTag(340,extension.get_Item('CHILD').Handle));
  ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(extension));incoming.Data.Clear();doc.Objects.EraseOwnedTree(extension);Check(host.ExtensionDictionary===null&&doc.GetObjectByHandle(host.Handle)===host,'Live host was erased.');doc.Objects.SetExtensionDictionary(host,new DxfDictionary());
}
export function ErasureExtensionAliases(){const doc=new DxfDocument(),parent=new DxfDictionaryWithDefault();doc.NamedObjects.Add('PARENT',parent);const extension=new DxfDictionary();extension.Add('CHILD',new DxfXRecord());parent.Add('PRIMARY',extension,false);parent.Add('ALIAS',extension,true);doc.Objects.SetExtensionDictionary(parent,extension);parent.Default=extension;ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(extension));parent.Default=null;doc.Objects.EraseOwnedTree(extension);Check(!parent.IsErased&&parent.Count===0&&parent.ExtensionDictionary===null,'Combined aliases and extension not removed.');}
export function ErasureNestedTombstone(){const [doc,dead]=ErasureLeaf();doc.Objects.EraseOwnedTree(dead);const host=new Line();doc.Entities.Add(host);for(const attach of [false,true])for(const fallback of [false,true]){const graph=new DxfDictionary();if(fallback){const nested=new DxfDictionaryWithDefault();nested.Default=dead;graph.Add('NESTED',nested);}else{const nested=new DxfIdBuffer();nested.References.Add(dead);graph.Add('NESTED',nested);}ErasureReject(doc,()=>{if(attach)doc.Objects.SetExtensionDictionary(host,graph);else doc.NamedObjects.Add('NESTED',graph);});Check(graph.Database===null&&graph.Handle===null&&graph.Owner===null&&host.ExtensionDictionary===null,'Nested rejection adopted graph.');Check(graph.get_Item('NESTED').Handle===null,'Nested rejection allocated child.');}}
export function ErasureSameNameIdentity(){const [doc,target]=ErasureLeaf(),real=doc.TextStyles.get_Item('Standard'),foreign=new DxfDocument(),decoy=foreign.TextStyles.get_Item('Standard');Check(real.Equals(decoy)&&real!==decoy,'Same-name identity precondition.');real.PersistentReactors.Add(target);ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(target));real.PersistentReactors.Clear();decoy.PersistentReactors.Add(target);doc.Objects.EraseOwnedTree(target);Check(target.IsErased&&decoy.PersistentReactors.get_Item(0)===target,'Foreign same-name carrier affected local erasure.');}
export function ErasureNewOpaque(code,handle){return new DxfOpaqueObject('QA_PRIVATE',[new DxfTag(100,'AcDbQaPrivate'),new DxfTag(code,handle)]);}
export function ErasureOpaque(owned,code){const [doc,target]=ErasureLeaf(),opaque=ErasureNewOpaque(code,target.Handle);doc.NamedObjects.Add('OPAQUE',opaque);ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(owned?opaque:target));if(owned){doc.NamedObjects.Remove('OPAQUE');opaque.Owner=target;ErasureReject(doc,()=>doc.Objects.EraseOwnedTree(target));}}
export function ErasureOpaqueUnrelated(){const [doc,target]=ErasureLeaf(),opaque=ErasureNewOpaque(340,doc.Layers.get_Item('0').Handle);doc.NamedObjects.Add('OPAQUE',opaque);const stored=opaque.Tags.get_Item(1).Value;doc.Objects.EraseOwnedTree(target);Check(!opaque.IsErased&&opaque.Database===doc.Objects,'Unrelated opaque object erased.');Equal(stored,opaque.Tags.get_Item(1).Value,'Opaque payload changed');}
export function ErasureCloneCallback(){const source=new DxfDocument(),root=ErasureGraph(source),target=new DxfDocument();void target.Objects;const callback={*[Symbol.iterator](){source.Objects.EraseOwnedTree(root);}};ErasureReject(target,()=>target.Objects.Clone(root,target.NamedObjects,'COPY',callback));Check(root.IsErased,'Mapping callback did not execute.');}
export function ErasureDeep(){const doc=new DxfDocument(),root=new DxfDictionary();doc.NamedObjects.Add('DEEP',root);let current=root;for(let i=0;i<2048;i++){const child=new DxfDictionary();current.Add('CHILD',child);current=child;}doc.Objects.EraseOwnedTree(root);Check(root.IsErased&&current.IsErased,'Deep ownership closure incomplete.');Equal(1,doc.Objects.Items.Count,'Deep registration residue');}
