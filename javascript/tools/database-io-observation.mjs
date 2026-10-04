// Observation shape checks only. Values, errors and byte strings are never rewritten.
export function ValidateDatabaseObservation(rows,count,kind) {
  if(!Number.isInteger(count)||count<1||!Array.isArray(rows)||rows.length!==count)
    throw new Error('Incomplete database IO observation.');
  if(!['payload','metadata'].includes(kind))throw new Error('Unknown database observation kind.');
  const keys=kind==='payload'?['result','error','record','pending','seed','apps','classes','output']:
    ['result','error','code','position','value','current','declared','accepted','metadata'];
  for(const row of rows){
    const value=row?.value;
    if(row?.ok!==true||value==null||!keys.every(k=>Object.hasOwn(value,k)))throw new Error('Malformed database IO envelope.');
    if(value.error!==null&&(typeof value.error?.type!=='string'||!Object.hasOwn(value.error,'param')||!Object.hasOwn(value.error,'message')))
      throw new Error('Malformed database IO error.');
    if(kind==='payload'){
      if(value.record==null||value.pending==null||!Object.hasOwn(value.record,'model')||typeof value.seed!=='string'||typeof value.output!=='string'||!Array.isArray(value.apps))
        throw new Error('Malformed database payload state.');
    }else if(!Number.isInteger(value.code)||!Number.isInteger(value.position)||typeof value.current?.handle!=='string'||!Array.isArray(value.declared)||!Array.isArray(value.accepted)||!Array.isArray(value.metadata))
      throw new Error('Malformed source metadata state.');
  }
  return rows;
}
