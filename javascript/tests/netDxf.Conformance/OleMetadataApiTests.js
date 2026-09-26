// Complete detached reject-flags bodies. Registered document and IO cases remain unported.
import { Ole2Frame, OleObjectType, Ole2FrameMetadataFields, Vector3, XData, ApplicationRegistry, XDataRecord, XDataCode } from '../../index.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Equal, Check, Throws } from './TestHarness.js';
const OlePayload=size=>Uint8Array.from({length:size},(_,i)=>i*131%256);
function NewOleMetadataFrame(){
  const frame=new Ole2Frame(OlePayload(33),new Vector3(1,2,3),new Vector3(4,5,6),String.raw`Literal \U+000A`,4,OleObjectType.Static,1);
  const data=new XData(new ApplicationRegistry('OLE_META'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'independent'));frame.XData.Add(data);return frame;
}
export function RegisterOleMetadataApiTests(){for(const flags of [-1,64,-2147483648])Run(`olemetadata/api/reject/${flags}`,()=>OleMetadataRejectFlags(flags));}
export function OleMetadataRejectFlags(flags){
  const original=NewOleMetadataFrame();Throws(ArgumentOutOfRangeException,()=>original.WithMetadataFields(flags));
  Equal(Ole2FrameMetadataFields.All,original.MetadataFields,'Invalid selection mutated source');
  Check(original.GetBinaryData().every((value,i)=>value===OlePayload(33)[i]),'Invalid selection mutated data.');
}
