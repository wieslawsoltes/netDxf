// Complete pinned OleMetadataApiTests.cs, including registered selection and output cases.
import { DxfDocument, DxfRawDocument, DxfVersion, MemoryStream, Ole2Frame, OleObjectType, Ole2FrameMetadataFields, Vector3, XData, ApplicationRegistry, XDataRecord, XDataCode } from '../../index.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Equal, Check, Throws } from './TestHarness.js';
import { OlePayload, OleSingle, OleRecords } from './Ole2FrameTests.js';
import { CheckOleMetadataPresence } from './OleMetadataTests.js';
function NewOleMetadataFrame(){
  const frame=new Ole2Frame(OlePayload(33),new Vector3(1,2,3),new Vector3(4,5,6),String.raw`Literal \U+000A`,4,OleObjectType.Static,1);
  const data=new XData(new ApplicationRegistry('OLE_META'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'independent'));frame.XData.Add(data);return frame;
}
export function RegisterOleMetadataApiTests(){for(let mask=0;mask<64;mask++)Run(`olemetadata/api/${mask}`,()=>OleMetadataApi(mask));for(const flags of [-1,64,-2147483648])Run(`olemetadata/api/reject/${flags}`,()=>OleMetadataRejectFlags(flags));}
export function OleMetadataRejectFlags(flags){
  const original=NewOleMetadataFrame();Throws(ArgumentOutOfRangeException,()=>original.WithMetadataFields(flags));
  Equal(Ole2FrameMetadataFields.All,original.MetadataFields,'Invalid selection mutated source');
  Check(original.GetBinaryData().every((value,i)=>value===OlePayload(33)[i]),'Invalid selection mutated data.');
}

export function OleMetadataApi(mask){
  const original=NewOleMetadataFrame(),owner=new DxfDocument();owner.Entities.Add(original);Equal(Ole2FrameMetadataFields.All,original.MetadataFields,'Constructor output compatibility');
  const selected=original.WithMetadataFields(mask);Equal(mask,selected.MetadataFields,'Explicit selection');Check(selected.Owner===null&&selected.Handle===null,'Metadata selection copied document identity.');
  Check(selected.Color!==original.Color&&selected.Layer!==original.Layer&&selected.XData.get_Item('OLE_META')!==original.XData.get_Item('OLE_META'),'Selection aliases common mutable state.');
  const clone=selected.Clone();Equal(selected.MetadataFields,clone.MetadataFields,'Clone changed optional field selection');Equal(original.Description,clone.Description,'Selecting absence changed dormant description');Check(original.UpperLeftCorner.Equals(clone.UpperLeftCorner),'Selecting absence changed dormant point');Equal(original.TileMode,clone.TileMode,'Selecting absence changed stored mode');
  const bytes=selected.GetBinaryData();bytes[0]^=255;Equal(OlePayload(33),original.GetBinaryData(),'Selection aliases source bytes.');Equal(OlePayload(33),selected.GetBinaryData(),'Selection aliases bytes.');
  for(const frame of [selected,clone]){const doc=new DxfDocument(DxfVersion.AutoCad2018),output=new MemoryStream();doc.Entities.Add(frame);try{Check(doc.Save(output,true),'Selected metadata save failed.');output.Position=0;CheckOleMetadataPresence(OleSingle(OleRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='OLE2FRAME')),mask);}finally{output.Dispose();}}
  const restored=selected.WithMetadataFields(Ole2FrameMetadataFields.All);Equal(original.Description,restored.Description,'Restoring fields lost dormant description');Equal(Ole2FrameMetadataFields.All,restored.MetadataFields,'Explicit restoration');Equal(Ole2FrameMetadataFields.All,original.MetadataFields,'Selection mutated source flags');
}
