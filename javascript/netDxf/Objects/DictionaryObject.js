// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { GenericDictionary } from '../../runtime/GenericDictionary.js';
/** Original legacy dictionary projection used by typed document import/output. */
export class DictionaryObject extends DxfObject {
  #entries=new GenericDictionary(0,null,'string');
  IsHardOwner=true; Cloning=1;
  constructor(owner){super('DICTIONARY');this.Owner=owner;}
  get Entries(){return this.#entries;}
}
