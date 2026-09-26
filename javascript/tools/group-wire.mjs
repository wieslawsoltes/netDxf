// Observation-only snapshots; entity behavior remains in production code.
import { Group, GroupEntityChangeEventArgs } from '../index.js';
export function groupWire(value,wire) {
  if(value instanceof GroupEntityChangeEventArgs)return {type:'GroupEntityChangeEventArgs',item:wire(value.Item)};
  if(!(value instanceof Group))return undefined;
  return {type:'Group',name:value.Name,code:value.CodeName,reserved:value.IsReserved,unnamed:value.IsUnnamed,
    description:wire(value.Description),selectable:value.IsSelectable,handle:value.Handle,owner:value.Owner?.CodeName??null,
    entities:Array.from(value.Entities,wire),xdata:Array.from(value.XData.Values,wire)};
}
