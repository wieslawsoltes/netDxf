import { NumberText } from './GeometryRuntime.js';
import { ArgumentException, RequireInteger } from './Errors.js';
/** Explicit CLR boxing for object-valued APIs where JavaScript Number alone cannot
 * distinguish Int32, Int16, Byte and Double. Plain Numbers are type-directed by
 * the receiving column; these wrappers preserve a caller's explicit boxed type.
 */
export class BoxedScalar {
  #type; #value;
  constructor(type,value) {
    switch(type) {
      case 'Int32':value=RequireInteger(value,-2147483648,2147483647);break;
      case 'Int16':value=RequireInteger(value,-32768,32767);break;
      case 'Byte':value=RequireInteger(value,0,255);break;
      case 'Double':if(typeof value!=='number')throw new ArgumentException('Double requires Number.','value');break;
      case 'Int64':if(typeof value!=='bigint'||BigInt.asIntN(64,value)!==value)throw new ArgumentException('Int64 requires signed BigInt.','value');break;
      default:throw new ArgumentException('Unsupported boxed scalar type.','type');
    }
    this.#type=type;this.#value=value;Object.freeze(this);
  }
  get Type(){return this.#type;}get Value(){return this.#value;}
  ToString(){return this.#type==='Double'?NumberText(this.#value):String(this.#value);}
}
