// Typed boxed enum metadata for object-valued header entries. Integer boxes use BoxedScalar.
import { BoxedScalar } from './BoxedScalar.js';
import { RequireInteger } from './Errors.js';
/** All enums currently used by HeaderVariables have Int32 as their underlying type. */
export class HeaderEnum {
  #type; #value; #names;
  constructor(type,names,value){this.#type=type;this.#names=names;this.#value=RequireInteger(value,-2147483648,2147483647);Object.freeze(this);}
  get Type(){return this.#type;} get Value(){return this.#value;}
  ToString(){return Object.entries(this.#names).find(([,value])=>value===this.#value)?.[0]??String(this.#value);}
}
export const UnwrapHeaderNumber = value => value instanceof BoxedScalar || value instanceof HeaderEnum ? value.Value : value;
