// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { ObservableCollectionEventArgs } from './ObservableCollectionEventArgs.js';
import { EventHook } from '../../runtime/EventHook.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException,
  InvalidOperationException, RequireInteger } from '../../runtime/Errors.js';

// EqualityComparer<T>.Default calls a reference type's Equals even for the same
// instance. User equality can be non-reflexive (for example NaN MLINE offsets).
const equal = (a, b) => a == null || b == null ? a == null && b == null :
  typeof a.Equals === 'function' ? a.Equals(b) : a === b || (Number.isNaN(a) && Number.isNaN(b));
const defaultCompare = (a, b) => {
  if (a === b) return 0;
  if (a == null) return -1;
  if (b == null) return 1;
  if (a.CompareTo) return a.CompareTo(b);
  if (typeof a === 'number' && typeof b === 'number') return Number.isNaN(a) ? (Number.isNaN(b) ? 0 : -1) : Number.isNaN(b) ? 1 : a < b ? -1 : 1;
  if (typeof a === 'boolean' && typeof b === 'boolean') return a ? 1 : -1;
  // String default sorting depends on the .NET globalization profile. Require an
  // explicit comparer instead of quietly substituting JS ordinal/host collation.
  throw new ArgumentException('Default comparison for this type is not yet qualified; supply a comparer.');
};

/** Source event order and cancellation semantics; no Proxy or per-item event wrappers. */
export class ObservableCollection {
  #items = [];
  #version = 0;
  #defaultValue = null;
  constructor(capacity = 0, elementType = null) {
    RequireInteger(capacity, 0, 2147483647, 'capacity');
    if (['byte','short','int','long','float','double','enum'].includes(elementType)) this.#defaultValue = elementType === 'long' ? 0n : 0;
    else if (elementType === 'bool') this.#defaultValue = false;
    else if (typeof elementType === 'function') this.#defaultValue = new elementType();
    for (const name of ['BeforeAddItem', 'AddItem', 'BeforeRemoveItem', 'RemoveItem'])
      Object.defineProperty(this, name, { value: new EventHook(), enumerable: true });
  }
  get Count() { return this.#items.length; }
  get IsReadOnly() { return false; }
  #index(index, allowEnd = false) { return RequireInteger(index, 0, this.Count - (allowEnd ? 0 : 1), 'index'); }
  get_Item(index) { return Copy(this.#items[this.#index(index)]); }
  set_Item(index, value) {
    const remove = this.get_Item(index), add = Copy(value);
    if (this.OnBeforeRemoveItemEvent(remove) || this.OnBeforeAddItemEvent(add)) return;
    this.#index(index); this.#items[index] = Copy(value); this.#version++;
    this.OnAddItemEvent(add); this.OnRemoveItemEvent(remove);
  }
  OnAddItemEvent(item) { this.AddItem.Invoke(this, new ObservableCollectionEventArgs(item)); }
  OnBeforeAddItemEvent(item) { const e = new ObservableCollectionEventArgs(item); this.BeforeAddItem.Invoke(this, e); return e.Cancel; }
  OnBeforeRemoveItemEvent(item) { const e = new ObservableCollectionEventArgs(item); this.BeforeRemoveItem.Invoke(this, e); return e.Cancel; }
  OnRemoveItemEvent(item) { this.RemoveItem.Invoke(this, new ObservableCollectionEventArgs(item)); }
  Add(item) {
    item = Copy(item);
    if (this.OnBeforeAddItemEvent(item)) throw new ArgumentException('The item cannot be added to the collection.', 'item');
    this.#items.push(Copy(item)); this.#version++; this.OnAddItemEvent(item);
  }
  AddRange(collection) {
    if (collection == null) throw new ArgumentNullException('collection');
    for (const item of collection) this.Add(item);
  }
  Insert(index, item) {
    this.#index(index, true); item = Copy(item);
    if (this.OnBeforeAddItemEvent(item)) throw new ArgumentException('The item cannot be added to the collection.', 'item');
    this.#index(index, true); this.#items.splice(index, 0, Copy(item)); this.#version++; this.OnAddItemEvent(item);
  }
  Remove(item) {
    item = Copy(item);
    if (!this.Contains(item) || this.OnBeforeRemoveItemEvent(item)) return false;
    const at = this.IndexOf(item);
    if (at >= 0) { this.#items.splice(at, 1); this.#version++; }
    this.OnRemoveItemEvent(item); return true;
  }
  /** Exact overload selector where IEnumerable<T> and T are indistinguishable in JS. */
  RemoveOverload(signature, items) {
    if (signature === 'T') return this.Remove(items);
    if (signature !== 'IEnumerable<T>') throw new ArgumentException('Unknown Remove overload.', 'signature');
    if (items == null) throw new ArgumentNullException('items');
    for (const item of items) this.Remove(item);
  }
  RemoveAt(index) {
    if (!Number.isInteger(index) || index < 0 || index >= this.Count)
      throw new ArgumentOutOfRangeException(`The parameter index ${index} must be in between 0 and ${this.Count}.`);
    const remove = this.get_Item(index);
    if (this.OnBeforeRemoveItemEvent(remove)) return;
    this.#index(index); this.#items.splice(index, 1); this.#version++; this.OnRemoveItemEvent(remove);
  }
  Clear() { for (const item of this.ToArray()) this.Remove(item); }
  IndexOf(item) { return this.#items.findIndex(value => equal(value, item)); }
  Contains(item) { return this.IndexOf(item) >= 0; }
  CopyTo(array, arrayIndex = 0) {
    if (array == null) throw new ArgumentNullException('destinationArray');
    RequireInteger(arrayIndex, 0, 2147483647, 'destinationIndex');
    if (array.length - arrayIndex < this.Count) throw new ArgumentException('Destination array was not long enough.', 'destinationArray');
    for (let i = 0; i < this.Count; i++) array[arrayIndex + i] = Copy(this.#items[i]);
  }
  /** Convenience equivalent to LINQ ToArray; not counted as a source member. */
  ToArray() { return this.#items.map(Copy); }
  Reverse() { this.#items.reverse(); this.#version++; }
  Sort(...args) {
    let index = 0, count = this.Count, compare = defaultCompare;
    if (args.length === 3) {
      [index, count] = args;
      RequireInteger(index, 0, 2147483647, 'index'); RequireInteger(count, 0, 2147483647, 'count');
      if (index > this.Count - count) throw new ArgumentException('Offset and length were out of bounds for the array.');
    } else if (args.length > 1) throw new ArgumentException('No matching Sort overload.');
    const comparer = args.length === 3 ? args[2] : args[0];
    if (comparer != null) {
      if (typeof comparer === 'function') compare = comparer;
      else if (typeof comparer.Compare === 'function') compare = (a, b) => comparer.Compare(a, b);
      else throw new ArgumentException('Expected a comparison function or IComparer adapter.', 'comparer');
    }
    if (count > 1) {
      try { introSort(this.#items, index, index + count - 1, 2 * (Math.floor(Math.log2(count)) + 1), (a,b) => compare(Copy(a), Copy(b))); }
      catch (error) { throw new InvalidOperationException('Failed to compare two elements in the array.', { cause: error }); }
    }
    this.#version++;
  }
  GetEnumerator() {
    const version = this.#version, owner = this; let index = 0, current = Copy(this.#defaultValue);
    const check = () => { if (version !== owner.#version) throw new InvalidOperationException('Collection was modified; enumeration operation may not execute.'); };
    return {
      get Current() { return Copy(current); },
      MoveNext() {
        check();
        if (index < owner.Count) { current = Copy(owner.#items[index++]); return true; }
        index = owner.Count + 1; current = Copy(owner.#defaultValue); return false;
      },
      Reset() { check(); index = 0; current = Copy(owner.#defaultValue); },
      Dispose() {},
      next() { const done = !this.MoveNext(); return { done, value: done ? undefined : this.Current }; },
      [Symbol.iterator]() { return this; },
    };
  }
  [Symbol.iterator]() { return this.GetEnumerator(); }
}

// .NET ArraySortHelper-style introsort: no stability assumption, depth-limited heapsort.
function introSort(a, lo, hi, depth, compare) {
  const swap = (x,y) => { [a[x],a[y]] = [a[y],a[x]]; };
  const order = (x,y) => { if (x !== y && compare(a[x],a[y]) > 0) swap(x,y); };
  while (hi > lo) {
    const count = hi - lo + 1;
    if (count <= 16) {
      if (count === 2) { order(lo,hi); return; }
      if (count === 3) { order(lo,lo+1); order(lo,hi); order(lo+1,hi); return; }
      for (let i = lo; i < hi; i++) { const item = a[i+1]; let j = i; while (j >= lo && compare(item,a[j]) < 0) { a[j+1] = a[j]; j--; } a[j+1] = item; }
      return;
    }
    if (depth-- === 0) { heapSort(a,lo,count,compare); return; }
    const middle = lo + ((hi-lo) >> 1); order(lo,middle); order(lo,hi); order(middle,hi);
    const pivot = a[middle]; swap(middle,hi-1); let left = lo, right = hi-1;
    for (;;) {
      do { left++; if (left > hi) throw new ArgumentException('Inconsistent comparer.'); } while (compare(a[left],pivot) < 0);
      do { right--; if (right < lo) throw new ArgumentException('Inconsistent comparer.'); } while (compare(pivot,a[right]) < 0);
      if (left >= right) break;
      swap(left,right);
    }
    swap(left,hi-1); introSort(a,left+1,hi,depth,compare); hi = left-1;
  }
}
function heapSort(a,lo,count,compare) {
  const down = (i,n) => { const d=a[lo+i-1]; while(i<=Math.floor(n/2)){let child=2*i;if(child<n&&compare(a[lo+child-1],a[lo+child])<0)child++;if(compare(d,a[lo+child-1])>=0)break;a[lo+i-1]=a[lo+child-1];i=child;}a[lo+i-1]=d; };
  for(let i=Math.floor(count/2);i>=1;i--)down(i,count);
  for(let i=count;i>1;i--){[a[lo],a[lo+i-1]]=[a[lo+i-1],a[lo]];down(1,i-1);}
}
