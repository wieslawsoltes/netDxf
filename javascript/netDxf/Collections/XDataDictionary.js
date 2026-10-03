// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { StringDictionary } from '../../runtime/StringDictionary.js';
import { EventHook } from '../../runtime/EventHook.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, RequireInteger } from '../../runtime/Errors.js';
import { ObservableCollectionEventArgs } from './ObservableCollectionEventArgs.js';
export class XDataDictionary {
  #items = new StringDictionary();
  constructor(itemsOrCapacity = 0) {
    Object.defineProperties(this, { AddAppReg: { value: new EventHook(), enumerable: true }, RemoveAppReg: { value: new EventHook(), enumerable: true } });
    if (typeof itemsOrCapacity === 'number') RequireInteger(itemsOrCapacity, 0, 2147483647, 'capacity');
    else this.AddRange(itemsOrCapacity);
  }
  get Count() { return this.#items.Count; }
  get IsReadOnly() { return false; }
  get AppIds() { return this.#items.Keys; }
  get Keys() { return this.#items.Keys; }
  get Values() { return this.#items.Values; }
  get_Item(appId) { return this.#items.get_Item(appId); }
  set_Item(appId, value) {
    if (value == null) throw new ArgumentNullException('value');
    if (!OrdinalIgnoreCaseEquals(value.ApplicationRegistry.Name, appId)) throw new ArgumentException('The extended data registry name must equal the specified appId.');
    const previous = {};
    if (this.#items.TryGetValue(appId, previous)) { if (previous.value === value) return; this.Remove(appId); }
    this.Add(value);
  }
  Add(item, dictionaryValue) {
    // Explicit IDictionary.Add(key, value) in C# intentionally ignores the key.
    if (arguments.length === 2) item = dictionaryValue;
    if (item == null) throw new ArgumentNullException('item');
    const existing = {};
    if (this.#items.TryGetValue(item.ApplicationRegistry.Name, existing)) {
      const merged = item.Container !== null && item.Container !== this ? item.CopyForRegistry(existing.value.ApplicationRegistry) : item;
      existing.value.XDataRecord.AddRange(merged.XDataRecord);
    } else {
      item = this.#acquire(item); this.#items.Add(item.ApplicationRegistry.Name, item);
      this.AddAppReg.Invoke(this, new ObservableCollectionEventArgs(item.ApplicationRegistry));
    }
  }
  AddRange(items) { if (items == null) throw new ArgumentNullException('items'); for (const item of items) this.Add(item); }
  Remove(appId) {
    if (!this.#items.ContainsKey(appId)) return false;
    const item = this.#items.get_Item(appId); this.#release(item); this.#items.Remove(appId);
    this.RemoveAppReg.Invoke(this, new ObservableCollectionEventArgs(item.ApplicationRegistry)); return true;
  }
  Clear() { for (const name of Array.from(this.AppIds)) this.Remove(name); }
  ContainsAppId(appId) { return this.#items.ContainsKey(appId); }
  ContainsKey(appId) { return this.ContainsAppId(appId); }
  ContainsValue(value) { return this.#items.ContainsValue(value); }
  TryGetValue(appId, value) { return this.#items.TryGetValue(appId, value); }
  AddPair(item) { this.Add(item.Value); }
  RemovePair(item) { return item.Value === this.get_Item(item.Key) && this.Remove(item.Key); }
  Contains(item) { const box = {}; return this.TryGetValue(item.Key, box) && box.value === item.Value; }
  CopyTo(array, arrayIndex = 0) { this.#items.CopyTo(array, arrayIndex); }
  GetEnumerator() { return this.#items.GetEnumerator(); }
  [Symbol.iterator]() { return this.GetEnumerator(); }
  #acquire(item) {
    if (item.Container !== null && item.Container !== this) item = item.CopyStoredGraph();
    item.Container = this; item.ApplicationRegistry.AttachXData(this); return item;
  }
  #release(item) { item.ApplicationRegistry.DetachXData(this); item.Container = null; }
  ReplaceForBinding(appId, value) {
    if (value == null) throw new ArgumentNullException('value');
    if (!OrdinalIgnoreCaseEquals(appId, value.ApplicationRegistry.Name)) throw new ArgumentException('An internal registry binding cannot change its name.', 'value');
    const previous = this.get_Item(appId); if (previous === value) return;
    value = this.#acquire(value); this.#release(previous); value.ApplicationRegistry.AttachXData(this); this.#items.set_Item(appId, value);
  }
  CanonicalizeApplicationRegistry(appId, registry) {
    const item = this.get_Item(appId);
    if (item.ApplicationRegistry !== registry) { this.#release(item); item.ApplicationRegistry = registry; this.#acquire(item); }
  }
  ValidateApplicationRegistryRename(registry, newName) {
    const item = {}, other = {};
    if (!this.TryGetValue(registry.Name, item) || item.value.ApplicationRegistry !== registry) throw new InvalidOperationException('The XData application registry binding is inconsistent.');
    if (this.TryGetValue(newName, other) && other.value !== item.value) throw new ArgumentException('The XData dictionary already contains the requested application name.', 'newName');
  }
  CommitApplicationRegistryRename(registry, newName) {
    const item = this.get_Item(registry.Name); this.#items.Remove(registry.Name); this.#items.Add(newName, item);
  }
}
