import { sectionInput } from './transport-sections-wire.mjs';
import * as api from '../index.js';
import { ArgumentException,NullReferenceException } from '../runtime/Errors.js';
export function WireInput(value,target){
  if(value&&typeof value==='object'){
    if('h'in value){let handle=target(value.h).Handle;if(handle===null)throw new NullReferenceException();if(value.lower)handle=handle.toLowerCase();return '0'.repeat(value.pad??0)+handle;}
    if('ref'in value)return target(value.ref);
    if('vector'in value)return new api.Vector3(...value.vector.map(v=>WireInput(v,target)));
  }
  return sectionInput(value);
}
export function PayloadFailure(error){return {type:error.name,param:error.ParamName??null,message:error instanceof ArgumentException||error instanceof NullReferenceException?null:error.Message??error.message,
  inner:error.InnerException?{type:error.InnerException.name,param:error.InnerException.ParamName??null}:null};}
