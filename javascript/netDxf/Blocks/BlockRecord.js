// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
export class BlockRecord extends DxfObject {
  static DefaultUnits=0;
  Name; Layout=null; Units; AllowExploding=true; ScaleUniformly=false;
  /** C# internal constructor. Ownership is supplied by the document host. */
  constructor(name) {
    super(DxfObjectCode.BlockRecord);
    if(name==null||name==='')throw new ArgumentNullException('name');
    this.Name=name;this.Units=BlockRecord.DefaultUnits;
  }
  get IsForInternalUseOnly(){if(this.Name==null)throw new NullReferenceException();return this.Name.startsWith('*');}
  ToString(){return this.Name;}
}
