import {Sin,Cos} from '../runtime/reference-math/sincos.js';
import {Asin,Acos} from '../runtime/reference-math/asincos.js';
import {Atan} from '../runtime/reference-math/atan.js';
import {Atan2} from '../runtime/reference-math/atan2.js';
import {Tan} from '../runtime/reference-math/tan.js';
import {fma} from '../runtime/reference-math/arithmetic.js';
import {doubleBits,fromBits} from './wire.mjs';
const functions={Sin,Cos,Asin,Acos,Atan,Atan2,Tan,Fma:fma};
export const referenceMathCall=calls=>calls.map(call=>doubleBits(functions[call.name](...call.args.map(fromBits))));
