// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DimensionStyleOverride } from '../Tables/DimensionStyleOverride.js';
import { DimensionStyleOverrideDictionaryEventArgs } from './DimensionStyleOverrideDictionaryEventArgs.js';
import { EnumDictionary } from '../../runtime/EnumDictionary.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentException, ArgumentNullException, RequireInteger } from '../../runtime/Errors.js';
/** Source-ordered events and .NET dictionary views; no rollback is invented after observer failures. */
export class DimensionStyleOverrideDictionary {
  #items = new EnumDictionary();
  constructor(capacity = 0) {
    RequireInteger(capacity, 0, 2147483647, 'capacity');
    for (const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])
      Object.defineProperty(this, event, {value: new EventHook(), enumerable: true});
  }
  #event(name, item) {
    const e = new DimensionStyleOverrideDictionaryEventArgs(item);
    this[name].Invoke(this, e); return e.Cancel;
  }
  get Count() { return this.#items.Count; }
  get IsReadOnly() { return false; }
  get Types() { return this.#items.Keys; }
  get Values() { return this.#items.Values; }
  get_Item(type) { return this.#items.get_Item(type); }
  set_Item(type, value) {
    if (value == null) throw new ArgumentNullException('value');
    if (type !== value.Type) throw new ArgumentException('Dictionary and override types must agree.');
    const remove = this.#items.get_Item(type);
    if (this.#event('BeforeRemoveItem', remove) || this.#event('BeforeAddItem', value)) return;
    this.#items.set_Item(type, value);
    this.#event('AddItem', value); this.#event('RemoveItem', remove);
  }
  Add(...args) {
    const item = args.length === 2 ? new DimensionStyleOverride(args[0], args[1]) : args[0];
    if (item == null) throw new ArgumentNullException('item');
    if (this.#event('BeforeAddItem', item)) throw new ArgumentException('The override cannot be added.', 'item');
    this.#items.Add(item.Type, item); this.#event('AddItem', item);
  }
  AddRange(collection) {
    if (collection == null) throw new ArgumentNullException('collection');
    for (const item of collection) this.Add(item);
  }
  Remove(type) {
    const found = {};
    if (!this.#items.TryGetValue(type, found) || this.#event('BeforeRemoveItem', found.value)) return false;
    this.#items.Remove(type); this.#event('RemoveItem', found.value); return true;
  }
  Clear() { for (const type of Array.from(this.#items.Keys)) this.Remove(type); }
  ContainsType(type) { return this.#items.ContainsKey(type); }
  ContainsValue(value) { return this.#items.ContainsValue(value); }
  TryGetValue(type, output) { return this.#items.TryGetValue(type, output); }
  GetEnumerator() { return this.#items.GetEnumerator(); }
  [Symbol.iterator]() { return this.GetEnumerator(); }
  // Explicit IDictionary/ICollection interface adapters. The C# keyed Add ignores the supplied key.
  get Keys() { return this.#items.Keys; }
  ContainsKey(type) { return this.ContainsType(type); }
  AddKeyValue(key, value) { this.Add(value); }
  AddPair(pair) { this.Add(pair.Value); }
  RemovePair(pair) {
    if (pair.Value !== this.#items.get_Item(pair.Key)) return false;
    return this.Remove(pair.Key);
  }
  ContainsPair(pair) {
    const found = {}; return this.#items.TryGetValue(pair.Key, found) && found.value === pair.Value;
  }
  CopyTo(array, arrayIndex) { this.#items.CopyTo(array, arrayIndex); }
}
