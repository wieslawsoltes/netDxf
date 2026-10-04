// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable, BindResource, ChangeResource, Listen } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
const resources = [['TextStyle','TextStyles'], ['LeaderArrow','Blocks'], ['DimArrow1','Blocks'], ['DimArrow2','Blocks'],
  ['DimLineLinetype','Linetypes'], ['ExtLine1Linetype','Linetypes'], ['ExtLine2Linetype','Linetypes']];
export class DimensionStyles extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.DimensionStyleTable, handle, {eventRename:true, valueMembership:true}); }
  BeforeOwner(item, assignHandle) { for (const [property, name] of resources) BindResource(this,item,property,this.Owner[name],assignHandle); }
  AfterOwner(item) {
    Listen(this,item,'LinetypeChanged',(sender,e)=>ChangeResource(sender,e,this.Owner.Linetypes));
    Listen(this,item,'TextStyleChanged',(sender,e)=>ChangeResource(sender,e,this.Owner.TextStyles));
    Listen(this,item,'BlockChanged',(sender,e)=>{
      if (e.OldValue !== null) this.Owner.Blocks.References.get_Item(e.OldValue.Name).Remove(sender);
      // Source Add(null) throws; do not silently treat null as a default arrow here.
      e.NewValue = this.Owner.Blocks.Add(e.NewValue);
      if (e.NewValue !== null) this.Owner.Blocks.References.get_Item(e.NewValue.Name).Add(sender);
    });
  }
  BeforeRemove(item) { for (const [property,name] of resources) if (item[property] !== null) this.Owner[name].References.get_Item(item[property].Name).Remove(item); }
}
