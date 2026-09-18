// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { AcisEntity } from './AcisEntity.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
export class Body extends AcisEntity {
  constructor() { super(EntityType.Body, DxfObjectCode.Body); }
  Clone() { const copy = new Body(); this.CopyTo(copy); return copy; }
}
