// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../index.js';
import { BoxedString } from '../runtime/BoxedString.js';
export function InstallDocumentOpaqueEntity(Type){
  Type.prototype.RemovedOpaqueHandle=function(handle,removed){return removed.has(this.StoredTableHandleTarget(api.DxfOpaqueEntity.CanonicalHandle(handle)));};
  Type.prototype.OpaqueEntityReferencesRemoval=function(removed){
    for(const entity of this.AddedObjects.Values)if(entity instanceof api.DxfOpaqueEntity&&!removed.has(entity)&&entity.ReferencesRemoval(removed))return true;
    if(!Array.from(removed).some(item=>item instanceof api.DxfOpaqueEntity))return false;
    for(const item of this.RetainedMetadataObjects()){
      if(removed.has(item))continue;
      if(item.Owner!==null&&removed.has(item.Owner)||item.ExtensionDictionary!==null&&removed.has(item.ExtensionDictionary))return true;
      if(Array.from(item.PersistentReactors).some(value=>removed.has(value)))return true;
      if(item instanceof api.EntityObject&&Array.from(item.Reactors).some(value=>removed.has(value)))return true;
      for(const data of item.XData.Values)for(const tag of data.XDataRecord)if(tag.Code===api.XDataCode.DatabaseHandle&&this.RemovedOpaqueHandle(tag.Value,removed))return true;
      if(item instanceof api.DxfDatabaseObject&&Array.from(item.DatabaseReferences).some(value=>removed.has(value)))return true;
      if(item instanceof api.DxfDictionary&&Array.from(item.Entries).some(entry=>removed.has(entry.Target)))return true;
      if(item instanceof api.DxfDictionaryWithDefault&&item.Default!==null&&removed.has(item.Default))return true;
      const tags=item instanceof api.DxfXRecord?item.Data:item instanceof api.DxfOpaqueObject?item.Tags:[];
      for(const tag of tags)if(api.DxfObjectDatabase.IsReference(tag)&&this.RemovedOpaqueHandle(tag.Value,removed))return true;
    }
    for(const variable of this.DrawingVariables.CustomValues()){
      const kind=api.DxfGroupCode.GetHandleKind(variable.GroupCode),value=variable.Value instanceof BoxedString?variable.Value.Value:variable.Value;
      if(kind!==api.DxfHandleKind.None&&kind!==api.DxfHandleKind.Arbitrary&&typeof value==='string'&&this.RemovedOpaqueHandle(value,removed))return true;
    }
    return false;
  };
}
