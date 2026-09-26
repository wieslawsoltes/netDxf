// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { TableSnapshot } from '../../runtime/TablePayload.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
export class DxfTableStyleBorderValues {
  constructor(storedLineweight,isVisible,storedColor) {
    this.StoredLineweight=RequireInteger(storedLineweight,-32768,32767,'storedLineweight');
    this.IsVisible=isVisible;this.StoredColor=RequireInteger(storedColor,-32768,32767,'storedColor');Object.freeze(this);
  }
}
export class DxfTableStyleRowBorders {
  static get BorderCount(){return 6;}
  constructor(values) {
    if(values==null)throw new ArgumentNullException('values');
    const snapshot=[];
    ConsumeManagedEnumerable(values,value=>{
      if(snapshot.length===6)throw new ArgumentException('Exactly six border values are required.','values');
      if(value==null)throw new ArgumentException('A border value cannot be null.','values');snapshot.push(value);
    });
    if(snapshot.length!==6)throw new ArgumentException('Exactly six border values are required.','values');
    this.Values=TableSnapshot(snapshot);Object.freeze(this);
  }
  WithBorder(index,value) {
    if(index<0||index>=6||!Number.isInteger(index))throw new ArgumentOutOfRangeException('index');
    if(value==null)throw new ArgumentNullException('value');
    const snapshot=Array.from(this.Values);snapshot[index]=value;return new DxfTableStyleRowBorders(snapshot);
  }
  static TryRead(tags) {
    const fields=Array(18).fill(null);
    for(const tag of tags) {
      const c=tag.Code,slot=c>=274&&c<=279?c-274:c>=284&&c<=289?c-284+6:c>=64&&c<=69?c-64+12:-1;
      if(slot<0)continue;if(fields[slot]!==null)return null;fields[slot]=tag;
    }
    const values=[];
    for(let i=0;i<6;i++) {
      if(fields[i]===null||fields[i+6]===null||fields[i+12]===null)return null;
      const flag=fields[i+6].Value;if(flag!==0&&flag!==1)return null;
      values.push(new DxfTableStyleBorderValues(fields[i].Value,flag!==0,fields[i+12].Value));
    }
    return new DxfTableStyleRowBorders(values);
  }
}
