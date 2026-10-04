// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { UcsReferences } from './UcsReferences.js';
export class UCSs extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.UcsTable, handle, {rejectForeign:true, parameter:'ucs', identityReferences:true}); }
  ValidateIncoming(item) { UcsReferences.ValidateXData(item,this.Owner); UcsReferences.Validate(item,this.Owner); }
  AfterOwner(item) { UcsReferences.Register(item); }
  BeforeRemove(item) { UcsReferences.Unregister(item); }
}
