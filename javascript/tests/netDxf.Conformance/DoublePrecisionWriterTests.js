// Complete original direct-codec cases. Document IO cases remain unported rather
// than omitting their typed Save/Load, XData and header assertions.
import { DxfTagValueType } from '../../netDxf/IO/DxfGroupCode.js';
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { Encoding } from '../../runtime/Encoding.js';
import { Culture } from '../../runtime/GeometryRuntime.js';
import { fromBits } from '../../tools/wire.mjs';
import { ExpectedTagTypes } from './RawTagTests.js';
import { NewCodeWriter,NewCodeReader } from '../support/CodecFactory.js';
import { Run,Check,Equal,SameDoubleBits,BooleanName } from './TestHarness.js';
export const ExactDoubles=[1e-20,-1e-20,Number.MIN_VALUE,-Number.MIN_VALUE,1.2345678901234567,-1.2345678901234567,Number.MAX_VALUE,-Number.MAX_VALUE,0,-0,
  fromBits('3FF0000000000001'),fromBits('3FEFFFFFFFFFFFFF'),fromBits('0010000000000000'),fromBits('000FFFFFFFFFFFFF'),1e300,-1e300,1e-300,-1e-300,Math.PI,Math.E,9007199254740991,9007199254740992,0.1,1];
export function RegisterDoublePrecisionWriterTests(){
  for(const [code,type] of ExpectedTagTypes())if(type===DxfTagValueType.Double)for(const binary of [false,true])Run(`double-output/all-groups/${code}/${BooleanName(binary)}`,()=>ExactDoubleCodec(code,binary,ExactDoubles));
  for(const culture of ['en-US','pl-PL','tr-TR','ar-SA'])Run('double-output/random-corpus/'+culture,()=>ExactDoubleRandom(culture));
  Run('double-output/canonical-zero',ExactDoubleZero);
}
export function ExactDoubleCodec(code,binary,values){
  const stream=new MemoryStream();
  try{const writer=NewCodeWriter(stream,binary);for(const value of values)writer.Write(code,value);writer.Write(0,'EOF');writer.Flush();stream.Position=0;const reader=NewCodeReader(stream,binary);
    for(const expected of values){reader.Next();Equal(code,reader.Code,'Double group boundary');SameDoubleBits(expected,reader.ReadDouble(),'Lost finite-double bits at group '+code);}
    reader.Next();Equal('EOF',reader.ReadString(),'Double following-record boundary');Check(stream.CanRead,"Double codec closed its caller's stream.");
  }finally{stream.Dispose();}
}
export function ExactDoubleRandom(culture){
  const previous=Culture.Current;
  try{Culture.Current=culture;const values=[];let state=0x9E3779B97F4A7C15n;
    while(values.length<4096){state^=state>>12n;state=BigInt.asUintN(64,state^(state<<25n));state^=state>>27n;const bits=BigInt.asUintN(64,state*0x2545F4914F6CDD1Dn);if(((bits>>52n)&2047n)!==2047n)values.push(fromBits(bits.toString(16).padStart(16,'0')));}
    ExactDoubleCodec(40,false,values);
  }finally{Culture.Current=previous;}
}
export function ExactDoubleZero(){const stream=new MemoryStream();try{const writer=NewCodeWriter(stream,false);for(const value of [0,-0,1])writer.WriteDouble(value);writer.Flush();const text=Encoding.UTF8.GetString(stream.ToArray()).replace(/^\uFEFF+/,'').replace(/\r\n/g,'\n');Equal('0.0\n-0.0\n1.0\n',text,'Explicit real/negative-zero spelling');}finally{stream.Dispose();}}
