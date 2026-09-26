import { NullReferenceException } from '../../runtime/Errors.js';
// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { XDataCode } from '../XDataCode.js';
import { XDataRecord } from '../XDataRecord.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
/** Preserve opaque records and nested control strings when changing public HATCH metadata. */
export class HatchPatternXData {
  static FindOrigin(records) {
    if (records === null) throw new NullReferenceException();
    let depth = 0;
    for (let index = 0; index < records.Count; index++) {
      const record = records.get_Item(index);
      if (record.Code === XDataCode.ControlString) {
        if (record.Value === '{') depth++;
        else if (depth > 0) depth--;
      } else if (depth === 0 && record.Code === XDataCode.RealX && index + 2 < records.Count
        && records.get_Item(index + 1).Code === XDataCode.RealY && records.get_Item(index + 2).Code === XDataCode.RealZ)
        return index;
    }
    return -1;
  }
  static WithOrigin(records, origin) {
    const result = new ReferenceList(records ?? []), index = this.FindOrigin(result);
    const tuple = [new XDataRecord(XDataCode.RealX, origin.X), new XDataRecord(XDataCode.RealY, origin.Y),
      new XDataRecord(XDataCode.RealZ, 0)];
    if (index >= 0) for (let offset = 0; offset < 3; offset++) result.set_Item(index + offset, tuple[offset]);
    else for (let offset = 0; offset < 3; offset++) result.Insert(offset, tuple[offset]);
    return result;
  }
  static WithColorIndex(records, colorIndex) {
    const result = new ReferenceList(records ?? []), value = new XDataRecord(XDataCode.Int16, colorIndex);
    if (result.Count !== 0 && result.get_Item(0).Code === XDataCode.Int16) result.set_Item(0, value);
    else result.Insert(0, value);
    return result;
  }
}
