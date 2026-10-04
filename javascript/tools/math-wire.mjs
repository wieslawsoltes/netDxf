import * as HighPrecision from './HighPrecisionMath.mjs';
import { DotNetMath, RemainderDouble } from '../runtime/GeometryRuntime.js';
import { doubleBits, fromBits } from './wire.mjs';
export function mathCall(requests) {
  return requests.map(request=>{
    const fn=request.member==='Remainder'?RemainderDouble:DotNetMath[request.member];
    if(typeof fn!=='function')throw new Error('Unknown math request: '+request.member);
    return {id:request.id,result:doubleBits(fn(...request.args.map(fromBits)))};
  });
}

/** Development reference only; never replaces the production backend. */
export function highPrecisionMathCall(requests) {
  return requests.map(request => {
    const fn = request.member === 'Remainder' ? RemainderDouble : HighPrecision[request.member];
    if (typeof fn !== 'function') throw new Error('Unknown high-precision request: ' + request.member);
    return { id: request.id, result: doubleBits(fn(...request.args.map(fromBits))) };
  });
}
