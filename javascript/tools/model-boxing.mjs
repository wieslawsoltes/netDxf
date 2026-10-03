// CLR object-valued input transport. These boxes represent input types and references,
// never expected output values or implementation results.
import {BoxedScalar} from '../runtime/BoxedScalar.js';
import {BoxedString} from '../runtime/BoxedString.js';
import {BoxedBoolean} from '../runtime/BoxedBoolean.js';
import {HeaderEnum} from '../runtime/HeaderBox.js';
import {resolve} from './model-types.mjs';
const emptyString=new BoxedString('');
function boxed(value,descriptor){
  if(value==null||typeof value==='object')return value;
  if(typeof value==='string')return value===''?emptyString:new BoxedString(value);
  if(typeof value==='boolean')return new BoxedBoolean(value);
  if(descriptor&&typeof descriptor==='object'&&'enum' in descriptor)
    return new HeaderEnum(descriptor.enum.split('.').at(-1),resolve(descriptor.enum),value);
  const kind=descriptor&&typeof descriptor==='object'?['int','short','byte','long'].find(k=>k in descriptor):null;
  return new BoxedScalar({int:'Int32',short:'Int16',byte:'Byte',long:'Int64'}[kind]??(typeof value==='bigint'?'Int64':'Double'),value);
}
/** A ref descriptor reuses the same input box; separate literals allocate separate boxes. */
export class ModelBoxing {
  #values;#descriptors=new Map();#boxes=new Map();
  constructor(values){this.#values=values;}
  Register(id,descriptor){this.#descriptors.set(id,descriptor);this.#boxes.delete(id);}
  Read(descriptor,value){
    if(descriptor&&typeof descriptor==='object'&&'ref' in descriptor){
      const id=descriptor.ref;
      if(!this.#boxes.has(id))this.#boxes.set(id,boxed(this.#values.get(id),this.#descriptors.get(id)));
      return this.#boxes.get(id);
    }
    return boxed(value,descriptor);
  }
}
