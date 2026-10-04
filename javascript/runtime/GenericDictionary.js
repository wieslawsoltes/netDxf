// .NET dictionary adaptation for the hand-ported generic collection APIs.
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Prime sizing adapts .NET Foundation MIT-licensed HashHelpers; see THIRD_PARTY_NOTICES.md.
import { Copy, CopyValue, DoubleHash } from './GeometryRuntime.js';
import { BoxedScalar } from './BoxedScalar.js';
import { BoxedString } from './BoxedString.js';
import { BoxedBoolean } from './BoxedBoolean.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException,
  KeyNotFoundException, InvalidOperationException, NotSupportedException, RequireInteger } from './Errors.js';

export function DefaultValue(type) {
  if (type === 'long') return 0n;
  if (type === 'char') return '\0';
  if (type === 'bool') return false;
  if (['byte', 'short', 'int', 'float', 'double', 'enum'].includes(type)) return 0;
  if (typeof type === 'function' && type.prototype[CopyValue]) return new type();
  return null;
}
export function ValueEquals(left, right) {
  if (left == null || right == null) return left == null && right == null;
  if ((typeof left === 'string' || left instanceof BoxedString) && (typeof right === 'string' || right instanceof BoxedString))
    return (left instanceof BoxedString ? left.Value : left) === (right instanceof BoxedString ? right.Value : right);
  if (left instanceof BoxedScalar || right instanceof BoxedScalar)
    return left instanceof BoxedScalar && right instanceof BoxedScalar && left.Type === right.Type && ValueEquals(left.Value, right.Value);
  // Do not short-circuit reference equality: source Equals can be non-reflexive.
  return typeof left.Equals === 'function' ? left.Equals(right) :
    left === right || (typeof left === 'number' && typeof right === 'number' && Number.isNaN(left) && Number.isNaN(right));
}
export function ReferenceEquals(left, right, type = null) {
  if (DefaultValue(type) !== null || left?.[CopyValue] || right?.[CopyValue]) return false;
  if (left == null || right == null) return left == null && right == null;
  // Primitive nonempty strings cannot carry CLR reference identity. Use a shared
  // BoxedString for reference-sensitive string/object APIs; empty strings are shared.
  if (typeof left !== 'object' && typeof left !== 'function' || typeof right !== 'object' && typeof right !== 'function')
    return (left === '' || left instanceof BoxedString && left.Value === '') &&
      (right === '' || right instanceof BoxedString && right.Value === '');
  return left === right;
}
export function KeyValuePair(key, value) {
  key = Copy(key); value = Copy(value);
  return Object.freeze({ get Key() { return Copy(key); }, get Value() { return Copy(value); } });
}
const identities = new WeakMap(); let nextIdentity = 1;
function hash(value) {
  if (value instanceof BoxedString || value instanceof BoxedBoolean || value instanceof BoxedScalar) return hash(value.Value);
  if (typeof value.GetHashCode === 'function') return value.GetHashCode() | 0;
  if (typeof value === 'number') return DoubleHash(value);
  if (typeof value === 'bigint') return Number(BigInt.asIntN(32, value ^ (value >> 32n)));
  if (typeof value === 'boolean') return value ? 1 : 0;
  if (typeof value === 'string') { let h = 0; for (let i = 0; i < value.length; i++) h = (Math.imul(h, 31) + value.charCodeAt(i)) | 0; return h; }
  if (typeof value !== 'object' && typeof value !== 'function') throw new ArgumentException('Unsupported dictionary key type.', 'key');
  if (!identities.has(value)) identities.set(value, nextIdentity++ | 0);
  return identities.get(value);
}
// Match .NET's growth points so collision-chain order is retained after resize.
const primes = [3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239,
  293, 353, 431, 521, 631, 761, 919, 1103, 1327, 1597, 1931, 2333, 2801, 3371,
  4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519, 21023, 25229, 30293,
  36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437, 187751, 225307,
  270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
  1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369];
function capacityFor(minimum) {
  for (const prime of primes) if (prime >= minimum) return prime;
  for (let n = minimum | 1; n < 2147483647; n += 2) {
    let prime = (n - 1) % 101 !== 0;
    for (let d = 3; prime && d * d <= n; d += 2) if (n % d === 0) prime = false;
    if (prime) return n;
  }
  return minimum;
}
/** Internal dictionary storage. Keys/values are live read-only views; stored structs are copied. */
export class GenericDictionary {
  #slots = []; #free = []; #buckets = new Map(); #version = 0; #count = 0; #capacity;
  #comparer; #keys; #values; #keyType; #valueType;
  constructor(capacity = 0, comparer = null, keyType = null, valueType = null) {
    RequireInteger(capacity, 0, 2147483647, 'capacity');
    if (comparer != null && (typeof comparer.Equals !== 'function' || typeof comparer.GetHashCode !== 'function'))
      throw new ArgumentException('An IEqualityComparer needs Equals and GetHashCode.', 'comparer');
    this.#capacity = capacity ? capacityFor(capacity) : 0;
    this.#comparer = comparer; this.#keyType = keyType; this.#valueType = valueType;
  }
  get Count() { return this.#count; }
  #hash(key) {
    if (key == null) throw new ArgumentNullException('key');
    return this.#comparer ? this.#comparer.GetHashCode(Copy(key)) | 0 : hash(key);
  }
  #find(key, code) {
    for (const at of this.#buckets.get(code) ?? []) {
      const row = this.#slots[at];
      if (this.#comparer ? this.#comparer.Equals(Copy(row.Key), Copy(key)) : ValueEquals(row.Key, key)) return at;
    }
    return -1;
  }
  #lookup(key) {
    if (key == null) throw new ArgumentNullException('key');
    return this.#capacity === 0 ? -1 : this.#find(key, this.#hash(key));
  }
  ContainsKey(key) { return this.#lookup(Copy(key)) >= 0; }
  TryGetValue(key, result) {
    key = Copy(key); const at = this.#lookup(key);
    if (result != null) result.value = at < 0 ? DefaultValue(this.#valueType) : Copy(this.#slots[at].Value);
    return at >= 0;
  }
  get_Item(key) {
    const box = {}; if (!this.TryGetValue(key, box)) throw new KeyNotFoundException(); return box.value;
  }
  #insert(key, value, replace) {
    key = Copy(key); value = Copy(value);
    if (key == null) throw new ArgumentNullException('key');
    if (this.#capacity === 0) this.#capacity = capacityFor(0);
    const code = this.#hash(key), found = this.#find(key, code);
    if (found >= 0) {
      if (!replace) throw new ArgumentException('An item with the same key has already been added.');
      this.#slots[found].Value = value; return;
    }
    if (this.#free.length === 0 && this.#slots.length === this.#capacity) {
      this.#capacity = capacityFor(Math.max(3, 2 * this.#capacity));
      this.#buckets.clear();
      for (let at = 0; at < this.#slots.length; at++) {
        const row = this.#slots[at];
        if (row !== null) { const chain = this.#buckets.get(row.hash) ?? []; chain.unshift(at); this.#buckets.set(row.hash, chain); }
      }
    }
    const at = this.#free.length ? this.#free.pop() : this.#slots.length;
    this.#slots[at] = { Key: key, Value: value, hash: code };
    const chain = this.#buckets.get(code) ?? []; chain.unshift(at); this.#buckets.set(code, chain);
    this.#count++; this.#version++;
  }
  Add(key, value) { this.#insert(key, value, false); }
  set_Item(key, value) { this.#insert(key, value, true); }
  Remove(key) {
    key = Copy(key);
    if (key == null) throw new ArgumentNullException('key');
    if (this.#capacity === 0) return false;
    const code = this.#hash(key), at = this.#find(key, code);
    if (at < 0) return false;
    const chain = this.#buckets.get(code); chain.splice(chain.indexOf(at), 1);
    if (chain.length === 0) this.#buckets.delete(code);
    this.#slots[at] = null; this.#free.push(at); this.#count--; return true;
  }
  Clear() { this.#slots.length = 0; this.#free.length = 0; this.#buckets.clear(); this.#count = 0; }
  ContainsValue(value) { for (const row of this.#slots) if (row !== null && ValueEquals(row.Value, value)) return true; return false; }
  ContainsPair(pair) { const box = {}; return this.TryGetValue(pair.Key, box) && ValueEquals(box.value, pair.Value); }
  get Keys() { return this.#keys ??= this.#view('Key'); }
  get Values() { return this.#values ??= this.#view('Value'); }
  #view(field) {
    const owner = this;
    return Object.freeze({ get Count() { return owner.Count; }, get IsReadOnly() { return true; },
      Contains(value) { return field === 'Key' ? owner.ContainsKey(value) : owner.ContainsValue(value); },
      CopyTo(array, index = 0) { owner.CopyTo(array, index, field); },
      Add() { throw new NotSupportedException('Collection is read-only.'); },
      Remove() { throw new NotSupportedException('Collection is read-only.'); },
      Clear() { throw new NotSupportedException('Collection is read-only.'); },
      GetEnumerator() { return owner.GetEnumerator(field); }, [Symbol.iterator]() { return this.GetEnumerator(); } });
  }
  CopyTo(array, index = 0, field = null) {
    if (array == null) throw new ArgumentNullException('array');
    if (!Number.isInteger(index) || index < 0 || index > array.length) throw new ArgumentOutOfRangeException('index', index);
    if (array.length - index < this.Count) throw new ArgumentException('Destination array is not long enough.');
    for (const row of this) array[index++] = field === null ? row : row[field];
  }
  GetEnumerator(field = null) {
    // ICollection<T> exposes the BCL's empty singleton for an empty view. Unlike
    // the concrete enumerator, its Current throws and subsequent additions do
    // not invalidate it. The main dictionary always returns a real enumerator.
    if (field !== null && this.Count === 0) return {
      get Current() { throw new InvalidOperationException('Enumeration has not started or has already finished.'); },
      MoveNext() { return false; }, Reset() {}, Dispose() {},
      next() { return { done: true, value: undefined }; }, [Symbol.iterator]() { return this; }
    };
    const owner = this, version = this.#version;
    const empty = () => field === null ? KeyValuePair(DefaultValue(owner.#keyType), DefaultValue(owner.#valueType)) : DefaultValue(field === 'Key' ? owner.#keyType : owner.#valueType);
    let at = 0, current = empty();
    const check = () => { if (version !== owner.#version) throw new InvalidOperationException('Collection was modified.'); };
    return { get Current() { return Copy(current); },
      MoveNext() {
        check();
        while (at < owner.#slots.length) { const row = owner.#slots[at++]; if (row !== null) { current = field === null ? KeyValuePair(row.Key, row.Value) : Copy(row[field]); return true; } }
        at = owner.#slots.length + 1; current = empty(); return false;
      },
      Reset() { check(); at = 0; current = empty(); }, Dispose() {},
      next() { return this.MoveNext() ? { done: false, value: this.Current } : { done: true, value: undefined }; },
      [Symbol.iterator]() { return this; } };
  }
  [Symbol.iterator]() { return this.GetEnumerator(); }
}
