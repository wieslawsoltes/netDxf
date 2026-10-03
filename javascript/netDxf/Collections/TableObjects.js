// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectReference } from '../DxfObjectReference.js';
import { ApplicationRegistry } from '../Tables/ApplicationRegistry.js';
import { GenericDictionary } from '../../runtime/GenericDictionary.js';
import { OrdinalIgnoreCaseEquals, OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { ArgumentNullException, NotSupportedException } from '../../runtime/Errors.js';
export const TableNameComparer = Object.freeze({
  Equals: OrdinalIgnoreCaseEquals,
  GetHashCode(value) { let h = 0; for (const c of OrdinalIgnoreCaseKey(value)) h = (Math.imul(h, 31) + c.codePointAt(0)) | 0; return h; },
});
/** Registered table base. The trailing constructor arguments adapt internal C# construction. */
export class TableObjects extends DxfObject {
  #list = new GenericDictionary(0, TableNameComparer); #references = new GenericDictionary(0, TableNameComparer);
  constructor(document, codeName, handle = null) {
    super(codeName);
    if (new.target === TableObjects) throw new NotSupportedException('TableObjects is abstract.');
    this.Owner = document;
    if (handle == null || handle === '') document.NumHandles = super.AssignHandle(document.NumHandles);
    else this.Handle = handle;
    document.AddedObjects.Add(this.Handle, this);
  }
  get List() { return this.#list; } // protected in C#
  get References() { return this.#references; } // internal in C#
  get Count() { return this.#list.Count; }
  get Items() { return this.#list.Values; }
  get Names() { return this.#list.Keys; }
  get_Item(name) { const result = {}; return this.#list.TryGetValue(name, result) ? result.value : null; }
  TryGetValue(name, output) { return this.#list.TryGetValue(name, output); }
  Contains(value) { return typeof value === 'string' ? this.#list.ContainsKey(value) : this.#list.ContainsValue(value); }
  HasReferences(value) {
    const item = typeof value === 'string' ? this.get_Item(value) : value;
    if (item instanceof ApplicationRegistry) return this.Owner.ApplicationRegistryReferences(item).Count !== 0;
    const name = typeof value === 'string' ? value : value.Name;
    return !this.#references.get_Item(name).IsEmpty() || (item !== null && this.Owner.MLeaderReferences(item).Count > 0);
  }
  GetReferences(value) {
    const item = typeof value === 'string' ? this.get_Item(value) : value;
    if (item instanceof ApplicationRegistry) return this.Owner.ApplicationRegistryReferences(item);
    const name = typeof value === 'string' ? value : value.Name, result = this.#references.get_Item(name).ToList();
    if (item !== null) for (const extra of this.Owner.MLeaderReferences(item)) {
      const i = Array.from(result).findIndex(r => r.Reference === extra.Reference);
      if (i < 0) result.Add(extra);
      else result.set_Item(i, new DxfObjectReference(extra.Reference, (result.get_Item(i).Uses + extra.Uses) | 0));
    }
    return result;
  }
  Add(item, assignHandle = true) { if (item == null) throw new ArgumentNullException('item'); return this.AddRecord(item, assignHandle); }
  Clear() { for (const name of Array.from(this.#list.Keys)) this.Remove(name); }
  GetEnumerator() { return this.#list.Values.GetEnumerator(); }
  [Symbol.iterator]() { return this.GetEnumerator(); }
}
