// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
export class DxfSpatialIndex extends DxfDatabaseObject {
  #timestamp = 0;
  constructor() { super('SPATIAL_INDEX'); }
  get Timestamp() { return this.#timestamp; }
  set Timestamp(value) { if (!Number.isFinite(value)) throw new ArgumentOutOfRangeException('value'); this.#timestamp = value; }
  CloneShell() { const copy = new DxfSpatialIndex(); copy.Timestamp = this.Timestamp; return copy; }
}
