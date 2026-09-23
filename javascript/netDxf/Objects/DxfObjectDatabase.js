import { InstallDatabaseSectionManager } from './DxfObjectDatabase.SectionManager.js';
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary, DxfDictionaryWithDefault, DxfXRecord } from './DxfDatabaseObject.js';
import { DxfTag } from '../IO/DxfTag.js';
import { DxfHandleKind, DxfTagValueType } from '../IO/DxfGroupCode.js';
import { XDataRecord } from '../XDataRecord.js';
import { XDataCode } from '../XDataCode.js';
import { EntityObject } from '../Entities/EntityObject.js';
import { Polyline3D } from '../Entities/Polyline3D.js';
import { SunReferences } from './SunReferences.js';
import { ValidateDeclaredOwnership } from './DxfDeclaredOwnership.js';
import { GenericDictionary } from '../../runtime/GenericDictionary.js';
import { TableNameComparer } from '../Collections/TableObjects.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { IsAncestor, IsReservedDictionaryName, ReadOnlyReferenceView } from '../../runtime/DatabaseModel.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, FormatException } from '../../runtime/Errors.js';
import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { ReferenceMap } from '../../runtime/DatabaseReferenceMap.js';
import { SetDatabaseRegistry } from '../../runtime/RegisteredDatabaseState.js';
import { InstallDatabaseContainers } from './DxfObjectDatabase.Containers.js';
import { InstallDatabaseOutputSettings } from './DxfObjectDatabase.OutputSettings.js';
import { InstallDatabaseGeoData } from './DxfObjectDatabase.GeoData.js';
import { InstallDatabaseSun } from './DxfObjectDatabase.Sun.js';
import { InstallDatabaseErasure } from './DxfObjectDatabase.Erase.js';
import { InstallDatabaseMLeaderStyle } from './DxfMLeaderStyle.js';
import { InstallDatabaseSection } from './DxfObjectDatabase.Section.js';
const maximum=9223372036854775807n;
const parse=handle=>typeof handle==='string'&&/^[0-9a-f]{1,16}$/i.test(handle)?BigInt('0x'+handle):null;
const nullHandle=handle=>parse(handle)===0n;
export class DxfObjectDatabase {
  #objects=new GenericDictionary(0,TableNameComparer,'string'); #document; #root;
  constructor(document){this.#document=document;SetDatabaseRegistry(this,this.#objects);this.#root=new DxfDictionary();this.#root.Owner=document;this.Register(this.#root,false);}
  get Document(){return this.#document;}
  get Root(){return this.#root;}
  get Items(){return ReadOnlyReferenceView(new ReferenceList(this.#objects.Values));}
  static IsReference(tag){return [DxfHandleKind.SoftPointer,DxfHandleKind.HardPointer,DxfHandleKind.SoftOwner,DxfHandleKind.HardOwner].includes(tag.HandleKind);}
  static IsAncestor(ancestor,item){return IsAncestor(ancestor,item);}
  static IsReservedName(name){return IsReservedDictionaryName(name);}
  IsRegistered(item){return item!=null&&item.Handle!==null&&this.Document.GetObjectByHandle(item.Handle)===item;}
  CheckRegistered(item){if(!this.IsRegistered(item))throw new ArgumentException('The referenced object must be registered in this document.','item');}
  SetExtensionDictionary(owner,dictionary){
    if(owner==null)throw new ArgumentNullException('owner');if(dictionary==null)throw new ArgumentNullException('dictionary');this.CheckRegistered(owner);
    if(owner===this.Document.Layers)throw new InvalidOperationException('The LAYER table extension dictionary is managed by the layer-state collection.');
    if(owner.ExtensionDictionary!==null&&owner.ExtensionDictionary!==dictionary)throw new InvalidOperationException('An extension dictionary is already attached.');
    if(dictionary.Owner!==null&&dictionary.Owner!==owner)throw new ArgumentException('The dictionary already has an owner.','dictionary');
    if(IsAncestor(dictionary,owner))throw new ArgumentException('Extension dictionary ownership cannot form a cycle.','dictionary');
    this.PrepareTarget(dictionary);dictionary.Owner=owner;owner.ExtensionDictionary=dictionary;
  }
  Validate(){
    const errors=new ReferenceList();
    for(const item of this.#objects.Values){
      item.ValidateDatabaseSchema(this,errors);ValidateDeclaredOwnership(this,item,this.#objects.Values,errors,true);
      for(const reference of item.DatabaseReferences)if(reference!==null&&!this.IsRegistered(reference))errors.Add('Unregistered '+item.CodeName+' reference: '+item.Handle);
      if(item.Database!==this||this.Document.GetObjectByHandle(item.Handle)!==item)errors.Add('Object registration mismatch: '+item.Handle);
      if(item!==this.Root&&item.Owner===null)errors.Add('Object has no owner: '+item.Handle);
      if(item.Owner!==null&&!this.IsRegistered(item.Owner))errors.Add('Owner is outside the document: '+item.Handle);
      if(item.Owner instanceof DxfDictionary&&!Array.from(item.Owner.Entries).some(e=>e.Target===item)&&item.Owner.ExtensionDictionary!==item)errors.Add('Owned object has no owning dictionary entry: '+item.Handle);
      const ancestors=new Set();for(let owner=item;owner!==null;owner=owner.Owner){if(ancestors.has(owner)){errors.Add('Ownership cycle: '+item.Handle);break;}ancestors.add(owner);}
      if(item instanceof DxfDictionary)for(const entry of item.Entries){
        if(!this.IsRegistered(entry.Target))errors.Add('Unregistered dictionary target: '+entry.Name);
        if(entry.Target instanceof EntityObject)errors.Add('Graphical entity used as dictionary entry: '+entry.Name);
        if(entry.Target.Owner!==item)errors.Add('Dictionary ownership mismatch: '+entry.Name);
      }
      if(item instanceof DxfDictionaryWithDefault&&item.Default!==null&&!this.IsRegistered(item.Default))errors.Add('Unregistered dictionary default: '+item.Handle);
      if(item instanceof DxfXRecord)for(const tag of item.Data)if(DxfObjectDatabase.IsReference(tag)&&!nullHandle(tag.Value)&&this.Document.GetObjectByHandle(tag.Value)===null)errors.Add('Unresolved XRECORD reference '+tag.Code+': '+tag.Value);
    }
    for(const item of this.Document.AddedObjects.Values){
      SunReferences.Validate(item,this,errors);
      if(item.ExtensionDictionary!==null&&(!this.IsRegistered(item.ExtensionDictionary)||item.ExtensionDictionary.Owner!==item))errors.Add('Invalid extension dictionary: '+item.Handle);
      for(const reactor of item.PersistentReactors)if(reactor===null||!this.IsRegistered(reactor))errors.Add('Unregistered persistent reactor: '+item.Handle);
      if(item instanceof DxfDatabaseObject)for(const data of item.XData.Values)for(const tag of data.XDataRecord)
        if(tag.Code===XDataCode.DatabaseHandle&&!nullHandle(tag.Value)&&this.Document.GetObjectByHandle(tag.Value)===null)errors.Add('Unresolved XData reference: '+tag.Value);
    }
    return ReadOnlyReferenceView(errors);
  }
  PrepareTarget(target){
    if(!(target instanceof DxfDatabaseObject)){this.CheckRegistered(target);return;}
    const found=new Set(),pending=[target];
    while(pending.length){
      const item=pending.pop();if(item.IsErased)throw new InvalidOperationException('An erased object cannot be registered again.');
      if(found.has(item))continue;found.add(item);
      if(item.Database!==null&&item.Database!==this)throw new ArgumentException('Cannot link objects from different documents.','target');
      if(item instanceof DxfDictionary)for(const entry of item.Entries){if(entry.Target instanceof DxfDatabaseObject)pending.push(entry.Target);else this.CheckRegistered(entry.Target);}
      for(const child of item.DeclaredOwnedObjects){if(child===null)throw new ArgumentException('A declared ownership slot cannot be null.','target');pending.push(child);}
      if(item.ExtensionDictionary!==null)pending.push(item.ExtensionDictionary);
    }
    for(const item of found){
      if(item.Owner instanceof DxfDatabaseObject&&item.Owner.Database===null&&!found.has(item.Owner))throw new ArgumentException('The detached target is owned outside the adopted graph.','target');
      if(item instanceof DxfDictionaryWithDefault&&item.Default!==null&&!this.IsRegistered(item.Default)&&!found.has(item.Default))throw new ArgumentException('The default is outside the adopted graph.','target');
      for(const reference of item.DatabaseReferences)if(reference!==null&&!this.IsRegistered(reference)&&!found.has(reference))throw new ArgumentException('An object reference is outside the adopted graph.','target');
      for(const reactor of item.PersistentReactors)if(!this.IsRegistered(reactor)&&!found.has(reactor))throw new ArgumentException('A reactor is outside the adopted graph.','target');
    }
    const errors=new ReferenceList();for(const item of found)ValidateDeclaredOwnership(this,item,found,errors,false);
    if(errors.Count)throw new ArgumentException('Invalid declared ownership: '+Array.from(errors).join('; '),'target');
    this.#plan(Array.from(found).filter(item=>item.Database===null));
    for(const item of found)if(item.Database===null)this.Register(item,false);
    for(const item of found)item.MaterializeOwnedObjectReferences();
  }
  #seed(tag,current){
    if(tag.ValueType!==DxfTagValueType.Handle||tag.HandleKind===DxfHandleKind.ObjectIdentity||nullHandle(tag.Value)||this.Document.GetObjectByHandle(tag.Value)!==null)return current;
    const handle=parse(tag.Value);if(handle===null||handle>maximum)return current;
    if(handle>=maximum-1n)throw new ArgumentException('The typed database cannot allocate beyond the exposed reference handle.','tag');
    return handle>=current?handle+1n:current;
  }
  #plan(incoming){
    let candidate=this.Document.NumHandles;const registrations=new Set();
    for(const item of incoming){
      if(item instanceof DxfXRecord)for(const tag of item.Data)candidate=this.#seed(tag,candidate);
      for(const tag of item.AllocationReservations)candidate=this.#seed(tag,candidate);
      for(const data of item.XData.Values){
        if(!this.Document.ApplicationRegistries.Contains(data.ApplicationRegistry.Name))registrations.add(OrdinalIgnoreCaseKey(data.ApplicationRegistry.Name));
        for(const tag of data.XDataRecord)if(tag.Code===XDataCode.DatabaseHandle)candidate=this.#seed(new DxfTag(1005,tag.Value),candidate);
      }
    }
    if(candidate<=0n||candidate>maximum-BigInt(incoming.length+registrations.size))throw new InvalidOperationException('The document handle range is exhausted.');
    this.Document.NumHandles=candidate;
  }
  GetReservedSeed(tag,current){return this.#seed(tag,current);}
  ReserveUnresolvedReference(tag){this.Document.NumHandles=this.#seed(tag,this.Document.NumHandles);}
  Register(item,preserveHandle){
    if(item.IsErased)throw new InvalidOperationException('An erased object cannot be registered again.');
    if(preserveHandle){
      if(!item.Handle||nullHandle(item.Handle)||this.Document.GetObjectByHandle(item.Handle)!==null)throw new FormatException('Duplicate or invalid database object handle: '+item.Handle);
      const handle=parse(item.Handle);if(handle===null||handle>=maximum)throw new FormatException('Unsupported object handle: '+item.Handle);
      if(handle>=this.Document.NumHandles)this.Document.NumHandles=handle+1n;
    }else{
      if(this.Document.NumHandles<=0n||this.Document.NumHandles===maximum)throw new InvalidOperationException('The document handle range is exhausted.');
      while(this.Document.GetObjectByHandle(this.Document.NumHandles.toString(16).toUpperCase())!==null)this.Document.NumHandles++;
      this.Document.NumHandles=item.AssignHandle(this.Document.NumHandles);
    }
    for(const data of Array.from(item.XData.Values))item.XData.ReplaceForBinding(data.ApplicationRegistry.Name,data.CopyStoredGraph());
    item.Database=this;this.#objects.Add(item.Handle,item);this.Document.AddedObjects.Add(item.Handle,item);
  }
  ReplaceRoot(root){this.#objects.Remove(this.Root.Handle);this.Document.AddedObjects.Remove(this.Root.Handle);this.Root.Database=null;this.#root=root;root.Owner=this.Document;this.Register(root,true);}
  Clone(source,destination,name,externalReferences=null){
    if(source==null)throw new ArgumentNullException('source');if(destination==null)throw new ArgumentNullException('destination');
    if(source.Database===null)throw new ArgumentException('The source must be registered.','source');
    if(destination.Database!==this)throw new ArgumentException('The destination belongs to another database.','destination');
    DxfDictionary.ValidateName(name);
    if(destination.Contains(name)||destination===this.Root&&IsReservedDictionaryName(name))throw new ArgumentException('The destination name already exists or is reserved.','name');
    return this.CloneOwnershipGraph(source,destination,name,false,externalReferences);
  }
  CloneOwnershipGraph(source,destination,name,extension,externalReferences=null,sun=false){
    Polyline3D.RejectStoredRecordOwnershipClone(source);
    const external=ReferenceMap(externalReferences);
    Polyline3D.RejectStoredRecordOwnershipClone(source);this.CheckRegistered(destination);
    if(sun)this.CheckSunDestination(destination);
    else if(extension){if(destination===this.Document.Layers||destination.ExtensionDictionary!==null)throw new InvalidOperationException('The destination extension-dictionary slot is occupied or reserved.');}
    else if(destination.Database!==this||destination.Contains(name)||destination===this.Root&&IsReservedDictionaryName(name))throw new ArgumentException('The destination name already exists or is reserved.','name');
    if(source.IsErased||source.Database===null)throw new InvalidOperationException('The clone source must remain registered and cannot be erased.');source.Database.CheckRegistered(source);
    const errors=source.Database.Validate();if(errors.Count)throw new InvalidOperationException('Cannot clone an invalid source graph: '+Array.from(errors).join('; '));
    const originals=Array.from(source.Database.#objects.Values).filter(item=>item===source||IsAncestor(source,item)),map=new Map(originals.map(item=>[item,item.CloneShell()]));
    const resolve=value=>{
      if(value===null)return null;if(map.has(value))return map.get(value);if(external.has(value)){this.CheckRegistered(external.get(value));return external.get(value);}
      if(source.Database===this)return value;throw new InvalidOperationException('A reference outside the cloned graph needs an explicit destination mapping: '+value.Handle);
    };
    const resolveHandle=(handle,kind)=>{const target=source.Database.Document.GetObjectByHandle(handle);if(target===null)throw new InvalidOperationException('Cannot clone an unresolved '+kind+' reference: '+handle);return resolve(target);};
    for(const original of originals){
      const clone=map.get(original);clone.Owner=original===source?destination:resolve(original.Owner);
      if(original instanceof DxfDictionary)for(const entry of original.Entries)clone.AddLoaded(entry.Name,resolve(entry.Target),entry.IsHardOwner);
      if(original instanceof DxfDictionaryWithDefault)clone.Default=resolve(original.Default);
      clone.ExtensionDictionary=resolve(original.ExtensionDictionary);for(const reactor of original.PersistentReactors)clone.PersistentReactors.Add(resolve(reactor));
      original.CopyDatabaseReferencesTo(clone,resolve);
      for(const data of original.XData.Values){clone.XData.Add(data.CopyStoredGraph());for(const tag of data.XDataRecord)if(tag.Code===XDataCode.DatabaseHandle&&!nullHandle(tag.Value))resolveHandle(tag.Value,'XData');}
      if(original instanceof DxfXRecord)for(const tag of original.Data)if(DxfObjectDatabase.IsReference(tag)&&!nullHandle(tag.Value))resolveHandle(tag.Value,'XRECORD');
    }
    const cloneErrors=new ReferenceList();for(const original of originals)map.get(original).ValidateDatabaseSchema(this,cloneErrors);
    if(cloneErrors.Count)throw new InvalidOperationException('Invalid cloned object schema: '+Array.from(cloneErrors).join('; '));
    this.#plan(originals.map(item=>map.get(item)));for(const original of originals)this.Register(map.get(original),false);
    for(const original of originals)map.get(original).MaterializeOwnedObjectReferences();
    for(const original of originals){const clone=map.get(original);
      if(original instanceof DxfXRecord)for(let i=0;i<original.Data.Count;i++){const tag=original.Data.get_Item(i);if(DxfObjectDatabase.IsReference(tag)&&!nullHandle(tag.Value))clone.ReplaceLoadedData(i,new DxfTag(tag.Code,resolveHandle(tag.Value,'XRECORD').Handle));}
      for(const data of original.XData.Values)for(let i=0;i<data.XDataRecord.Count;i++){const tag=data.XDataRecord.get_Item(i);if(tag.Code===XDataCode.DatabaseHandle&&!nullHandle(tag.Value))clone.XData.get_Item(data.ApplicationRegistry.Name).XDataRecord.set_Item(i,new XDataRecord(XDataCode.DatabaseHandle,resolveHandle(tag.Value,'XData').Handle));}
    }
    const result=map.get(source);if(sun)SunReferences.Set(destination,result);else if(extension)destination.ExtensionDictionary=result;else destination.AddLoaded(name,result,true);return result;
  }
}

InstallDatabaseContainers(DxfObjectDatabase);
InstallDatabaseOutputSettings(DxfObjectDatabase);
InstallDatabaseGeoData(DxfObjectDatabase);
InstallDatabaseSun(DxfObjectDatabase);
InstallDatabaseErasure(DxfObjectDatabase);
InstallDatabaseMLeaderStyle(DxfObjectDatabase);
InstallDatabaseSection(DxfObjectDatabase);

InstallDatabaseSectionManager(DxfObjectDatabase);
