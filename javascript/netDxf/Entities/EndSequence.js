// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
/** Original internal SEQEND record. Handle allocation is performed by its owner. */
export class EndSequence extends DxfObject {
  constructor() { super(DxfObjectCode.EndSequence); this.Owner = null; }
}
