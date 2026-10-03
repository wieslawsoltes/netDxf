// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable, ChangeResource, Listen } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
export class MLineStyles extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.MLineStyleDictionary, handle, {eventRename:true, valueMembership:true}); }
  #add(item, element) { element.Linetype = this.Owner.Linetypes.Add(element.Linetype); this.Owner.Linetypes.References.get_Item(element.Linetype.Name).Add(item); }
  BeforeOwner(item) { for (const element of item.Elements) this.#add(item,element); }
  AfterOwner(item) {
    Listen(this,item,'MLineStyleElementAdded',(sender,e)=>this.#add(sender,e.Item));
    Listen(this,item,'MLineStyleElementRemoved',(sender,e)=>this.Owner.Linetypes.References.get_Item(e.Item.Linetype.Name).Remove(sender));
    Listen(this,item,'MLineStyleElementLinetypeChanged',(sender,e)=>ChangeResource(sender,e,this.Owner.Linetypes));
  }
  BeforeRemove(item) { for (const element of item.Elements) this.Owner.Linetypes.References.get_Item(element.Linetype.Name).Remove(item); }
}
