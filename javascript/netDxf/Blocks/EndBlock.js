// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
/** Internal block termination record; does not allocate a document handle. */
export class EndBlock extends DxfObject {
  constructor(owner) { super(DxfObjectCode.BlockEnd); this.Owner=owner; }
}
