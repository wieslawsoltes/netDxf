// Complete detached originals only. Typed transport/document cases remain unregistered.
import {DxfSpatialIndex,DxfVbaProject} from '../../index.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException} from '../../runtime/Errors.js';
import {Run,Equal,Check,Throws,SameDoubleBits} from './TestHarness.js';
export function RegisterStoredEnvelopeTests(){
  Run('stored-envelopes/model-transactional',StoredEnvelopeModel);
  Run('stored-envelopes/model-admission-limits',StoredEnvelopeModelLimits);
}
const same=(a,b)=>a.length===b.length&&a.every((v,i)=>v===b[i]);
function* StoredThrowingChunks(){yield Uint8Array.of(9);throw new InvalidOperationException('deliberate enumeration failure');}
export function StoredEnvelopeModel(){
  const index=new DxfSpatialIndex();Equal(0,index.Timestamp,'Default timestamp');
  for(const value of [-Number.MAX_VALUE,Number.MAX_VALUE,Number.MIN_VALUE,-0,-1.25,2451544.5000000005]){
    index.Timestamp=value;SameDoubleBits(value,index.Timestamp,'Timestamp bits');
  }
  const old=index.Timestamp;
  for(const value of [NaN,-Infinity,Infinity])Throws(ArgumentOutOfRangeException,()=>{index.Timestamp=value;});
  Equal(old,index.Timestamp,'Rejected timestamp changed value');
  const project=new DxfVbaProject();Equal(0,project.DataLength,'Default VBA length');Equal(0,project.Chunks.Count,'Default physical chunks');
  const input=Uint8Array.from({length:300},(_,i)=>i);project.Data=input;input[0]=9;
  Equal(0,project.Data[0],'Data setter aliases input');Check(same(Array.from(project.Chunks,c=>c.length),[127,127,46]),'Canonical chunk sizes changed.');
  const chunks=[Uint8Array.of(0,255,1),new Uint8Array(),Uint8Array.of(7)];project.SetChunks(chunks);chunks[0][0]=88;
  const snapshot=project.Chunks;snapshot.get_Item(0)[0]=77;const bytes=project.Data;bytes[0]=66;
  Equal(0,project.Data[0],'Payload getter or chunk setter aliases data');
  Throws(ArgumentNullException,()=>{project.Data=null;});
  Throws(ArgumentNullException,()=>project.SetChunks(null));
  Throws(ArgumentException,()=>project.SetChunks([Uint8Array.of(4),null]));
  Throws(ArgumentOutOfRangeException,()=>project.SetChunks([Uint8Array.of(4),new Uint8Array(128)]));
  Throws(InvalidOperationException,()=>project.SetChunks(StoredThrowingChunks()));
  Check(same(Array.from(project.Data),[0,255,1,7])&&same(Array.from(project.Chunks,c=>c.length),[3,0,1]),'Rejected setter changed payload or chunks.');
  project.SetChunks([new Uint8Array(),new Uint8Array()]);Equal(2,project.Chunks.Count,'Empty chunks lost');
  project.Data=new Uint8Array();Equal(0,project.Chunks.Count,'Empty Data assignment must canonicalize physical chunks');
}
export function StoredEnvelopeModelLimits(){
  const project=new DxfVbaProject();
  project.Data=new Uint8Array(DxfVbaProject.MaximumDataLength);Equal(DxfVbaProject.MaximumDataLength,project.DataLength,'Exact maximum bytes');
  Throws(ArgumentOutOfRangeException,()=>{project.Data=new Uint8Array(DxfVbaProject.MaximumDataLength+1);});
  Equal(DxfVbaProject.MaximumDataLength,project.DataLength,'Oversized Data changed prior value');
  project.SetChunks(project.Chunks);Equal(DxfVbaProject.MaximumDataLength,project.DataLength,'Maximum SetChunks length');
  function* overflow(){yield* project.Chunks;yield Uint8Array.of(1);}
  Throws(ArgumentOutOfRangeException,()=>project.SetChunks(overflow()));
  Equal(DxfVbaProject.MaximumDataLength,project.DataLength,'Oversized chunk sum changed prior value');
  function* emptyChunks(count){const empty=new Uint8Array();for(let i=0;i<count;i++)yield empty;}
  project.SetChunks(emptyChunks(DxfVbaProject.MaximumChunkCount));Equal(DxfVbaProject.MaximumChunkCount,project.Chunks.Count,'Exact maximum chunks');
  Throws(ArgumentOutOfRangeException,()=>project.SetChunks(emptyChunks(DxfVbaProject.MaximumChunkCount+1)));
  Equal(DxfVbaProject.MaximumChunkCount,project.Chunks.Count,'Too many chunks changed prior sequence');
}
