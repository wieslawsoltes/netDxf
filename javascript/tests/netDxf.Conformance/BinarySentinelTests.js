// Complete original BinarySentinelTests.cs; the BinaryReader wrapper is the existing stream codec adapter.
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { ArgumentNullException, EndOfStreamException, InvalidDataException } from '../../runtime/Errors.js';
import { Run, Equal, Throws } from './TestHarness.js';
export class FragmentedReadStream extends MemoryStream {
  constructor(bytes){super(bytes,false);}
  Read(buffer,offset,count){return super.Read(buffer,offset,Math.min(count,1));}
}
export function RegisterBinarySentinelTests(){
  for(let length=0;length<BinarySentinel.length;length++)Run(`binary/sentinel/truncated-${length}`,()=>{
    const stream=new MemoryStream(BinarySentinel.slice(0,length));try{Throws(EndOfStreamException,()=>new BinaryCodeValueReader(stream));}finally{stream.Dispose();}
  });
  for(let index=0;index<BinarySentinel.length;index++)Run(`binary/sentinel/corrupt-byte-${index}`,()=>{
    const bytes=BinarySentinel.slice();bytes[index]^=1;const stream=new MemoryStream(bytes);try{Throws(InvalidDataException,()=>new BinaryCodeValueReader(stream));}finally{stream.Dispose();}
  });
  Run('binary/sentinel/fragmented-stream',()=>{const stream=new FragmentedReadStream(BinarySentinel.slice());try{new BinaryCodeValueReader(stream);Equal(BinarySentinel.length,stream.Position,'sentinel consumption');}finally{stream.Dispose();}});
  Run('binary/sentinel/null-reader',()=>Throws(ArgumentNullException,()=>new BinaryCodeValueReader(null)));
  Run('binary/sentinel/no-overread',()=>{const stream=new MemoryStream(Uint8Array.from([...BinarySentinel,0x55,0xaa]));try{new BinaryCodeValueReader(stream);Equal(0x55,stream.ReadByte(),'next byte after sentinel');}finally{stream.Dispose();}});
}
