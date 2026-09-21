// Observation-only adapter. HeaderVariables is an opaque reference until its public
// getters/lists are explicitly requested; wall clocks are never normalized or masked.
import { HeaderVariables,HeaderVariable,HeaderDateTime,HeaderTimeSpan,HeaderEnum,BoxedString,BoxedBoolean } from '../index.js';
import { BoxedScalar } from '../runtime/BoxedScalar.js';
function typeName(value){
  if(value==null)return null;
  if(value instanceof BoxedScalar||value instanceof HeaderEnum||value instanceof BoxedString||value instanceof BoxedBoolean)return value.Type;
  if(value instanceof HeaderDateTime)return 'DateTime';
  if(value instanceof HeaderTimeSpan)return 'TimeSpan';
  return {number:'Double',string:'String',boolean:'Boolean',bigint:'Int64'}[typeof value]??value.constructor.name;
}
export function headerWire(value,wire){
  if(value instanceof HeaderVariables)return {type:'HeaderVariables'};
  if(value instanceof HeaderVariable)return {type:'HeaderVariable',name:value.Name,code:value.GroupCode,valueType:typeName(value.Value),value:wire(value.Value)};
  if(value instanceof BoxedScalar||value instanceof HeaderEnum||value instanceof BoxedString||value instanceof BoxedBoolean)return wire(value.Value);
  if(value instanceof HeaderDateTime)return {date:value.Calendar,ticks:String(value.Ticks),kind:value.Kind};
  if(value instanceof HeaderTimeSpan)return {ticks:String(value.Ticks)};
  return undefined;
}
