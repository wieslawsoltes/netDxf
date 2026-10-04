// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { RequireInteger } from '../../runtime/Errors.js';
export class DxfTableStyleRowDataTypes {
  constructor(storedDataType,storedUnitType) {
    this.StoredDataType=RequireInteger(storedDataType,-2147483648,2147483647,'storedDataType');
    this.StoredUnitType=RequireInteger(storedUnitType,-2147483648,2147483647,'storedUnitType');Object.freeze(this);
  }
  static TryRead(tags) {
    let data=null,unit=null;
    for(const tag of tags) {
      if(tag.Code===90){if(data!==null)return null;data=tag;}
      else if(tag.Code===91){if(unit!==null)return null;unit=tag;}
    }
    return data===null||unit===null?null:new DxfTableStyleRowDataTypes(data.Value,unit.Value);
  }
}
