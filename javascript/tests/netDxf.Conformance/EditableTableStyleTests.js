// Complete original constructor case. Typed file/round-trip cases remain unported.
import { DxfTableStyleHeader, DxfTableStyleRowValues } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Throws } from './TestHarness.js';
export function RegisterEditableTableStyleTests(){Run('table-style-edit/constructors',EditableStyleConstructors);}
export function EditedStyleHeader(description='Edited Żółć \\U+0041 😀'){return new DxfTableStyleHeader(description,1,-7,2.125,3.25,true,false);}
export function EditableStyleConstructors(){
  Throws(ArgumentNullException,()=>EditedStyleHeader(null));
  for(const value of ['nul\0','\ud800','\udc00','A\ud800B','a'.repeat(256)])Throws(ArgumentException,()=>EditedStyleHeader(value));
  for(const value of [NaN,Infinity,-Infinity,-1]){
    Throws(ArgumentOutOfRangeException,()=>new DxfTableStyleHeader('',0,0,value,0,false,false));
    Throws(ArgumentOutOfRangeException,()=>new DxfTableStyleHeader('',0,0,0,value,false,false));
    Throws(ArgumentOutOfRangeException,()=>new DxfTableStyleRowValues(value,0,0,0,false));
  }
  Throws(ArgumentOutOfRangeException,()=>new DxfTableStyleHeader('',-1,0,0,0,false,false));
  Throws(ArgumentOutOfRangeException,()=>new DxfTableStyleHeader('',2,0,0,0,false,false));
}
