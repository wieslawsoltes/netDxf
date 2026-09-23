// Complete original binary chunk reader cases, including all truncated prefixes.
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
import { EndOfStreamException } from '../../runtime/Errors.js';
import { Run,Equal,Throws } from './TestHarness.js';
class FragmentedReadStream extends MemoryStream {Read(buffer,offset,count){return super.Read(buffer,offset,Math.min(1,count));}}
export function RegisterBinaryChunkTests(){
  for(const code of [...Array.from({length:10},(_,i)=>310+i),1004]){
    for(const length of [0,1,2,127,128,254,255])Run(`binary/chunk/${code}/complete-${length}`,()=>CheckCompleteChunk(code,length,false));
    Run(`binary/chunk/${code}/all-truncated-prefixes`,()=>{for(let actual=0;actual<255;actual++){const stream=new MemoryStream(CreateChunkBytes(code,255,actual,false));try{const reader=new BinaryCodeValueReader(stream);Throws(EndOfStreamException,()=>reader.Next());}finally{stream.Dispose();}}});
    Run(`binary/chunk/${code}/missing-count`,()=>{const stream=new MemoryStream(Uint8Array.from([...BinarySentinel,code&255,code>>>8]));try{const reader=new BinaryCodeValueReader(stream);Throws(EndOfStreamException,()=>reader.Next());}finally{stream.Dispose();}});
    Run(`binary/chunk/${code}/fragmented`,()=>CheckCompleteChunk(code,255,true));
  }
}
export function CreateChunkBytes(code,declaredLength,actualLength,addEof){return Uint8Array.from([...BinarySentinel,code&255,code>>>8,declaredLength,...Array.from({length:actualLength},(_,i)=>i&255),...(addEof?[0,0,69,79,70,0]:[])]);}
export function CheckCompleteChunk(code,length,fragmented){
  const bytes=CreateChunkBytes(code,length,length,true),stream=fragmented?new FragmentedReadStream(bytes):new MemoryStream(bytes);
  try{const reader=new BinaryCodeValueReader(stream);reader.Next();const payload=reader.ReadBytes();Equal(length,payload.length,'chunk length');for(let i=0;i<length;i++)Equal(i&255,payload[i],'chunk byte');reader.Next();Equal('EOF',reader.ReadString(),'following record alignment');Equal(stream.Length,stream.Position,'total consumption');}
  finally{stream.Dispose();}
}
