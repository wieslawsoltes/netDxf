// Port of pinned StrictBinaryValueTests.cs. DataView supplies explicit CLR wire widths.
import { DxfDocument, MemoryStream } from '../../index.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
import { EndOfStreamException, InvalidDataException, NotSupportedException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, HeaderVersion } from './TestHarness.js';
import { StrictRanges } from './StrictTextValueTests.js';

class FragmentedReadStream extends MemoryStream { Read(buffer,offset,count) { return super.Read(buffer,offset,Math.min(1,count)); } }
class StrictNonSeekableStream extends MemoryStream {
  get CanSeek(){return false;} get Position(){throw new NotSupportedException();} set Position(_){throw new NotSupportedException();}
  Seek(){throw new NotSupportedException();}
}
const text=value=>new TextEncoder().encode(value+'\0');
function scalar(method,size,value) {const bytes=new Uint8Array(size);new DataView(bytes.buffer)[method](0,value,true);return bytes;}
const double=value=>scalar('setFloat64',8,value);
export function RegisterStrictBinaryValueTests() {
  for(const code of StrictRanges([10,59],[110,149],[210,239],[460,469],[1010,1059])) {
    Run(`binary/strict/double/${code}/finite-bits`,()=>{for(const value of [0,-0,-1.25e12,Number.MIN_VALUE,-Number.MIN_VALUE,Number.MAX_VALUE,-Number.MAX_VALUE]) {
      const actual=StrictBinaryRead(code,double(value),'ReadDouble',true);Equal(double(value),double(actual),'finite IEEE bits');
    }});
    Run(`binary/strict/double/${code}/nonfinite`,()=>{for(const bits of [0x7ff0000000000000n,0xfff0000000000000n,0x7ff0000000000001n,0x7ff8000000000001n,0xfff8000000001234n])StrictBinaryReject(code,scalar('setBigUint64',8,bits));});
  }
  for(const code of StrictRanges([290,299])) {
    Run(`binary/strict/bool/${code}/valid`,()=>{Equal(false,StrictBinaryRead(code,Uint8Array.of(0),'ReadBool'),'false byte');Equal(true,StrictBinaryRead(code,Uint8Array.of(1),'ReadBool',true),'true byte');});
    Run(`binary/strict/bool/${code}/all-invalid-bytes`,()=>{for(let value=2;value<=255;value++)StrictBinaryReject(code,Uint8Array.of(value));});
  }
  for(const code of StrictRanges([5,5],[105,105],[320,369],[390,399],[480,481],[1005,1005])) {
    Run(`binary/strict/handle/${code}/valid`,()=>{for(const [raw,expected] of [['0','0'],[' \t000af \t','AF'],['7fffffffffffffff','7FFFFFFFFFFFFFFF'],['8000000000000000','8000000000000000'],['ffffffffffffffff','FFFFFFFFFFFFFFFF']])Equal(expected,StrictBinaryRead(code,text(raw),'ReadHex',true),'unsigned handle');});
    Run(`binary/strict/handle/${code}/invalid`,()=>{for(const raw of ['',' ','G','1G','-1','+1','0xFF','1 2','ＦF','10000000000000000','00000000000000000'])StrictBinaryReject(code,text(raw));});
  }
  RegisterBinaryIntegerEndpoints('int16','ReadShort',StrictRanges([60,79],[170,179],[270,289],[370,389],[400,409],[1060,1070]),[-32768,0,32767],'setInt16',2);
  RegisterBinaryIntegerEndpoints('int32','ReadInt',StrictRanges([90,99],[420,429],[440,459],[1071,1071]),[-2147483648,0,2147483647],'setInt32',4);
  RegisterBinaryIntegerEndpoints('int64','ReadLong',StrictRanges([160,169]),[-9223372036854775808n,0n,9223372036854775807n],'setBigInt64',8);
  Run('binary/strict/scalar-truncation',()=>{
    for(const [code,size] of [[10,8],[70,2],[90,4],[160,8],[290,1]])for(let count=0;count<size;count++) {
      const stream=new FragmentedReadStream(StrictBinaryBytes(code,new Uint8Array(count),false));
      try {const reader=new BinaryCodeValueReader(stream);Throws(EndOfStreamException,()=>reader.Next());} finally {stream.Dispose();}
    }
    const stream=new MemoryStream(StrictBinaryBytes(5,new TextEncoder().encode('ABCD'),false));
    try {const reader=new BinaryCodeValueReader(stream);Throws(EndOfStreamException,()=>reader.Next());} finally {stream.Dispose();}
  });
  Run('binary/strict/nonseekable-codec',()=>{
    const stream=new StrictNonSeekableStream(StrictBinaryBytes(290,Uint8Array.of(2),true));
    try {const reader=new BinaryCodeValueReader(stream);let failure;try{reader.Next();}catch(error){failure=error;}
      Check(failure instanceof InvalidDataException,'Invalid flag accepted on nonseekable codec input.');Check(failure.message.includes('byte address unknown'),'Nonseekable diagnostic requested an unsupported position.');
    }finally{stream.Dispose();}
  });
  Run('binary/strict/ordinary-strings',()=>{for(const code of [0,1,2,3,4,6,7,8,9,100,101,102,300,410,430,470,1000,1001,1002,1003,1006,1009])Equal('  Zażółć λ  ',StrictBinaryRead(code,text('  Zażółć λ  '),'ReadString'),'UTF-8 strings unchanged');});
  for(const version of SupportedVersions) {
    Run(`binary/strict/document/${VersionName(version)}/nonfinite`,()=>StrictBinaryDocument(version,true));
    Run(`binary/strict/document/${VersionName(version)}/invalid-handle`,()=>StrictBinaryDocument(version,false));
  }
}
function RegisterBinaryIntegerEndpoints(kind,getter,codes,values,method,size){for(const code of codes)Run(`binary/strict/${kind}/${code}/endpoints`,()=>{for(const value of values)Equal(value,StrictBinaryRead(code,scalar(method,size,value),getter,true),'signed integer endpoint and runtime type');});}
export function StrictBinaryBytes(code,payload,eof){return Uint8Array.from([...BinarySentinel,code&255,code>>>8,...payload,...(eof?[0,0,69,79,70,0]:[])]);}
export function StrictBinaryRead(code,payload,getter,fragmented=false){
  const bytes=StrictBinaryBytes(code,payload,true),stream=fragmented?new FragmentedReadStream(bytes):new MemoryStream(bytes);
  try{const reader=new BinaryCodeValueReader(stream);reader.Next();const result=reader[getter]();reader.Next();Equal('EOF',reader.ReadString(),'following record');Equal(stream.Length,stream.Position,'exact consumption');return result;}finally{stream.Dispose();}
}
export function StrictBinaryReject(code,payload){
  const stream=new FragmentedReadStream(Uint8Array.from([...new Uint8Array(7),...StrictBinaryBytes(code,payload,false)]));stream.Position=7;
  try {const reader=new BinaryCodeValueReader(stream);let failure;try{reader.Next();}catch(error){failure=error;}
    Check(failure instanceof InvalidDataException,'Malformed binary value silently accepted.');Check(failure.message.includes('group code '+code),'Missing binary group diagnostic.');Check(failure.message.includes('byte address 31'),'Wrong absolute value-byte address.');
  }finally{stream.Dispose();}
}
export function StrictBinaryDocument(version,nonfinite){
  const stream=new MemoryStream(),writer=new BinaryCodeValueWriter(stream),tag=(code,value)=>writer.Write(code,value);
  try {tag(0,'SECTION');tag(2,'HEADER');tag(9,'$ACADVER');tag(1,HeaderVersion(version));tag(9,'$DWGCODEPAGE');tag(3,'ANSI_1252');tag(9,'$HANDSEED');tag(5,'FFFF');tag(0,'ENDSEC');
    tag(0,'SECTION');tag(2,'ENTITIES');tag(0,'LINE');tag(5,nonfinite?'AB':'INVALID');tag(100,'AcDbEntity');tag(8,'0');tag(100,'AcDbLine');tag(10,nonfinite?NaN:1);tag(20,2);tag(30,3);tag(11,4);tag(21,5);tag(31,6);tag(0,'ENDSEC');tag(0,'EOF');writer.Flush();stream.Position=0;
    if(GetTypedIOConfiguration()==='Debug')Throws(InvalidDataException,()=>DxfDocument.Load(stream));else Check(DxfDocument.Load(stream)===null,'Invalid binary document loaded instead of failing.');
    Check(stream.CanRead,'Caller stream was closed.');
  }finally{stream.Dispose();}
}
