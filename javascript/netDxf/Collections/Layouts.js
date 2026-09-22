// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable, Listen, Unlisten } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { DxfObjectReferences } from './DxfObjectReferences.js';
import { Block } from '../Blocks/Block.js';
import { ArgumentException, OverflowException } from '../../runtime/Errors.js';
export class Layouts extends RegisteredTable {
  static MaxCapacity=256;
  constructor(document,handle=null){super(document,DxfObjectCode.LayoutDictionary,handle);}
  AddRecord(layout,assignHandle=true){
    if(this.Count>=Layouts.MaxCapacity)throw new OverflowException('Layout table overflow.');
    const existing=this.get_Item(layout.Name);if(existing!==null)return existing;
    layout.Owner=this;let block=layout.AssociatedBlock;
    if(layout.IsPaperSpace&&block===null){
      const name=this.Count===1?Block.DefaultPaperSpaceName:Block.DefaultPaperSpaceName+(this.Count-2);
      block=Block.CreateOverload('string,System.Collections.Generic.IEnumerable<netDxf.Entities.EntityObject>,System.Collections.Generic.IEnumerable<netDxf.Entities.AttributeDefinition>,bool',name,null,null,false);
      if(layout.TabOrder===0)layout.TabOrder=this.Count;
    }
    block=this.Owner.Blocks.Add(block);layout.AssociatedBlock=block;block.Record.Layout=layout;this.Owner.Blocks.References.get_Item(block.Name).Add(layout);
    if(layout.Viewport!==null)layout.Viewport.Owner=block;
    if(assignHandle||!layout.Handle)this.Owner.NumHandles=layout.AssignHandle(this.Owner.NumHandles);
    this.List.Add(layout.Name,layout);this.References.Add(layout.Name,new DxfObjectReferences());this.References.get_Item(layout.Name).Add(block);
    Listen(this,layout,'NameChanged',(sender,e)=>{
      if(this.Contains(e.NewValue))throw new ArgumentException('There is already another layout with the same name.');
      this.List.Remove(sender.Name);this.List.Add(e.NewValue,sender);
      // Preserve source behavior: layout name notifications do not re-key References.
    });
    this.Owner.AddedObjects.Add(layout.Handle,layout);return layout;
  }
  Remove(value){
    const item=typeof value==='string'?this.get_Item(value):value;
    if(item==null||!this.Contains(item)||item.IsReserved||this.Owner.StoredTableReferencesRemoval(item))return false;
    for(const entity of Array.from(item.AssociatedBlock.Entities))item.AssociatedBlock.Entities.Remove(entity);
    for(const attribute of Array.from(item.AssociatedBlock.AttributeDefinitions.Values))item.AssociatedBlock.AttributeDefinitions.Remove(attribute.Tag);
    this.Owner.Blocks.References.get_Item(item.AssociatedBlock.Name).Remove(item);this.Owner.Blocks.Remove(item.AssociatedBlock);item.AssociatedBlock=null;
    this.Owner.AddedObjects.Remove(item.Handle);this.References.Remove(item.Name);this.List.Remove(item.Name);item.Handle=null;item.Owner=null;Unlisten(this,item);
    this.RenameAssociatedBlocks();return true;
  }
  RenameAssociatedBlocks(){
    const names=Array.from(this.Items).filter(l=>l.IsPaperSpace).map(l=>l.AssociatedBlock.Name.slice(Block.DefaultPaperSpaceName.length)).map(s=>/^[+-]?\d+$/.test(s)?Number(s):-1).sort((a,b)=>a-b);
    names.forEach((n,i)=>this.Owner.Blocks.get_Item(Block.DefaultPaperSpaceName+(n===-1?'':n)).SetName(Block.DefaultPaperSpaceName+(i===0?'':i-1),false));
  }
}
