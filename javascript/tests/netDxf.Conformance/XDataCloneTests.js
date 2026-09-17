// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Standalone original cases. Entity/layer/block/document cases remain unported, not skipped.
import { ApplicationRegistry, XData, XDataRecord, XDataCode } from '../../index.js';
import { Run, Check, Equal } from './TestHarness.js';
const CloneApplication = 'DXF_CLONE_CONFORMANCE';
export function RegisterXDataCloneTests() {
  for (const length of [0,1,127]) Run(`xdata/clone/binary-length-${length}`,()=>CheckBinaryClone(length));
  Run('xdata/clone/shared-input-records',CheckSharedBinaryClone);
  Run('xdata/clone/scalar-records',CheckScalarClone);
}
export function BinaryXData(bytes) { const data=new XData(new ApplicationRegistry(CloneApplication));data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,bytes));return data; }
export function BinaryValue(data,index=0){return data.XDataRecord.get_Item(index).Value;}
export function CheckBinaryClone(length){
  const bytes=Uint8Array.from({length},(_,i)=>i),source=BinaryXData(bytes),copy=source.Clone(),sibling=source.Clone();
  Equal(source.ApplicationRegistry.Name,copy.ApplicationRegistry.Name);
  Check(source.ApplicationRegistry!==copy.ApplicationRegistry);Check(source.XDataRecord.get_Item(0)!==copy.XDataRecord.get_Item(0));
  Check(bytes!==BinaryValue(copy));Check(BinaryValue(copy)!==BinaryValue(sibling));Equal(bytes,BinaryValue(copy));
  if(length){bytes[0]=200;Equal(0,BinaryValue(copy)[0]);BinaryValue(copy)[0]=150;Equal(200,bytes[0]);Equal(0,BinaryValue(sibling)[0]);}
  copy.XDataRecord.Clear();Equal(1,source.XDataRecord.Count);
}
export function CheckSharedBinaryClone(){
  const bytes=Uint8Array.of(1,2,3),source=BinaryXData(bytes);source.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,bytes));const copy=source.Clone();
  Check(BinaryValue(copy,0)!==bytes);Check(BinaryValue(copy,1)!==bytes);BinaryValue(copy,0)[0]=77;Equal(1,bytes[0]);
}
export function CheckScalarClone(){
  const source=new XData(new ApplicationRegistry(CloneApplication));
  for(const code of Object.values(XDataCode)){
    if(code===XDataCode.AppReg||code===XDataCode.BinaryData)continue;
    const value=code===XDataCode.ControlString?'{':code===XDataCode.DatabaseHandle?'ABC123':code===XDataCode.String?'Zażółć gęślą jaźń':code===XDataCode.LayerName?'Geometry':code===XDataCode.Int16?-42:code===XDataCode.Int32?-123456:12.75;
    source.XDataRecord.Add(new XDataRecord(code,value));
  }
  const copy=source.Clone();Equal(source.XDataRecord.Count,copy.XDataRecord.Count);
  for(let i=0;i<source.XDataRecord.Count;i++){const a=source.XDataRecord.get_Item(i),b=copy.XDataRecord.get_Item(i);Equal(a.Code,b.Code);Equal(typeof a.Value,typeof b.Value);Equal(a.Value,b.Value);}
}
