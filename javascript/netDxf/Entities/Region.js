// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { AcisEntity } from './AcisEntity.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
export class Region extends AcisEntity {
  constructor() { super(EntityType.Region, DxfObjectCode.Region); }
  Clone() { const copy = new Region(); this.CopyTo(copy); return copy; }
}
