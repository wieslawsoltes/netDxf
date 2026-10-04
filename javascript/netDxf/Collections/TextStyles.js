// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
export class TextStyles extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.TextStyleTable, handle, {}); }
}
