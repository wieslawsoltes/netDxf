// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ArgumentException, InvalidOperationException } from '../../runtime/Errors.js';
export class ApplicationRegistries extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.ApplicationIdTable, handle, {cloneForeign:true, identityReferences:true}); }
  Contains(item) { return typeof item === 'string' ? super.Contains(item) : item != null && this.get_Item(item.Name) === item; }
  ValidateRecordRename(record, name) {
    if (!this.Contains(record)) throw new InvalidOperationException('The application registry has inconsistent table membership.');
    const other = this.get_Item(name);
    if (other !== null && other !== record) throw new ArgumentException('There is already another application registry with the same name.', 'newName');
  }
}
