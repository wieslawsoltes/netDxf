import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException } from './Errors.js';
/** Synchronous reference-element List/Collection adapter. Numeric indexers use get_Item/set_Item. */
export class ReferenceList {
  #items = []; #version = 0;
  constructor(items = []) { this.AddRange(items); }
  get Count() { return this.#items.length; }
  get length() { return this.Count; }
  get IsReadOnly() { return false; }
  #index(index, append = false) {
    if (!Number.isInteger(index) || index < 0 || index >= this.Count + (append ? 1 : 0))
      throw new ArgumentOutOfRangeException('index', index);
  }
  get_Item(index) { this.#index(index); return this.#items[index]; }
  set_Item(index, value) { this.#index(index); this.#items[index] = value; this.#version++; }
  Add(value) { this.#items.push(value); this.#version++; }
  AddRange(items) {
    if (items == null) throw new ArgumentNullException('collection');
    for (const item of items === this ? this.ToArray() : items) this.Add(item);
  }
  Insert(index, item) { this.#index(index, true); this.#items.splice(index, 0, item); this.#version++; }
  IndexOf(item) { return this.#items.findIndex(value => value === item || (value?.Equals?.(item) ?? false)); }
  Contains(item) { return this.IndexOf(item) !== -1; }
  Remove(item) { const at = this.IndexOf(item); if (at < 0) return false; this.RemoveAt(at); return true; }
  RemoveAt(index) { this.#index(index); this.#items.splice(index, 1); this.#version++; }
  Clear() { this.#items.length = 0; this.#version++; }
  CopyTo(array, arrayIndex = 0) {
    if (array == null) throw new ArgumentNullException('array');
    if (!Number.isInteger(arrayIndex) || arrayIndex < 0) throw new ArgumentOutOfRangeException('arrayIndex', arrayIndex);
    if (array.length - arrayIndex < this.Count) throw new ArgumentException('Destination array is not long enough.');
    for (let i = 0; i < this.Count; i++) array[arrayIndex + i] = this.#items[i];
  }
  ToArray() { return this.#items.slice(); }
  GetEnumerator() {
    const owner = this, version = this.#version; let at = 0, current = null;
    const check = () => { if (owner.#version !== version) throw new InvalidOperationException('Collection was modified.'); };
    return { get Current() { return current; }, MoveNext() { check(); if (at < owner.Count) { current = owner.#items[at++]; return true; } at = owner.Count + 1; current = null; return false; },
      Reset() { check(); at = 0; current = null; }, Dispose() {},
      next() { return this.MoveNext() ? { done: false, value: current } : { done: true, value: undefined }; }, [Symbol.iterator]() { return this; } };
  }
  [Symbol.iterator]() { return this.GetEnumerator(); }
}
