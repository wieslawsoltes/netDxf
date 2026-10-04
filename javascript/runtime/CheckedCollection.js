import { ReferenceList } from './ReferenceList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from './Errors.js';
/** Collection<T> checks public index bounds before invoking the item validation hook. */
export class CheckedCollection extends ReferenceList {
  #check;
  constructor(check) { super(); this.#check = check; }
  #index(index, insert) {
    if (!Number.isInteger(index) || index < 0 || index >= this.Count + (insert ? 1 : 0))
      throw new ArgumentOutOfRangeException('index', index);
  }
  Add(item) { this.Insert(this.Count, item); }
  Insert(index, item) { this.#index(index, true); this.#check(item); super.Insert(index, item); }
  set_Item(index, item) { this.#index(index, false); this.#check(item); super.set_Item(index, item); }
}
/** Preserve code units and spelling; validation does not normalize stored names. */
export function CheckStoredName(value, parameter, { allowEmpty = false, nullIsArgumentNull = false } = {}) {
  if (value == null && nullIsArgumentNull) throw new ArgumentNullException(parameter);
  if (typeof value !== 'string' || (!allowEmpty && value.length === 0) || /[\0\r\n]/.test(value))
    throw new ArgumentException('Expected single-line Unicode text without NUL.', parameter);
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i);
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = value.charCodeAt(++i);
      if (!(next >= 0xdc00 && next <= 0xdfff)) throw new ArgumentException('Unpaired UTF-16 surrogate.', parameter);
    } else if (code >= 0xdc00 && code <= 0xdfff) throw new ArgumentException('Unpaired UTF-16 surrogate.', parameter);
  }
}
