// Complete detached defensive-data case only. Registered graph, IO and cloning cases remain unported.
import { DxfXRecord, DxfTag } from '../../index.js';
import { ArgumentException, ArgumentNullException } from '../../runtime/Errors.js';
import { Run, Equal, Throws } from './TestHarness.js';
export function RegisterNamedObjectDatabaseTests(){Run('named-objects/defensive-data',NamedObjectDefensiveData);}
export function NamedObjectDefensiveData(){
  const bytes=Uint8Array.of(1,2,3),record=new DxfXRecord();record.Data.Add(new DxfTag(310,bytes));
  bytes[0]=90;Equal(1,record.Data.get_Item(0).Value[0],'input isolation');
  record.Data.get_Item(0).Value[0]=91;Equal(1,record.Data.get_Item(0).Value[0],'output isolation');
  Throws(ArgumentException,()=>record.Data.Add(new DxfTag(0,'LINE')));
  Throws(ArgumentException,()=>record.Data.Add(new DxfTag(5,'FF')));
  Throws(ArgumentException,()=>record.Data.Add(new DxfTag(1001,'APP')));
  Throws(ArgumentNullException,()=>record.Data.Add(null));
  Throws(ArgumentException,()=>record.Data.Add(new DxfTag(370,25)));
  Throws(ArgumentException,()=>record.Data.set_Item(0,new DxfTag(440,42)));
  Throws(ArgumentException,()=>record.Data.Add(new DxfTag(310,new Uint8Array(128))));
}
