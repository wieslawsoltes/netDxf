import { Copy } from './GeometryRuntime.js';
import { ReferenceList } from './ReferenceList.js';
/** List<T> where T is a CLR value type: copy on ingress, indexing and enumeration. */
export class ValueList extends ReferenceList {
  Add(value) { super.Add(Copy(value)); }
  Insert(index, value) { super.Insert(index, Copy(value)); }
  get_Item(index) { return Copy(super.get_Item(index)); }
  set_Item(index, value) { super.set_Item(index, Copy(value)); }
  ToArray() { return super.ToArray().map(Copy); }
  CopyTo(array, arrayIndex = 0) {
    super.CopyTo(array, arrayIndex);
    for (let i = 0; i < this.Count; i++) array[arrayIndex + i] = Copy(array[arrayIndex + i]);
  }
  GetEnumerator() {
    const iterator = super.GetEnumerator();
    return { get Current() { return Copy(iterator.Current); }, MoveNext() { return iterator.MoveNext(); },
      Reset() { iterator.Reset(); }, Dispose() { iterator.Dispose(); },
      next() { return this.MoveNext() ? { done: false, value: this.Current } : { done: true, value: undefined }; },
      [Symbol.iterator]() { return this; } };
  }
}
