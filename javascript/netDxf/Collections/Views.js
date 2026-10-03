// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { UcsReferences } from './UcsReferences.js';
export class Views extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.ViewTable, handle, {rejectForeign:true, parameter:'view'}); }
  ValidateIncoming(item) { UcsReferences.ValidateXData(item,this.Owner); UcsReferences.Validate(item,this.Owner); item.ValidateLiveSection(this.Owner); }
  AfterOwner(item) { UcsReferences.Register(item); }
  CannotRemove(item) { return item.Sun !== null; }
  BeforeRemove(item) { UcsReferences.Unregister(item); }
}
