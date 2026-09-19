// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
import { CheckedCollection, CheckStoredName } from '../../runtime/CheckedCollection.js';
import { ArgumentNullException } from '../../runtime/Errors.js';
export class DxfLayerFilter extends DxfDatabaseObject {
  #names = new CheckedCollection(value => CheckStoredName(value, 'value'));
  constructor(names) {
    super('LAYER_FILTER');
    if (arguments.length) {
      if (names == null) throw new ArgumentNullException('names');
      for (const name of names) this.#names.Add(name);
    }
  }
  get LayerNames() { return this.#names; }
  CloneShell() { return new DxfLayerFilter(this.#names); }
  ValidateDatabaseSchema(database, errors) {
    if (!(this.Owner instanceof DxfDictionary)) errors.Add('LAYER_FILTER requires a dictionary owner.');
  }
}
