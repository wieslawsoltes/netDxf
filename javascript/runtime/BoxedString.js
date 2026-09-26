// Explicit CLR String reference identity for object-valued APIs. JS primitive strings
// carry values only; share one adapter when ReferenceEquals semantics are required.
import {ArgumentException} from './Errors.js';
let empty;
export class BoxedString {
  #value;
  constructor(value){if(typeof value!=='string')throw new ArgumentException('A string is required.','value');if(value===''&&empty)return empty;this.#value=value;Object.freeze(this);if(value==='')empty=this;}
  get Type(){return 'String';}get Value(){return this.#value;}
  Equals(other){return this.#value===(other instanceof BoxedString?other.Value:other);}
  ToString(){return this.#value;}
  Clone(){return this;}
}
