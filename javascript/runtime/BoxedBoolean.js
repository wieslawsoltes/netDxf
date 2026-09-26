// Explicit CLR Boolean box for object-valued APIs whose reference identity is observable.
import {ArgumentException} from './Errors.js';
export class BoxedBoolean {
  #value;
  constructor(value){if(typeof value!=='boolean')throw new ArgumentException('Boolean requires a boolean.','value');this.#value=value;Object.freeze(this);}
  get Type(){return 'Boolean';}get Value(){return this.#value;}
  Equals(other){return other instanceof BoxedBoolean&&other.Value===this.#value;}
  ToString(){return this.#value?'True':'False';}
}
