// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Original standalone graph-clone case; document registration cases remain missing.
import { ApplicationRegistry, XData, XDataRecord, XDataCode } from '../../index.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
export function RegisterAppIdXDataLifecycleTests(){Run('appid-lifecycle/clone-names-and-cycles',AppIdNamedClone);}
export function AppIdData(registry,value=1){const data=new XData(registry);data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(value,2)));return data;}
export function AppIdNamedClone(){
  const root=new ApplicationRegistry('ROOT'),peer=new ApplicationRegistry('PEER');root.XData.Add(AppIdData(root));root.XData.Add(AppIdData(peer,3));peer.XData.Add(AppIdData(root,5));
  const copy=root.Clone('RENAMED');Equal('RENAMED',copy.Name);Check(copy.XData.get_Item('RENAMED').ApplicationRegistry===copy);
  const copiedPeer=copy.XData.get_Item('PEER').ApplicationRegistry;
  Check(copiedPeer!==peer&&copiedPeer.XData.get_Item('RENAMED').ApplicationRegistry===copy);
  copiedPeer.XData.get_Item('RENAMED').XDataRecord.get_Item(0).Value[0]=99;Equal(5,peer.XData.get_Item('ROOT').XDataRecord.get_Item(0).Value[0]);
  Equal('ROOT',root.Name);Check(root.XData.get_Item('ROOT').ApplicationRegistry===root);Throws(ArgumentException,()=>root.Clone('PEER'));Equal('ROOT',root.Name);
  const reserved=new ApplicationRegistry(ApplicationRegistry.DefaultName),custom=reserved.Clone('CUSTOM');Equal('CUSTOM',custom.Name);Check(!custom.IsReserved);
}
