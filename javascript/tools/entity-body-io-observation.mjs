// Envelope validation only: never normalize observed values or errors.
export function ValidateEntityBodyObservation(rows,count){
  if(!Number.isInteger(count)||count<1||!Array.isArray(rows)||rows.length!==count)throw new Error('Incomplete entity body observation.');
  for(const row of rows){const value=row?.value;
    if(row?.ok!==true||value==null||!['error','reader','entity','output','apps','seed'].every(k=>Object.hasOwn(value,k)))throw new Error('Malformed entity body envelope.');
    if(!Number.isInteger(value.reader?.code)||!Number.isInteger(value.reader?.position)||!Object.hasOwn(value.reader,'value')||typeof value.output!=='string'||!Array.isArray(value.apps)||typeof value.seed!=='string')throw new Error('Malformed entity body state.');
    if(value.error!==null&&(!['type','param','message','inner','version'].every(k=>Object.hasOwn(value.error,k))||typeof value.error.type!=='string'))throw new Error('Malformed entity body error.');
  }
  return rows;
}
