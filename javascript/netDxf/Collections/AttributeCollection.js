import { CopyReferenceArray } from '../../runtime/ReferenceArrayCopy.js';
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
/** Read-only snapshot of attribute references; element objects remain mutable. */
export class AttributeCollection {
  #items;
  constructor(attributes=[]){if(attributes==null)throw new ArgumentNullException('attributes');this.#items=new ReferenceList(attributes);}
  static get IsReadOnly(){return true;}
  get Count(){return this.#items.Count;}
  get_Item(index){return this.#items.get_Item(index);}
  Contains(item){return this.#items.Contains(item);}
  IndexOf(item){return this.#items.IndexOf(item);}
  CopyTo(array,arrayIndex){CopyReferenceArray(this.#items,array,arrayIndex);}
  AttributeWithTag(tag){
    if(tag==null||tag.length===0)return null;
    for(const att of this.#items){if(att==null)throw new NullReferenceException();if(att.Definition!==null&&OrdinalIgnoreCaseEquals(tag,att.Tag))return att;}
    return null;
  }
  GetEnumerator(){return this.#items.GetEnumerator();}
  [Symbol.iterator](){return this.GetEnumerator();}
}
