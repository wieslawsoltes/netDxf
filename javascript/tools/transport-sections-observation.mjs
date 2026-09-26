// Fail closed on incomplete/malformed observations; this is not output normalization.
const own=(object,key)=>object!==null&&typeof object==='object'&&Object.hasOwn(object,key);
const required=(object,keys)=>keys.every(key=>own(object,key));
const base64=value=>typeof value==='string'&&/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value);
export function ValidateTransportObservation(rows, count) {
  if(!Number.isInteger(count)||count<1||!Array.isArray(rows)||rows.length!==count)throw new Error('Incomplete transport-section observation.');
  for(const row of rows) {
    const value=row?.value;
    if(row?.ok!==true||!required(value,['result','error','reader','output','common','background'])||!base64(value.output))throw new Error('Malformed transport-section envelope.');
    const error=value.error;
    if(error!==null&&(!required(error,['type','param','message','inner','version'])||typeof error.type!=='string'||!(error.param===null||typeof error.param==='string')||!(error.message===null||typeof error.message==='string')||!(error.version===null||Number.isInteger(error.version))||
        error.inner!==null&&(!required(error.inner,['type','param'])||typeof error.inner.type!=='string'||!(error.inner.param===null||typeof error.inner.param==='string'))))throw new Error('Malformed transport-section error.');
    const reader=value.reader,common=value.common;
    if(!required(reader,['code','value','position'])||!Number.isInteger(reader.code)||!Number.isInteger(reader.position))throw new Error('Malformed transport-section reader state.');
    if(!required(common,['color','shadow','seen','declared','actual','proxy','payload'])||typeof common.seen!=='boolean'||!Number.isInteger(common.actual)||!(common.declared===null||Number.isInteger(common.declared))||!(common.proxy===null||base64(common.proxy)))throw new Error('Malformed transport-section metadata state.');
    if(common.payload!==null&&(!required(common.payload,['bytes','open'])||!base64(common.payload.bytes)||typeof common.payload.open!=='boolean'))throw new Error('Malformed payload observation.');
    if(value.background!==null&&!required(value.background,['flags','scale','index','color','name','transparency']))throw new Error('Malformed background observation.');
  }
  return rows;
}
