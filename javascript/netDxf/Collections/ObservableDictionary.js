// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { ObservableDictionaryEventArgs } from './ObservableDictionaryEventArgs.js';
import { GenericDictionary, KeyValuePair, ReferenceEquals } from '../../runtime/GenericDictionary.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentException } from '../../runtime/Errors.js';

/** Source event/cancellation semantics. Trailing type arguments adapt erased CLR generics. */
export class ObservableDictionary {
  #items; #valueType;
  constructor(capacity = 0, comparer = null, keyType = null, valueType = null) {
    if (capacity === null || typeof capacity === 'object') { comparer = capacity; capacity = 0; }
    this.#items = new GenericDictionary(capacity, comparer, keyType, valueType); this.#valueType = valueType;
    for (const name of ['BeforeAddItem', 'AddItem', 'BeforeRemoveItem', 'RemoveItem'])
      Object.defineProperty(this, name, { value: new EventHook(), enumerable: true });
  }
  #event(name, item) { const e = new ObservableDictionaryEventArgs(item); this[name].Invoke(this, e); return e.Cancel; }
  get Count() { return this.#items.Count; }
  get IsReadOnly() { return false; }
  get Keys() { return this.#items.Keys; }
  get Values() { return this.#items.Values; }
  get_Item(key) { return this.#items.get_Item(key); }
  set_Item(key, value) {
    const remove = KeyValuePair(key, this.#items.get_Item(key)), add = KeyValuePair(key, value);
    if (this.#event('BeforeRemoveItem', remove) || this.#event('BeforeAddItem', add)) return;
    this.#items.set_Item(add.Key, add.Value);
    this.#event('AddItem', add); this.#event('RemoveItem', remove);
  }
  Add(key, value) {
    const add = KeyValuePair(key, value);
    if (this.#event('BeforeAddItem', add)) throw new ArgumentException('The item cannot be added to the dictionary.', 'value');
    this.#items.Add(add.Key, add.Value); this.#event('AddItem', add);
  }
  Remove(key) {
    if (!this.#items.ContainsKey(key)) return false;
    const remove = KeyValuePair(key, this.#items.get_Item(key));
    if (this.#event('BeforeRemoveItem', remove)) return false;
    this.#items.Remove(remove.Key); this.#event('RemoveItem', remove); return true;
  }
  Clear() { for (const key of Array.from(this.#items.Keys)) this.Remove(key); }
  ContainsKey(key) { return this.#items.ContainsKey(key); }
  ContainsValue(value) { return this.#items.ContainsValue(value); }
  TryGetValue(key, output) { return this.#items.TryGetValue(key, output); }
  GetEnumerator() { return this.#items.GetEnumerator(); }
  [Symbol.iterator]() { return this.GetEnumerator(); }
  // Explicit ICollection<KeyValuePair<TKey,TValue>> interface adapters.
  AddPair(pair) { this.Add(pair.Key, pair.Value); }
  RemovePair(pair) {
    if (!ReferenceEquals(pair.Value, this.#items.get_Item(pair.Key), this.#valueType)) return false;
    return this.Remove(pair.Key);
  }
  ContainsPair(pair) { return this.#items.ContainsPair(pair); }
  CopyTo(array, arrayIndex = 0) { this.#items.CopyTo(array, arrayIndex); }
}
