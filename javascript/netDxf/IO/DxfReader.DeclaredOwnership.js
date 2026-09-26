// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfXRecord,DxfDatabaseObject } from '../Objects/DxfDatabaseObject.js';
import { DxfDataTable } from '../Objects/DxfDataTable.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { WrappedFormat } from '../../runtime/DatabaseIOContext.js';
const asObject=value=>value instanceof DxfDatabaseObject?value:null;
export function ResolveDeclaredOwnership(context){
  for(const record of context.Document.Objects.Items){
    if(!(record instanceof DxfXRecord)||!record.IsTableRoundtripRecord)continue;
    if(record.IsCompositeTableRoundtripRecord){
      try{const content=context.GetObjectBySourceHandle(record.Data.get_Item(1).Value),geometry=context.GetObjectBySourceHandle(record.Data.get_Item(9).Value),data=context.GetObjectBySourceHandle(record.Data.get_Item(14).Value);
        record.BindCompositeTableRoundtripChildren(asObject(content),asObject(geometry),data instanceof DxfDataTable?data:null);
      }catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid composite TABLE ownership record: '+record.Handle,error);throw error;}continue;
    }
    if(Array.from(record.Data).slice(1).some(t=>t.Code===102))continue;
    const content=Array.from(record.Data).find(t=>t.Code===360),geometry=Array.from(record.Data).find(t=>t.Code===361);
    try{record.BindTableRoundtripChildren(content===undefined?null:asObject(context.Document.GetObjectByHandle(content.Value)),geometry===undefined?null:asObject(context.Document.GetObjectByHandle(geometry.Value)));}
    catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid TABLE roundtrip ownership record: '+record.Handle,error);throw error;}
  }
}
