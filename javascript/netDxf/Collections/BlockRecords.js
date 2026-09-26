// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable, Listen, Unlisten, ChangeResource } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { DxfObjectReferences } from './DxfObjectReferences.js';
export class BlockRecords extends RegisteredTable {
  constructor(document, handle = null) { super(document,DxfObjectCode.BlockRecordTable,handle); }
  AddRecord(block,assignHandle=true) {
    const existing=this.get_Item(block.Name);if(existing!==null)return existing;
    this.Owner.ValidateStoredTableBlockAdoption(block);
    if(assignHandle||!block.Handle)this.Owner.NumHandles=block.AssignHandle(this.Owner.NumHandles);
    this.List.Add(block.Name,block);this.References.Add(block.Name,new DxfObjectReferences());
    block.Layer=this.Owner.Layers.Add(block.Layer);this.Owner.Layers.References.get_Item(block.Layer.Name).Add(block);
    for(const entity of block.Entities)this.Owner.AddEntityToDocument(entity,assignHandle);
    for(const attribute of block.AttributeDefinitions.Values)this.Owner.AddAttributeDefinitionToDocument(attribute,assignHandle);
    block.Record.Owner=this;
    Listen(this,block,'LayerChanged',(sender,e)=>ChangeResource(sender,e,this.Owner.Layers));
    Listen(this,block,'EntityAdded',(_,e)=>this.Owner.AddEntityToDocument(e.Item,!e.Item.Handle));
    Listen(this,block,'EntityRemoved',(_,e)=>this.Owner.RemoveEntityFromDocument(e.Item));
    Listen(this,block,'AttributeDefinitionAdded',(_,e)=>this.Owner.AddAttributeDefinitionToDocument(e.Item,!e.Item.Handle));
    Listen(this,block,'AttributeDefinitionRemoved',(_,e)=>this.Owner.RemoveAttributeDefinitionFromDocument(e.Item));
    this.Owner.AddedObjects.Add(block.Handle,block);this.Owner.AddedObjects.Add(block.Record.Handle,block.Record);return block;
  }
  Remove(value) {
    const item=typeof value==='string'?this.get_Item(value):value;
    if(item==null||item.Record.Owner!==this||this.get_Item(item.Name)!==item||item.IsReserved||this.HasReferences(item)||this.Owner.StoredTableReferencesRemoval(item))return false;
    this.Owner.Layers.References.get_Item(item.Layer.Name).Remove(item);
    for(const entity of item.Entities){this.Owner.RemoveEntityFromDocument(entity);entity.Owner=item;}
    for(const attribute of item.AttributeDefinitions.Values){this.Owner.RemoveAttributeDefinitionFromDocument(attribute);attribute.Owner=item;}
    this.Owner.AddedObjects.Remove(item.Handle);this.Owner.AddedObjects.Remove(item.Record.Handle);this.References.Remove(item.Name);this.List.Remove(item.Name);
    item.Record.Handle=null;item.Record.Owner=null;item.Handle=null;Unlisten(this,item);return true;
  }
}
