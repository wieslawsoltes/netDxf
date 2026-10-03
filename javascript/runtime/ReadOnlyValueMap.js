import { Copy } from './GeometryRuntime.js';
import { InvalidOperationException, KeyNotFoundException, NotSupportedException } from './Errors.js';
/** Small value-type dictionary with .NET insertion-slot and read-only-view semantics. */
export class ReadOnlyValueMap {
  #slots = []; #indices = new Map(); #free = []; #version = 0; #default;
  constructor(defaultValue) { this.#default = defaultValue; }
  get Count() { return this.#indices.size; }
  get IsReadOnly() { return true; }
  ContainsKey(key) { return this.#indices.has(key); }
  get_Item(key) {
    if (!this.#indices.has(key)) throw new KeyNotFoundException();
    return Copy(this.#slots[this.#indices.get(key)].Value);
  }
  TryGetValue(key, output) {
    const found = this.ContainsKey(key); output.value = found ? this.get_Item(key) : this.#default(); return found;
  }
  $set(key, value) {
    if (this.#indices.has(key)) this.#slots[this.#indices.get(key)].Value = Copy(value);
    else {
      const index = this.#free.length ? this.#free.pop() : this.#slots.length;
      this.#slots[index] = { Key: key, Value: Copy(value) }; this.#indices.set(key, index); this.#version++;
    }
  }
  $remove(key) {
    if (!this.#indices.has(key)) return false;
    const index = this.#indices.get(key); this.#indices.delete(key); this.#slots[index] = null; this.#free.push(index); return true;
  }
  Add() { throw new NotSupportedException('Collection is read-only.'); }
  Remove() { throw new NotSupportedException('Collection is read-only.'); }
  Clear() { throw new NotSupportedException('Collection is read-only.'); }
  set_Item() { throw new NotSupportedException('Collection is read-only.'); }
  GetEnumerator() {
    const self = this, version = this.#version; let index = 0, current = null;
    return {
      get Current() { if (current === null) throw new InvalidOperationException(); return { Key: current.Key, Value: Copy(current.Value) }; },
      MoveNext() {
        if (version !== self.#version) throw new InvalidOperationException('Collection was modified.');
        while (index < self.#slots.length) { const slot = self.#slots[index++]; if (slot !== null) { current = { Key: slot.Key, Value: Copy(slot.Value) }; return true; } }
        current = null; return false;
      },
      Reset() { if (version !== self.#version) throw new InvalidOperationException(); index = 0; current = null; }, Dispose() {},
      next() { return this.MoveNext() ? { done: false, value: this.Current } : { done: true }; }, [Symbol.iterator]() { return this; }
    };
  }
  [Symbol.iterator]() { return this.GetEnumerator(); }
  get Keys() { const self = this; return Object.freeze({ get Count() { return self.Count; }, *[Symbol.iterator]() { for (const pair of self) yield pair.Key; } }); }
  get Values() { const self = this; return Object.freeze({ get Count() { return self.Count; }, *[Symbol.iterator]() { for (const pair of self) yield pair.Value; } }); }
}
