// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { CheckedCollection } from '../../runtime/CheckedCollection.js';
import { IsDatabaseModel } from '../../runtime/DatabaseModel.js';
import { ArgumentException } from '../../runtime/Errors.js';
export class DxfIdBuffer extends DxfDatabaseObject {
  #references;
  constructor() {
    super('IDBUFFER');
    this.#references = new CheckedCollection(target => {
      if (IsDatabaseModel(target, 'DxfDocument')) throw new ArgumentException('Use null for an IDBUFFER null handle.', 'target');
      if (target != null && this.Database != null) this.Database.CheckRegistered(target);
    });
  }
  get References() { return this.#references; }
  get DatabaseReferences() { return this.#references; }
  CloneShell() { return new DxfIdBuffer(); }
  CopyDatabaseReferencesTo(clone, resolve) {
    // Null entries are passed to the resolver too, exactly as in the original method.
    for (const target of this.#references) clone.References.Add(resolve(target));
  }
}
