// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfObjectReference } from './DxfObjectReference.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
import { Listen, Unlisten } from '../runtime/RegisteredTable.js';
export function ObjectMetadataMembers(owner) {
  if(owner==null)return [];
  const result=[owner];
  if(owner.CodeName==='INSERT')result.push(...owner.Attributes);
  if(owner.CodeName==='BLOCK')result.push(owner.End);
  if(owner.StoredRecords)result.push(...owner.StoredRecords);
  if(owner.CodeName==='LAYOUT'&&owner.Viewport!==null)result.push(owner.Viewport);
  return result;
}
export function InstallDocumentMetadata(Type) {
  const states=new WeakMap(),state=doc=>{let value=states.get(doc);if(!value)states.set(doc,value=new Set());return value;};
  Type.prototype.CanonicalXDataRegistry=function(source){return this.ApplicationRegistries.get_Item(source.Name)??this.ApplicationRegistries.Add(source.CloneStoredGraph());};
  Type.prototype.BindMetadataObject=function(item){
    const bound=state(this);if(item==null||bound.has(item))return;bound.add(item);
    for(const data of Array.from(item.XData.Values)){
      const registry=this.CanonicalXDataRegistry(data.ApplicationRegistry);
      item.XData.CanonicalizeApplicationRegistry(data.ApplicationRegistry.Name,registry);this.ApplicationRegistries.References.get_Item(registry.Name).Add(item);
    }
    // Separate subscription owner prevents removing entity/style listeners when metadata unbinds.
    Listen(bound,item,'XDataAddAppReg',(sender,e)=>{
      const data=sender.XData.get_Item(e.Item.Name),registry=this.CanonicalXDataRegistry(data.ApplicationRegistry);
      sender.XData.CanonicalizeApplicationRegistry(e.Item.Name,registry);this.ApplicationRegistries.References.get_Item(registry.Name).Add(sender);
    });
    Listen(bound,item,'XDataRemoveAppReg',(sender,e)=>this.ApplicationRegistries.References.get_Item(e.Item.Name).Remove(sender));
  };
  Type.prototype.UnbindMetadataObject=function(item){const bound=state(this);if(item==null||!bound.delete(item))return;
    for(const data of item.XData.Values)this.ApplicationRegistries.References.get_Item(data.ApplicationRegistry.Name).Remove(item);Unlisten(bound,item);
  };
  Type.prototype.ReplaceLayoutViewportMetadata=function(layout,previous,current){if(!state(this).has(layout))return;this.UnbindMetadataObject(previous);this.BindMetadataObject(current);};
  Type.prototype.RetainedMetadataObjects=function(){const found=new Set();for(const item of this.AddedObjects.Values)for(const member of ObjectMetadataMembers(item))if(member!==null)found.add(member);return new ReferenceList(found);};
  Type.prototype.ApplicationRegistryReferences=function(registry){
    const result=new ReferenceList();if(registry==null||!this.ApplicationRegistries.Contains(registry))return result;
    for(const item of this.RetainedMetadataObjects()){
      let uses=0;for(const data of item.XData.Values)if(data.ApplicationRegistry===registry)uses++;
      if(uses)result.Add(new DxfObjectReference(item,uses));
    }
    for(const extra of this.MLeaderReferences(registry)){
      const i=Array.from(result).findIndex(r=>r.Reference===extra.Reference);
      if(i<0)result.Add(extra);else result.set_Item(i,new DxfObjectReference(extra.Reference,(result.get_Item(i).Uses+extra.Uses)|0));
    }
    return result;
  };
}
