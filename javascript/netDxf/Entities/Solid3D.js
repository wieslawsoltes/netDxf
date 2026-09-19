// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { AcisEntity } from './AcisEntity.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export class Solid3D extends AcisEntity {
  constructor() { super(EntityType.Solid3D, DxfObjectCode.Solid3D); }
  #history = null;
  get HistoryHandle() { return this.#history; }
  set HistoryHandle(value) {
    if (value !== null && value !== '0') throw new NotSupportedException('Live ACIS history requires a complete modeler graph.');
    this.#history = value;
  }
  Clone() { const copy = new Solid3D(); this.CopyTo(copy); copy.HistoryHandle = this.HistoryHandle; return copy; }
}
