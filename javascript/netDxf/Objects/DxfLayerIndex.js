// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
import { CheckStoredName } from '../../runtime/CheckedCollection.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { IsAncestor, ReadOnlyReferenceView } from '../../runtime/DatabaseModel.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException } from '../../runtime/Errors.js';
export class DxfLayerIndexEntry {
  constructor(layerName, buffer) {
    CheckStoredName(layerName, 'layerName');
    if (buffer == null) throw new ArgumentNullException('buffer');
    Object.defineProperties(this, { LayerName: { value: layerName, enumerable: true }, Buffer: { value: buffer, enumerable: true } });
    Object.freeze(this);
  }
  get Count() { return this.Buffer.References.Count; }
}
export class DxfLayerIndex extends DxfDatabaseObject {
  #timestamp = 0; #entries = new ReferenceList();
  constructor() { super('LAYER_INDEX'); }
  get Timestamp() { return this.#timestamp; }
  set Timestamp(value) { if (!Number.isFinite(value)) throw new ArgumentOutOfRangeException('value'); this.#timestamp = value; }
  get Entries() { return ReadOnlyReferenceView(this.#entries); }
  SetEntries(values) { this.#setEntries(values, false); }
  LoadEntries(values) { this.#setEntries(values, true); }
  #setEntries(values, loading) {
    if (values == null) throw new ArgumentNullException('values');
    const snapshot = Array.from(values);
    if (this.IsErased) throw new InvalidOperationException('An erased index cannot adopt objects.');
    const children = new Set();
    for (const entry of snapshot) {
      if (entry == null) throw new ArgumentException('A layer-index entry cannot be null.', 'values');
      const child = entry.Buffer;
      if (children.has(child)) throw new ArgumentException('Each entry must own a distinct IDBUFFER.', 'values');
      children.add(child);
      if (child.IsErased) throw new InvalidOperationException('An erased buffer cannot be attached again.');
      if (child.Owner != null && child.Owner !== this) throw new ArgumentException('An IDBUFFER already has another owner.', 'values');
      if (child.Database !== this.Database) throw new ArgumentException('Index and buffers must share a database and registration state.', 'values');
      if (IsAncestor(child, this)) throw new ArgumentException('Layer-index ownership cannot form a cycle.', 'values');
      if (this.Database != null) {
        this.Database.CheckRegistered(this); this.Database.CheckRegistered(child);
        if (child.Owner !== this) throw new ArgumentException('A registered buffer must already belong to this index.', 'values');
      }
    }
    if (this.Database != null && !loading) {
      const before = new Set(Array.from(this.#entries, entry => entry.Buffer));
      if (children.size !== before.size || [...children].some(child => !before.has(child)))
        throw new InvalidOperationException('Registered entries must preserve their complete owned buffer set.');
    }
    for (const entry of this.#entries) if (!children.has(entry.Buffer)) entry.Buffer.Owner = null;
    for (const child of children) child.Owner = this;
    this.#entries = new ReferenceList(snapshot);
  }
  get DeclaredOwnedObjects() { return Array.from(this.#entries, entry => entry.Buffer); }
  get DatabaseReferences() { return this.DeclaredOwnedObjects; }
  CloneShell() { const copy = new DxfLayerIndex(); copy.Timestamp = this.Timestamp; return copy; }
  CopyDatabaseReferencesTo(clone, resolve) {
    clone.SetEntries(Array.from(this.#entries, entry => new DxfLayerIndexEntry(entry.LayerName, resolve(entry.Buffer))));
  }
  ValidateDatabaseSchema(database, errors) {
    if (this.Database != null && !(this.Owner instanceof DxfDictionary)) errors.Add('LAYER_INDEX requires a dictionary owner.');
    if ([...this.#entries].some(entry => entry.Buffer.Owner !== this)) errors.Add('LAYER_INDEX ownership is not reciprocal.');
    if (new Set(Array.from(this.#entries, entry => entry.Buffer)).size !== this.#entries.Count) errors.Add('LAYER_INDEX repeats an owned IDBUFFER.');
  }
}
