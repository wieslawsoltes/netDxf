// Complete original OleVersionPresenceApiTests.cs. Payload bytes follow the original OlePayload helper.
import { DxfDocument, DxfVersion, OleFrame, AciColor, Block, Insert, Matrix3, Vector3 } from '../../index.js';
import { NotSupportedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
const OlePayload=size=>Uint8Array.from({length:size},(_,i)=>i*131%256);
const singleOle=items=>{const rows=Array.from(items).filter(x=>x instanceof OleFrame);Equal(1,rows.length);return rows[0];};
export function RegisterOleVersionPresenceApiTests(){for(const v of [0,1,7,32767])Run(`oleframe/version-presence/api/${v}`,()=>OleVersionPresenceApi(v));}
export function OleVersionPresenceApi(value){
  const source=new OleFrame(OlePayload(128),value);source.Color=AciColor.Red;Check(source.HasOleVersion,'Existing constructor stopped emitting a version.');
  const doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(source);const absent=source.WithOleVersionPresence(false);
  Check(!absent.HasOleVersion&&source.HasOleVersion,'Selecting absence mutated the source.');Equal(value,absent.OleVersion,'Selecting absence modified a dormant value');Check(absent.Handle===null&&absent.Owner===null,'Copy retained database identity.');
  absent.Color=AciColor.Blue;Equal(1,source.Color.Index,'Copy changed source color');const data=absent.GetBinaryData();data[0]=77;
  Equal(OlePayload(128),source.GetBinaryData(),'Copy aliases source payload.');Equal(OlePayload(128),absent.GetBinaryData(),'Getter exposes mutable payload.');
  const clone=absent.Clone();Check(!clone.HasOleVersion,'Clone invented version metadata.');const restored=clone.WithOleVersionPresence(true);Check(restored.HasOleVersion&&!clone.HasOleVersion,'Presence restoration mutated source.');Equal(value,restored.OleVersion,'Restoration lost the dormant value');
  const block=new Block('AbsentVersion');block.Entities.Add(absent);const insert=new Insert(block),nested=insert.Clone();
  Check(!singleOle(nested.Block.Entities).HasOleVersion,'Nested clone invented a version.');Check(!singleOle(insert.Explode()).HasOleVersion,'Identity explosion invented a version.');
  Throws(NotSupportedException,()=>clone.TransformBy(Matrix3.Identity,Vector3.UnitX));Check(!clone.HasOleVersion,'Rejected transform changed presence.');
}
