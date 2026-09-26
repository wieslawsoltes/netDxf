// Validate transport envelopes only; no native errors, bits or values are normalized.
export function ValidateObjectGraphObservation(rows,count){
  if(!Number.isInteger(count)||count<1||!Array.isArray(rows)||rows.length!==count)throw new Error('Incomplete OBJECTS observation.');
  for(const row of rows){
    const value=row?.value;
    if(row?.ok!==true||value==null||!['result','error','reader','records','objects','root','seed','registry','classes','pendingStyles','output'].every(key=>Object.hasOwn(value,key)))throw new Error('Malformed OBJECTS observation envelope.');
    if(!Number.isInteger(value.reader?.code)||!Number.isInteger(value.reader?.position)||!Object.hasOwn(value.reader,'value')||!Array.isArray(value.records)||!Array.isArray(value.registry)||!(value.objects===null||Array.isArray(value.objects))||typeof value.seed!=='string'||typeof value.output!=='string'||!Number.isInteger(value.pendingStyles))throw new Error('Malformed OBJECTS observation state.');
    if(value.error!==null&&(typeof value.error.type!=='string'||!['param','message','inner'].every(key=>Object.hasOwn(value.error,key))))throw new Error('Malformed OBJECTS error.');
  }
  return rows;
}
