// Strict transport validation, independent of codec behavior and expected values.
export function ValidateCodecObservation(value, requested) {
  const object=v=>v!==null&&typeof v==='object'&&!Array.isArray(v);
  const error=v=>v===null||object(v)&&typeof v.name==='string'&&(v.param===null||typeof v.param==='string')&&(v.message===null||typeof v.message==='string');
  const host=v=>object(v)&&Array.isArray(v.calls)&&typeof v.closed==='boolean';
  if(!Number.isInteger(requested)||requested<1||!object(value)||!Object.hasOwn(value,'constructor')||!error(value.constructor)||!Object.hasOwn(value,'initial')||!Array.isArray(value.results)||!host(value.host))throw new Error('Invalid codec observation envelope.');
  const count=value.constructor===null?requested:0;
  if(value.results.length!==count)throw new Error('Incomplete codec command observations.');
  if(value.constructor!==null&&value.initial!==null)throw new Error('Rejected constructor cannot expose a reader/writer instance.');
  for(const item of value.results)if(!object(item)||!Object.hasOwn(item,'value')||!error(item.error)||!Object.hasOwn(item,'state')||!host(item.host))throw new Error('Malformed codec command observation.');
  return value;
}
