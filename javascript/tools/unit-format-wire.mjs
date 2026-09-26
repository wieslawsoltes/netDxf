// Observe the public formatting model without calculating its results.
import {UnitStyleFormat} from '../index.js';
export function unitFormatWire(value,wire){
  if(!(value instanceof UnitStyleFormat))return;
  const fields={};
  for(const [name,descriptor] of Object.entries(Object.getOwnPropertyDescriptors(UnitStyleFormat.prototype)))
    if(descriptor.get&&!name.startsWith('$'))fields[name]=wire(value[name]);
  return {type:'UnitStyleFormat',fields};
}
