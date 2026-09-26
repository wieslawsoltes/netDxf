// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Vector3 } from '../Vector3.js';
import { Matrix4 } from '../Matrix4.js';
import { DxfTag } from '../IO/DxfTag.js';
import { DxfTagValueType } from '../IO/DxfGroupCode.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { GenericDictionary, ValueEquals } from '../../runtime/GenericDictionary.js';
import { TableSnapshot, DecodeTableText } from '../../runtime/TablePayload.js';
import { CanonicalDependencyHandle } from '../../runtime/StoredDependencyCollections.js';
import { RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { SetOpaqueEntityState, OpaqueEntityState, InstallOpaqueBlockGuard } from '../../runtime/OpaqueEntityState.js';
import { InstallOpaqueHatch } from './DxfOpaqueEntity.Hatch.js';
import { InvalidDataException, InvalidOperationException, NotSupportedException, NullReferenceException, ArgumentException } from '../../runtime/Errors.js';
export const SameOpaqueSequence=(a,b)=>a.length===b.length&&a.every((item,i)=>ValueEquals(item,b[i]));
const sameClass=(a,b)=>['Name','CppClassName','ApplicationName','ProxyFlags','InstanceCount','WasProxy','IsEntity'].every(key=>a[key]===b[key]);
/** Source-bound unknown entity. The constructor and bookkeeping fields adapt the
 * internal loader, not an admission parser or a generic private-schema authoring API.
 */
export class DxfOpaqueEntity extends EntityObject {
  CommonFields=new Set(); ReferenceIndices=new ReferenceList(); OwnerIndices=new ReferenceList();
  CommonEnd=0; XDataStart=0; SourceOwnerHandle=null;
  constructor(source,name,tags,handle){
    super(EntityType.OpaqueEntity,name);
    if(source==null)throw new NullReferenceException();
    const canonical=DxfOpaqueEntity.CanonicalHandle(handle),snapshot=TableSnapshot(tags);
    Object.defineProperties(this,{SourceVersion:{value:source.DrawingVariables.AcadVer,enumerable:true},
      SourceHandle:{value:canonical,enumerable:true},SourceTags:{value:snapshot,enumerable:true}});
    this.Handle=canonical;this.XDataStart=snapshot.Count;
    SetOpaqueEntityState(this,{source,tags:snapshot,links:new Map(),xdataLinks:new Map(),pending:true,retired:false,
      sourceOwner:null,sourceLayout:null,sourceLayoutName:null,sourceClass:null,classSnapshot:null,
      originalExtension:null,originalReactors:null,originalManagedReactors:null,
      initialCommon:null,initialXData:null,releasedHatchReactors:new Set(),permittedManagedReactors:null,permittedPersistentReactors:null});
  }
  get Normal(){return Vector3.UnitZ;}
  set Normal(value){if(!Vector3.Equals(value,Vector3.UnitZ))throw new NotSupportedException('Unknown entity geometry cannot be edited.');}
  get References(){
    const state=OpaqueEntityState(this),values=[];
    for(const [index,target] of state.links)if(!state.releasedHatchReactors.has(index))values.push(target);
    values.push(this.Layer,this.Linetype);if(this.ExtensionDictionary!==null)values.push(this.ExtensionDictionary);
    values.push(...this.PersistentReactors);
    for(const data of this.XData.Values){values.push(data.ApplicationRegistry);for(const tag of data.XDataRecord){const target=this.XDataTarget(tag);if(target!==null)values.push(target);}}
    const seen=new GenericDictionary(),result=[];
    for(const value of values)if(!seen.ContainsKey(value)){seen.Add(value,true);result.push(value);}
    return TableSnapshot(result);
  }
  static CanonicalHandle(value){return CanonicalDependencyHandle(value);}
  Resolve(resolve,definition){
    const state=OpaqueEntityState(this);
    if(resolve(this.SourceHandle)!==this)throw new InvalidDataException('Unknown entity requires its exact physical source identity.');
    if(this.Owner===null||resolve(this.SourceOwnerHandle)!==this.Owner.Record)throw new InvalidDataException('Unknown entity requires its actual source BLOCK_RECORD owner.');
    state.sourceOwner=this.Owner;state.sourceLayout=this.Owner.Record.Layout;
    for(const index of this.CommonFields){
      const tag=state.tags.get_Item(index);
      if(tag.Code===67&&tag.Value!==(state.sourceLayout!==null&&state.sourceLayout.IsPaperSpace?1:0))throw new InvalidDataException('Unknown entity space flag conflicts with its physical owner.');
      if(tag.Code===410){state.sourceLayoutName=state.sourceLayout?.Name??null;
        if(state.sourceLayoutName===null||!OrdinalIgnoreCaseEquals(DecodeTableText(tag.Value),state.sourceLayoutName))throw new InvalidDataException('Unknown entity layout name conflicts with its physical owner.');}
    }
    for(const index of this.ReferenceIndices){const handle=DxfOpaqueEntity.CanonicalHandle(state.tags.get_Item(index).Value);if(handle==='0')continue;
      const target=resolve(handle);if(target==null)throw new InvalidDataException('Unresolved unknown entity source reference: '+handle);
      if(state.links.has(index))throw new ArgumentException('An item with the same key has already been added.');state.links.set(index,target);
    }
    for(const index of this.OwnerIndices)if(state.links.has(index)){const target=state.links.get(index);if(target===this||target.Owner!==this)throw new InvalidDataException('Unknown entity owner link requires reciprocal source ownership.');}
    for(const data of this.XData.Values){
      if(resolve(data.ApplicationRegistry.Handle)!==data.ApplicationRegistry)throw new InvalidDataException('Unknown entity XData requires an actual source APPID.');
      for(const tag of data.XDataRecord){const target=this.XDataTarget(tag);
        if(tag.Code===api.XDataCode.DatabaseHandle&&DxfOpaqueEntity.CanonicalHandle(tag.Value)==='0')continue;
        if(tag.Code!==api.XDataCode.DatabaseHandle&&tag.Code!==api.XDataCode.LayerName)continue;
        if(target===null||resolve(target.Handle)!==target)throw new InvalidDataException('Unknown entity XData requires an actual source target.');
        if(state.xdataLinks.has(tag))throw new ArgumentException('An item with the same key has already been added.');state.xdataLinks.set(tag,target);
      }
    }
    state.sourceClass=definition;state.classSnapshot=definition==null?null:definition.Clone();
    state.originalExtension=this.ExtensionDictionary;state.originalReactors=Array.from(this.PersistentReactors);state.originalManagedReactors=Array.from(this.Reactors);
    state.initialCommon=this.CommonValues();state.initialXData=this.XDataValues();state.pending=false;this.Validate(state.source);
  }
  ValidateIncoming(document,owner=null){
    const state=OpaqueEntityState(this);
    if(state.retired)throw new InvalidOperationException('A removed unknown entity cannot be reattached.');
    if(document!==null&&document!==state.source||!state.pending&&document===null)throw new InvalidOperationException('An unknown entity belongs only to its source document.');
    if(document!==null&&document.DrawingVariables.AcadVer!==this.SourceVersion)throw new NotSupportedException('Unknown entity conversion between DXF profiles is unsupported.');
    if(owner!==null&&DxfOpaqueEntity.CanonicalHandle(owner.Record.Handle??'0')!==this.SourceOwnerHandle)throw new InvalidOperationException('An unknown entity cannot change its physical block owner.');
    if(!state.pending)this.Validate(document);
  }
  Validate(document){
    const s=OpaqueEntityState(this);
    if(s.pending||s.retired||document!==s.source||this.Owner!==s.sourceOwner||document.StoredTableHandleTarget(this.SourceHandle)!==this)
      throw new InvalidOperationException('Unknown entity source registration or owner changed.');
    if(document.DrawingVariables.AcadVer!==this.SourceVersion)throw new NotSupportedException('Unknown entity conversion between DXF profiles is unsupported.');
    if(s.sourceOwner.Record.Layout!==s.sourceLayout||s.sourceLayoutName!==null&&s.sourceLayout.Name!==s.sourceLayoutName)throw new NotSupportedException('Unknown entity layout changes require a common metadata mapping.');
    for(const [index,target] of s.links)if(!s.releasedHatchReactors.has(index)&&document.StoredTableHandleTarget(DxfOpaqueEntity.CanonicalHandle(s.tags.get_Item(index).Value))!==target)
      throw new InvalidOperationException('Unknown entity dependency identity changed.');
    for(const index of this.OwnerIndices)if(s.links.has(index)&&s.links.get(index).Owner!==this)throw new InvalidOperationException('Unknown entity dependency ownership changed.');
    if(this.ExtensionDictionary!==s.originalExtension||!SameOpaqueSequence(Array.from(this.PersistentReactors),s.permittedPersistentReactors??s.originalReactors)||
      !SameOpaqueSequence(Array.from(this.Reactors),s.permittedManagedReactors??s.originalManagedReactors))throw new NotSupportedException('Unknown entity extension and reactor edits require a complete metadata mapping.');
    if(s.sourceClass!==null&&(!document.Classes.Contains(s.sourceClass.Name)||document.Classes.get_Item(s.sourceClass.Name)!==s.sourceClass||!sameClass(s.sourceClass,s.classSnapshot)))
      throw new InvalidOperationException('Unknown entity CLASS declaration changed.');
    if(s.sourceClass===null&&document.Classes.Contains(this.CodeName))throw new InvalidOperationException('An unknown entity cannot acquire a different CLASS declaration.');
    if(document.GetObjectByHandle(this.Layer.Handle)!==this.Layer||document.GetObjectByHandle(this.Linetype.Handle)!==this.Linetype)throw new InvalidOperationException('Unknown entity common resources must remain registered.');
    if(!Number.isFinite(this.LinetypeScale))throw new InvalidOperationException('Unknown entity linetype scale must be finite.');
    if(!Object.values(api.Lineweight).includes(this.Lineweight))throw new InvalidOperationException('Unknown entity lineweight is invalid.');
    if(this.ColorName!==null&&this.SourceVersion<14||this.ShadowMode!==null&&this.SourceVersion<15)throw new NotSupportedException('Unknown entity common metadata is unavailable in its source profile.');
    this.XDataValues();
    for(const data of this.XData.Values)for(const tag of data.XDataRecord){
      if(tag.Code===api.XDataCode.DatabaseHandle&&DxfOpaqueEntity.CanonicalHandle(tag.Value)==='0')continue;
      if(tag.Code!==api.XDataCode.DatabaseHandle&&tag.Code!==api.XDataCode.LayerName)continue;
      const target=this.XDataTarget(tag);if(target===null||document.StoredTableHandleTarget(target.Handle)!==target)throw new InvalidOperationException('Unknown entity XData target is no longer registered.');
    }
  }
  XDataTarget(record){
    if(record==null)return null;const state=OpaqueEntityState(this);
    if(state.xdataLinks.has(record))return state.xdataLinks.get(record);
    if(record.Code===api.XDataCode.DatabaseHandle)return state.source.StoredTableHandleTarget(DxfOpaqueEntity.CanonicalHandle(record.Value));
    if(record.Code===api.XDataCode.LayerName){const layer={};if(state.source.Layers.TryGetValue(record.Value,layer))return layer.value;}
    return null;
  }
  ValidatePreparedClass(definitions){
    const snapshot=OpaqueEntityState(this).classSnapshot;
    if(snapshot!==null&&(!definitions.Contains(this.CodeName)||!sameClass(definitions.get_Item(this.CodeName),snapshot)))throw new NotSupportedException('Generated CLASS output would change an unknown entity declaration.');
    if(snapshot!==null&&this.SourceVersion===13&&snapshot.InstanceCount!==null)throw new NotSupportedException('DXF 2000 output would omit the unknown entity CLASS instance count.');
  }
  static RejectBlockGeometry(root){
    const seen=new GenericDictionary();
    const visit=block=>{if(block===null||seen.ContainsKey(block))return;seen.Add(block,true);
      for(const entity of block.Entities){if(entity instanceof DxfOpaqueEntity)throw new NotSupportedException('Unknown entity block geometry requires its complete application schema.');
        if(entity instanceof api.Insert||entity instanceof api.Dimension)visit(entity.Block);}
    };visit(root);
  }
  MarkRemoved(){OpaqueEntityState(this).retired=true;}
  Clone(){throw new NotSupportedException('Unknown entity cloning requires its complete application schema.');}
  TransformBy(transformation,translation){
    const size=transformation instanceof Matrix4?4:3;
    for(let row=0;row<size;row++)for(let column=0;column<size;column++)if(transformation.get_Item(row,column)!==(row===column?1:0))
      throw new NotSupportedException('Unknown entity transforms require its application schema.');
    if(size===3&&!Vector3.Equals(translation,Vector3.Zero))throw new NotSupportedException('Unknown entity transforms require its application schema.');
  }
  CommonValues(){
    const result=new Map(),add=(code,value)=>result.set(code,value===null?[]:[new DxfTag(code,value)]);
    add(8,this.Layer.Name);add(6,this.Linetype.Name);add(62,this.Color.Index);add(420,this.Color.UseTrueColor?api.AciColor.ToTrueColor(this.Color):null);
    add(370,this.Lineweight);add(48,this.LinetypeScale);add(60,this.IsVisible?0:1);add(440,this.Transparency.StoredAlphaValue??api.Transparency.ToAlphaValue(this.Transparency));
    add(430,this.ColorName);add(284,this.ShadowMode);
    const graphics=[],bytes=this.ProxyGraphics;
    if(bytes!==null){graphics.push(this.SourceVersion>=17?new DxfTag(160,BigInt(bytes.length)):new DxfTag(92,bytes.length));for(let offset=0;offset<bytes.length;offset+=127)graphics.push(new DxfTag(310,bytes.slice(offset,offset+127)));}
    result.set(92,graphics);return result;
  }
  XDataValues(){
    const result=[];
    for(const data of this.XData.Values){result.push(new DxfTag(1001,data.ApplicationRegistry.Name));let depth=0;
      for(const record of data.XDataRecord){
        if(record===null||!Object.values(api.XDataCode).includes(record.Code)||record.Code===api.XDataCode.AppReg)throw new InvalidOperationException('Invalid unknown entity XData record.');
        const target=record.Code===api.XDataCode.LayerName?this.XDataTarget(record):null,value=target instanceof api.Layer?target.Name:record.Value,tag=new DxfTag(record.Code,value);
        if(tag.Code===1002){if(tag.Value==='{')depth++;else if(tag.Value!=='}'||--depth<0)throw new InvalidOperationException('Unbalanced unknown entity XData.');}result.push(tag);
      }
      if(depth!==0)throw new InvalidOperationException('Unbalanced unknown entity XData.');
    }
    return result;
  }
  static SameTags(a,b){
    a=Array.from(a);b=Array.from(b);if(a.length!==b.length)return false;
    return a.every((tag,i)=>{if(tag.Code!==b[i].Code)return false;const x=tag.Value,y=b[i].Value;
      return x instanceof Uint8Array?y instanceof Uint8Array&&x.length===y.length&&x.every((v,j)=>v===y[j]):ValueEquals(x,y);});
  }
  OutputTags(encode){
    const state=OpaqueEntityState(this),common=this.CommonValues(),changed=new Set(Array.from(common.keys()).filter(code=>!DxfOpaqueEntity.SameTags(common.get(code),state.initialCommon.get(code))));
    if(changed.has(62)||changed.has(420)){changed.add(62);changed.add(420);}
    const emitted=new Set(),result=[],add=tag=>result.push(tag.ValueType===DxfTagValueType.String&&tag.Code!==1002?new DxfTag(tag.Code,encode(tag.Value)):tag);
    const emit=code=>{if(!emitted.has(code)){emitted.add(code);for(const tag of common.get(code))add(tag);}};
    for(let i=0;i<this.XDataStart;i++){
      if(state.releasedHatchReactors.has(i))continue;
      if(i===this.CommonEnd)for(const code of common.keys())if(changed.has(code))emit(code);
      const tag=state.tags.get_Item(i),key=tag.Code===160||tag.Code===310?92:tag.Code;
      if(this.CommonFields.has(i)&&changed.has(key))emit(key);else result.push(tag);
    }
    const data=this.XDataValues();
    if(DxfOpaqueEntity.SameTags(data,state.initialXData))result.push(...Array.from(state.tags).slice(this.XDataStart));
    else {for(const tag of data)add(tag);result.push(...Array.from(state.tags).slice(this.XDataStart).filter(tag=>tag.Code===999));}
    return new ReferenceList(result);
  }
}
InstallOpaqueHatch(DxfOpaqueEntity);
RegisterDatabaseModel('DxfOpaqueEntity',DxfOpaqueEntity);

InstallOpaqueBlockGuard(block=>DxfOpaqueEntity.RejectBlockGeometry(block));
