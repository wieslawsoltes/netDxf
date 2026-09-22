// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectReference } from '../DxfObjectReference.js';
import { GenericDictionary } from '../../runtime/GenericDictionary.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { NullReferenceException } from '../../runtime/Errors.js';

const identities = new WeakMap(); let nextIdentity = 0;
const identityComparer = Object.freeze({
  Equals(left, right) { return left === right; },
  GetHashCode(value) {
    if (!identities.has(value)) identities.set(value, nextIdentity = (nextIdentity + 1) | 0);
    return identities.get(value);
  },
});
/** Internal table-reference accounting. The optional second Add argument selects
 * the erased IEnumerable overload, including a null enumerable input.
 */
export class DxfObjectReferences {
  #references;
  constructor(useReferenceIdentity = false) {
    this.#references = new GenericDictionary(0, useReferenceIdentity ? identityComparer : null, null, 'int');
  }
  IsEmpty() { return this.#references.Count === 0; }
  Add(item, enumerable = item != null && !(item instanceof DxfObject) && typeof item[Symbol.iterator] === 'function') {
    if (enumerable) {
      if (item == null) throw new NullReferenceException();
      for (const entry of item) {
        if (entry == null) throw new NullReferenceException();
        const key = entry.Reference;
        if (this.#references.ContainsKey(key))
          this.#references.set_Item(key, (this.#references.get_Item(key) + entry.Uses) | 0);
        else this.#references.Add(key, entry.Uses);
      }
    } else if (this.#references.ContainsKey(item))
      this.#references.set_Item(item, (this.#references.get_Item(item) + 1) | 0);
    else this.#references.Add(item, 1);
  }
  Remove(item) {
    if (!this.#references.ContainsKey(item)) return false;
    this.#references.set_Item(item, (this.#references.get_Item(item) - 1) | 0);
    if (this.#references.get_Item(item) === 0) this.#references.Remove(item);
    return true;
  }
  ToList() {
    const result = new ReferenceList();
    for (const pair of this.#references) result.Add(new DxfObjectReference(pair.Key, pair.Value));
    return result;
  }
}
