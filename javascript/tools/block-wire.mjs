// Observation only: snapshots are produced independently by C# and JavaScript.
import {Insert,Block,BlockRecord,EndBlock,BlockEntityChangeEventArgs,BlockAttributeDefinitionChangeEventArgs} from '../index.js';
const owner=value=>value==null?null:{code:value.CodeName,handle:value.Handle};
export function blockWire(value,wire){
  if(value instanceof BlockEntityChangeEventArgs||value instanceof BlockAttributeDefinitionChangeEventArgs)return {type:value.constructor.name,item:wire(value.Item)};
  if(value instanceof BlockRecord)return {type:'BlockRecord',name:wire(value.Name),code:value.CodeName,handle:value.Handle,owner:owner(value.Owner),layout:owner(value.Layout),units:value.Units,explode:value.AllowExploding,uniform:value.ScaleUniformly,internal:value.IsForInternalUseOnly,xdata:Array.from(value.XData.Values,wire)};
  if(value instanceof EndBlock)return {type:'EndBlock',code:value.CodeName,handle:value.Handle,owner:owner(value.Owner),xdata:Array.from(value.XData.Values,wire)};
  if(!(value instanceof Block))return undefined;
  return {type:'Block',name:wire(value.Name),code:value.CodeName,handle:value.Handle,reserved:value.IsReserved,internal:value.IsForInternalUseOnly,flags:value.Flags,xref:wire(value.XrefFile),isXref:value.IsXRef,description:wire(value.Description),origin:wire(value.Origin),layer:wire(value.Layer),record:wire(value.Record),end:wire(value.End),entities:Array.from(value.Entities,wire),attributes:Array.from(value.AttributeDefinitions.Values,wire),xdata:Array.from(value.XData.Values,wire)};
}

export function insertEntityWire(value,common,wire){
  if(!(value instanceof Insert))return undefined;
  return {common,block:wire(value.Block),position:wire(value.Position),scale:wire(value.Scale),rotation:wire(value.Rotation),rows:value.RowCount,columns:value.ColumnCount,dx:wire(value.ColumnSpacing),dy:wire(value.RowSpacing),multiple:value.IsMultiple,count:value.InstanceCount,attributes:Array.from(value.Attributes,wire)};
}
