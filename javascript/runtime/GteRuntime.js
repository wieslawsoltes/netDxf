// Native language adapters used by the source-derived GTE numerical modules.
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as Errors from './Errors.js';
import { FixedArray } from './FixedArray.js';
import { Copy, GetElement, SetElement } from './GeometryRuntime.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException,
  NullReferenceException, OverflowException, InvalidOperationException } from './Errors.js';

export function GteReference(value) {
  if (value == null) throw new NullReferenceException();
  return value;
}
export function GteInvoke(callback, ...args) { return GteReference(callback)(...args); }
export function GteArray(length, factory = () => 0) {
  if (!Number.isInteger(length) || length < 0) throw new OverflowException();
  return FixedArray(Array.from({length}, factory));
}
export function GteCopyTo(source, destination, index) {
  GteReference(source);
  if (destination == null) throw new ArgumentNullException('destinationArray');
  if (!Number.isInteger(index) || index < 0) throw new ArgumentOutOfRangeException('destinationIndex');
  if (index > destination.length || source.length > destination.length - index)
    throw new ArgumentException('Destination array was not long enough.', 'destinationArray');
  const values = Array.from(source, Copy); // CopyTo is overlap-safe.
  for (let i = 0; i < values.length; i++) SetElement(destination, index + i, values[i]);
}
const hashes = new WeakMap(); let nextHash = 1;
// CLR object hashes are process-specific. Preserve identity and signed Int32 shape,
// never substitute a deterministic content hash for source array identity.
export function GteHash(value) {
  GteReference(value);
  if (!hashes.has(value)) hashes.set(value, nextHash++ | 0);
  return hashes.get(value);
}
export function GteRef(read, write) { return {get value() { return read(); }, set value(v) { write(v); }}; }
export function GteElementRef(array, indices, indexer) {
  GteReference(array);
  return indexer ? GteRef(() => array.get_Item(...indices), value => array.set_Item(...indices, value)) :
    GteRef(() => GetElement(array, indices[0]), value => SetElement(array, indices[0], value));
}

export function GteFirst(values) {
  const iterator = GteReference(values)[Symbol.iterator]();
  const next = iterator.next();
  iterator.return?.();
  if (next.done) throw new InvalidOperationException('Sequence contains no elements.');
  return next.value;
}
export function GteLast(values) {
  let found = false, last;
  for (const value of GteReference(values)) { found = true; last = value; }
  if (!found) throw new InvalidOperationException('Sequence contains no elements.');
  return last;
}
const compareDouble = (a, b) => a === b || Number.isNaN(a) && Number.isNaN(b) ? 0 :
  Number.isNaN(a) ? -1 : Number.isNaN(b) ? 1 : a < b ? -1 : 1;
/** SortedDictionary<double,int> adapter used by the polynomial solver. */
export class GteSortedDictionary {
  #entries = []; #version = 0;
  get Count() { return this.#entries.length; }
  get length() { return this.Count; }
  #find(key) { return this.#entries.findIndex(entry => compareDouble(entry.Key, key) === 0); }
  ContainsKey(key) { return this.#find(key) !== -1; }
  Add(key, value) {
    if (this.ContainsKey(key)) throw new ArgumentException('An item with the same key has already been added.');
    this.#entries.push(Object.freeze({Key: key, Value: value}));
    this.#entries.sort((a, b) => compareDouble(a.Key, b.Key)); this.#version++;
  }
  get_Item(key) {
    const i = this.#find(key);
    if (i < 0) throw new Errors.KeyNotFoundException();
    return this.#entries[i].Value;
  }
  set_Item(key, value) {
    const i = this.#find(key);
    if (i < 0) this.Add(key, value);
    else { this.#entries[i] = Object.freeze({Key: this.#entries[i].Key, Value: value}); this.#version++; }
  }
  [Symbol.iterator]() {
    const version = this.#version; let index = 0;
    return {next: () => {
      if (version !== this.#version) throw new InvalidOperationException('Collection was modified.');
      return index < this.Count ? {value: this.#entries[index++], done: false} : {done: true};
    }};
  }
}
