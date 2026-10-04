// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable, Listen, Unlisten } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ArgumentException } from '../../runtime/Errors.js';
export class Groups extends RegisteredTable {
  constructor(document,handle=null){super(document,DxfObjectCode.GroupDictionary,handle,{eventRename:true,valueMembership:true});}
  AddRecord(group,assignHandle=true){if(group.IsUnnamed&&!group.Name)group.SetName('*A'+this.Owner.GroupNamesIndex++,false);return super.AddRecord(group,assignHandle);}
  #add(group,entity){
    if(entity.Owner!==null){if(entity.Owner.Owner.Owner.Owner!==this.Owner)throw new ArgumentException('The group and its entities must belong to the same document. Clone them instead.');}
    else this.Owner.Entities.Add(entity);
    this.References.get_Item(group.Name).Add(entity);
  }
  BeforeOwner(group){for(const entity of group.Entities)this.#add(group,entity);}
  AfterOwner(group){Listen(this,group,'EntityAdded',(sender,e)=>this.#add(sender,e.Item));Listen(this,group,'EntityRemoved',(sender,e)=>this.References.get_Item(sender.Name).Remove(e.Item));}
  Remove(value){
    const item=typeof value==='string'?this.get_Item(value):value;if(item==null||!this.Contains(item)||item.IsReserved)return false;
    for(const entity of item.Entities)entity.RemoveReactor(item);
    this.Owner.AddedObjects.Remove(item.Handle);this.References.Remove(item.Name);this.List.Remove(item.Name);item.Handle=null;item.Owner=null;Unlisten(this,item);return true;
  }
}
