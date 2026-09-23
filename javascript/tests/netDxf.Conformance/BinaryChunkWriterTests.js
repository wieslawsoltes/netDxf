// Complete original codec cases. XData cases also require unported typed Load/Save
// and are not shortened, skipped or counted as successful original identities.
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { ArgumentException,ArgumentNullException,ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { NewCodeWriter,NewCodeReader } from '../support/CodecFactory.js';
import { Run,Check,Equal,Throws } from './TestHarness.js';
const sequence=length=>Uint8Array.from({length},(_,i)=>i);
export function RegisterBinaryChunkWriterTests(){
  for(const code of [...Array.from({length:10},(_,i)=>310+i),1004]){
    Run(`binary-output/chunk/all-byte-lengths/${code}`,()=>BinaryChunkWriterLengths(code));
    for(const length of [256,257,511,512,65535])Run(`binary-output/chunk/oversize/${code}/${length}`,()=>BinaryChunkWriterRejected(ArgumentOutOfRangeException,code,new Uint8Array(length)));
    Run(`binary-output/chunk/null/${code}`,()=>BinaryChunkWriterRejected(ArgumentNullException,code,null));
    Run(`binary-output/chunk/wrong-type/${code}`,()=>BinaryChunkWriterRejected(ArgumentException,code,'00FF'));
  }
  Run('binary-output/chunk/direct-helper',BinaryChunkWriterDirect);
}
export function BinaryChunkWriterLengths(code){
  for(let length=0;length<=255;length++){
    const value=sequence(length),stream=new MemoryStream();
    try{
      const writer=NewCodeWriter(stream,true);writer.Write(code,value);writer.Write(0,'EOF');writer.Flush();
      const raw=stream.ToArray();Equal(length,raw[24],'One-byte chunk length prefix');Equal(value,raw.slice(25,25+length),'Writer changed chunk bytes.');
      stream.Position=0;const reader=NewCodeReader(stream,true);reader.Next();Equal(code,reader.Code,'Chunk group code');Equal(value,reader.ReadBytes(),'Chunk did not round trip.');
      reader.Next();Equal('EOF',reader.ReadString(),'Chunk misaligned the following record');
    }finally{stream.Dispose();}
  }
}
export function BinaryChunkWriterRejected(Type,code,value){
  const stream=new MemoryStream();
  try{
    stream.Write(Uint8Array.of(12,34,56));const writer=NewCodeWriter(stream,true);writer.Write(1,'prior value');writer.Flush();
    const before=stream.ToArray(),position=stream.Position;Throws(Type,()=>writer.Write(code,value));
    Equal(position,stream.Position,'Invalid chunk advanced the stream');Equal(before,stream.ToArray(),'Invalid chunk wrote a partial group code or payload.');
    Equal(1,writer.Code,'Invalid chunk changed current code');Equal('prior value',writer.Value,'Invalid chunk changed current value');
    writer.Write(code,Uint8Array.of(0,255));writer.Write(0,'EOF');writer.Flush();stream.Position=3;const reader=NewCodeReader(stream,true);
    reader.Next();Equal('prior value',reader.ReadString(),'Prior value lost');reader.Next();Equal(Uint8Array.of(0,255),reader.ReadBytes(),'Writer could not recover after rejection.');
    reader.Next();Equal('EOF',reader.ReadString(),'Recovery boundary');Check(stream.CanWrite,'Rejected chunk closed caller stream.');
  }finally{stream.Dispose();}
}
export function BinaryChunkWriterDirect(){
  const stream=new MemoryStream();
  try{
    const writer=NewCodeWriter(stream,true),before=stream.ToArray();Throws(ArgumentNullException,()=>writer.WriteBytes(null));Throws(ArgumentOutOfRangeException,()=>writer.WriteBytes(new Uint8Array(256)));Equal(before,stream.ToArray(),'Direct rejected binary data changed the stream.');
    for(const length of [0,1,127,128,255]){
      stream.SetLength(0);stream.Position=0;const value=sequence(length);writer.WriteBytes(value);writer.WriteByte(42);writer.Flush();const bytes=stream.ToArray();
      Equal(length+2,bytes.length,'Direct chunk size');Equal(length,bytes[0],'Direct chunk prefix');Equal(value,bytes.slice(1,1+length),'Direct chunk content');Equal(42,bytes.at(-1),'Direct chunk terminator');
    }
  }finally{stream.Dispose();}
}
