import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, KeyNotFoundException, InvalidOperationException, NotSupportedException, RequireInteger } from './Errors.js';
/** Int32 enum keys with .NET 8 dictionary slot order, free-slot reuse and live read-only views. */
export class EnumDictionary {
  #index = new Map(); #slots = []; #free = []; #version = 0; #keys; #values;
  constructor() {}
  #canonical(key) { return RequireInteger(key, -2147483648, 2147483647, 'key'); }
  get Count() { return this.#index.size; }
  ContainsKey(key) { return this.#index.has(this.#canonical(key)); }
  TryGetValue(key, result) {
    const at = this.#index.get(this.#canonical(key));
    if (result != null) result.value = at === undefined ? null : this.#slots[at].Value;
    return at !== undefined;
  }
  get_Item(key) { const box = {}; if (!this.TryGetValue(key, box)) throw new KeyNotFoundException(key); return box.value; }
  Add(key, value) {
    const canonical = this.#canonical(key);
    if (this.#index.has(canonical)) throw new ArgumentException('An item with the same key has already been added.');
    const at = this.#free.length ? this.#free.pop() : this.#slots.length;
    this.#slots[at] = Object.freeze({ Key: key, Value: value }); this.#index.set(canonical, at); this.#version++;
  }
  set_Item(key, value) {
    const at = this.#index.get(this.#canonical(key));
    if (at === undefined) this.Add(key, value);
    else this.#slots[at] = Object.freeze({ Key: this.#slots[at].Key, Value: value });
  }
  Remove(key) {
    const canonical = this.#canonical(key), at = this.#index.get(canonical);
    if (at === undefined) return false;
    this.#index.delete(canonical); this.#slots[at] = null; this.#free.push(at); return true;
  }
  Clear() { this.#index.clear(); this.#slots.length = 0; this.#free.length = 0; }
  ContainsValue(value) { for (const item of this) if (item.Value === value || (item.Value?.Equals?.(value) ?? false)) return true; return false; }
  get Keys() { return this.#keys ??= this.#view('Key'); }
  get Values() { return this.#values ??= this.#view('Value'); }
  #view(field) {
    const owner = this;
    return Object.freeze({ get Count() { return owner.Count; }, get IsReadOnly() { return true; },
      Contains(value) { return field === 'Key' ? owner.ContainsKey(value) : owner.ContainsValue(value); },
      CopyTo(array, index = 0) { owner.CopyTo(array, index, field); },
      Add() { throw new NotSupportedException('Collection is read-only.'); },
      Clear() { throw new NotSupportedException('Collection is read-only.'); },
      Remove() { throw new NotSupportedException('Collection is read-only.'); },
      GetEnumerator() { return owner.GetEnumerator(field); }, [Symbol.iterator]() { return this.GetEnumerator(); } });
  }
  CopyTo(array, index = 0, field = null) {
    if (array == null) throw new ArgumentNullException('array');
    if (!Number.isInteger(index) || index < 0 || index > array.length) throw new ArgumentOutOfRangeException('index', index);
    if (array.length - index < this.Count) throw new ArgumentException('Destination array is not long enough.');
    for (const item of this) array[index++] = field === null ? item : item[field];
  }
  GetEnumerator(field = null) {
    const owner = this, version = this.#version; let at = 0, current = field === null ? Object.freeze({ Key: 0, Value: null }) : field === 'Key' ? 0 : null;
    const check = () => { if (version !== owner.#version) throw new InvalidOperationException('Collection was modified.'); };
    return { get Current() { return current; }, MoveNext() { check(); while (at < owner.#slots.length) { const row = owner.#slots[at++]; if (row !== null) { current = field === null ? row : row[field]; return true; } } at = owner.#slots.length + 1; current = field === null ? Object.freeze({ Key: 0, Value: null }) : field === 'Key' ? 0 : null; return false; },
      Reset() { check(); at = 0; current = field === null ? Object.freeze({ Key: 0, Value: null }) : field === 'Key' ? 0 : null; }, Dispose() {},
      next() { return this.MoveNext() ? { done: false, value: current } : { done: true, value: undefined }; }, [Symbol.iterator]() { return this; } };
  }
  [Symbol.iterator]() { return this.GetEnumerator(); }
}
