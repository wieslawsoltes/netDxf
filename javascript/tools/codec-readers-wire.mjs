// Observation-only stream/text adapters. Parsing is performed by the production codecs.
import { BinaryCodeValueReader } from '../netDxf/IO/BinaryCodeValueReader.js';
import { TextCodeValueReader } from '../netDxf/IO/TextCodeValueReader.js';
import { DxfReader } from '../netDxf/IO/DxfReader.HeaderProbe.js';
import { DxfDocument } from '../netDxf/DxfDocument.js';
import { Encoding } from '../runtime/Encoding.js';
import { StringReader } from '../runtime/StringReader.js';
import { Culture } from '../runtime/GeometryRuntime.js';
import { IOException, NotSupportedException, ObjectDisposedException } from '../runtime/Errors.js';
import { doubleBits } from './wire.mjs';
const decode=value=>value&&typeof value==='object'?value.utf16.map(x=>String.fromCharCode(x)).join(''):value;
const wire=value=>value==null?null:typeof value==='string'?{utf16:Array.from({length:value.length},(_,i)=>value.charCodeAt(i))}:typeof value==='number'?{number:doubleBits(value)}:typeof value==='bigint'?{int64:String(value)}:value instanceof Uint8Array?{bytes:Array.from(value)}:value;
const failure=(error,message)=>({name:error.name,param:error.ParamName??null,message:message?error.Message??error.message:null});
class InputStream {
  calls=[];closed=false;attempts=0;
  constructor(input){this.input=input;this.bytes=Uint8Array.from(input.bytes);this.offset=input.origin??0;}
  get CanRead(){return !this.closed&&(this.input.readable??true);} get CanSeek(){return this.input.seekable??true;}
  get Length(){if(!this.CanSeek)throw new NotSupportedException('Length is unavailable.');return this.bytes.length;}
  get Position(){if(!this.CanSeek)throw new NotSupportedException('Position is unavailable.');return this.offset;}
  set Position(value){this.calls.push({method:'Position',value});if(this.input.failPositionSet)throw new IOException('Injected position failure.');this.offset=value;}
  before(method,count){this.attempts++;this.calls.push({method,count,offset:this.offset});if(this.attempts===this.input.failAt)throw new IOException('Injected stream read failure.');if(this.closed)throw new ObjectDisposedException('codec-input');}
  Read(buffer,offset,count){this.before('Read',count);const n=Math.min(count,this.input.fragment??2147483647,Math.max(0,this.bytes.length-this.offset));buffer.set(this.bytes.subarray(this.offset,this.offset+n),offset);this.offset+=n;return n;}
  ReadByte(){this.before('ReadByte',1);return this.offset<this.bytes.length?this.bytes[this.offset++]:-1;}
  Dispose(){this.closed=true;}Close(){this.Dispose();}
}
class InputText {
  calls=[];closed=false;attempts=0;
  constructor(input){this.input=input;this.reader=new StringReader(decode(input.text));}
  ReadLine(){this.attempts++;this.calls.push({method:'ReadLine',attempt:this.attempts});if(this.attempts===this.input.failAt)throw new IOException('Injected text read failure.');return this.reader.ReadLine();}
  Dispose(){this.closed=true;}Close(){this.Dispose();}
}
export function codecReadersCall(input){
  Culture.Current=input.culture??'';
  const host=input.mode==='text'?new InputText(input):new InputStream(input);
  let reader=null,constructor=null;
  if(input.mode!=='probe')try{
    if(input.mode==='text')reader=new TextCodeValueReader(input.nullReader?null:host);
    else {
      const encoding=input.encoding??'strict';
      const selected=encoding==='strict'?Encoding.UTF8:encoding==='latin1'?Encoding.Latin1:encoding==='ascii'?{GetString:bytes=>Array.from(bytes,x=>String.fromCharCode(x<128?x:63)).join('')}:{GetString:bytes=>new TextDecoder('utf-8',{ignoreBOM:true}).decode(bytes)};
      reader=new BinaryCodeValueReader(input.nullReader?null:host,input.nullEncoding?null:selected,input.legacy??false);
    }
  }catch(e){constructor=failure(e,true);}
  const state=()=>{if(reader===null)return null;let position;try{position=reader.CurrentPosition;}catch(e){position={error:failure(e,false)};}return {code:reader.Code,value:wire(reader.Value),position};};
  const observation=()=>input.mode==='text'?{calls:structuredClone(host.calls),closed:host.closed}:{calls:structuredClone(host.calls),closed:host.closed,offset:host.offset};
  const initial=state(),results=[];
  if(constructor===null)for(const step of input.steps){
    let value=null,error=null;
    try{
      if(input.mode==='probe'){
        const flag={value:false};
        if(step.method==='Public')value={version:DxfDocument.CheckDxfFileVersion(input.nullReader?null:host,flag),binary:flag.value};
        else value={text:wire(DxfReader.CheckHeaderVariable(input.nullReader?null:host,decode(step.variable??null),flag)),binary:flag.value};
      }else if(['Code5IsString','SkipComments'].includes(step.method))reader[step.method]=step.value;
      else value=wire(step.method==='Snapshot'?reader.Value:reader[step.method]());
    }catch(e){error=failure(e,step.method==='Next'||input.mode==='probe');}
    results.push({value,error,state:state(),host:observation()});
  }
  return {constructor,initial,results,host:observation()};
}
