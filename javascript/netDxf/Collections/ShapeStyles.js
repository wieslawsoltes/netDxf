// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
export class ShapeStyles extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.TextStyleTable, handle, {eventRename:true, valueMembership:true}); }
  ContainsShapeName(name) { for (const style of this.Items) if (style.ContainsShapeName(name)) return style; return null; }
}
