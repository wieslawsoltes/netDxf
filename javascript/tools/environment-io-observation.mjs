// Transport validation only. Does not alter any observed value, byte, or error.
export function ValidateEnvironmentObservation(rows,count) {
  if(!Number.isInteger(count)||count<1||!Array.isArray(rows)||rows.length!==count)throw new Error('Incomplete environment IO observation.');
  for(const row of rows) {
    const v=row?.value;
    if(row?.ok!==true||v==null||!['result','error','record','pending','plot','shade','cursor','public','hosts','layouts','seed','apps','classes','output'].every(k=>Object.hasOwn(v,k)))throw new Error('Malformed environment IO envelope.');
    if(v.error!==null&&(!['type','param','message','inner'].every(k=>Object.hasOwn(v.error,k))||typeof v.error.type!=='string'))throw new Error('Malformed environment IO error.');
    if(v.record==null||!Object.hasOwn(v.record,'model')||!Array.isArray(v.record.reactors)||v.pending==null||!['shade','geo','sun'].every(k=>Array.isArray(v.pending[k])))throw new Error('Malformed environment IO state.');
    if(typeof v.public!=='boolean'||!Number.isInteger(v.cursor)||!Array.isArray(v.hosts)||v.hosts.length!==3||!Array.isArray(v.layouts)||!Array.isArray(v.apps)||typeof v.seed!=='string'||typeof v.output!=='string')throw new Error('Malformed environment IO snapshot.');
  }
  return rows;
}
