// Observation-only output hosts; the production codecs serialize every value.
import { TextCodeValueWriter } from '../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueWriter } from '../netDxf/IO/BinaryCodeValueWriter.js';
import { Encoding } from '../runtime/Encoding.js';
import { Culture } from '../runtime/GeometryRuntime.js';
import { IOException, NotSupportedException } from '../runtime/Errors.js';
import { doubleBits,fromBits } from './wire.mjs';
const utf16=s=>({utf16:Array.from({length:s.length},(_,i)=>s.charCodeAt(i))});
const wire=value=>value==null?null:typeof value==='string'?utf16(value):typeof value==='bigint'?{int64:String(value)}:typeof value==='number'?{number:doubleBits(value)}:value instanceof Uint8Array?{bytes:Array.from(value)}:value;
function read(value){if(value==null||typeof value!=='object')return value;if('utf16'in value)return value.utf16.map(x=>String.fromCharCode(x)).join('');if('repeat'in value)return read(value.repeat.value).repeat(value.repeat.count);if('bytes'in value)return Uint8Array.from(value.bytes);if('double'in value)return fromBits(value.double);if('int64'in value)return BigInt(value.int64);for(const kind of ['int16','int32','byte'])if(kind in value)return value[kind];throw new Error('Unknown writer input descriptor.');}
class OutputStream {
  calls=[];bytes=[];closed=false;attempts=0;flushes=0;hook=null;
  constructor(input){this.input=input;}
  get CanWrite(){return !this.closed;} get CanSeek(){return this.input.seekable??true;}
  get Position(){if(!this.CanSeek)throw new NotSupportedException('Position is unavailable.');return this.bytes.length;}
  before(method,count){this.calls.push({method,count});if(++this.attempts===this.input.hookAt)this.hook?.();}
  output(buffer,offset,count){if(this.attempts===this.input.failAt){this.bytes.push(...buffer.subarray(offset,offset+Math.min(count,this.input.partial??0)));throw new IOException('Injected output failure.');}for(let i=0;i<count;i++)this.bytes.push(buffer[offset+i]);}
  Write(buffer,offset=0,count=buffer.length-offset){this.before('Write',count);this.output(buffer,offset,count);}
  WriteByte(value){this.before('WriteByte',1);this.output(Uint8Array.of(value),0,1);}
  Flush(){this.calls.push({method:'Flush',count:0});if(++this.flushes===this.input.failFlush)throw new IOException('Injected flush failure.');}
}
class OutputText {
  calls=[];text='';closed=false;attempts=0;flushes=0;hook=null;
  constructor(input){this.input=input;}
  WriteLine(value){const int=typeof value==='number',converted=int?String(value):value;this.calls.push({method:int?'WriteLine.Int32':'WriteLine.String',value:wire(converted)});if(++this.attempts===this.input.hookAt)this.hook?.();if(this.attempts===this.input.failAt)throw new IOException('Injected output failure.');this.text+=(converted??'')+read(this.input.newline??'\n');}
  Flush(){this.calls.push({method:'Flush',value:null});if(++this.flushes===this.input.failFlush)throw new IOException('Injected flush failure.');}
}
export function codecWritersCall(input){
  Culture.Current=input.culture??'';const binary=input.mode==='binary',host=binary?new OutputStream(input):new OutputText(input);
  const error=e=>({name:e.name,param:e.ParamName??null,message:e instanceof IOException||e.name==='Exception'?e.Message??e.message:null});
  let writer=null,constructor=null;
  try{
    if(!binary)writer=new TextCodeValueWriter(input.nullWriter?null:host);
    else {const kind=input.encoding??'strict',encoding=kind==='strict'?Encoding.UTF8:kind==='replacement'?{CodePage:65001,GetBytes:s=>new TextEncoder().encode(s)}:kind==='ascii'?{CodePage:20127,GetBytes:s=>Uint8Array.from(Array.from(s,c=>c.codePointAt(0)<128?c.codePointAt(0):63))}:Encoding.Latin1;
      writer=new BinaryCodeValueWriter(input.nullWriter?null:host,input.legacy??false,encoding);
    }
  }catch(e){constructor=error(e);}
  const invoke=step=>step.method==='Position'?BigInt(writer.CurrentPosition):step.method==='Write'?writer.Write(step.code,read(step.value)):writer[step.method](...(Object.hasOwn(step,'value')?[read(step.value)]:[]));
  host.hook=()=>{if(input.hook)invoke(input.hook);};
  const state=()=>{if(writer===null)return null;let position;try{position=writer.CurrentPosition;}catch(e){position={error:error(e)};}return{code:writer.Code,value:wire(writer.Value),position};};
  const snapshot=()=>({calls:structuredClone(host.calls),output:binary?{bytes:host.bytes.slice()}:wire(host.text),closed:host.closed});
  const initial=state(),results=[];
  if(constructor===null)for(const step of input.steps){let value=null,failure=null;try{value=wire(invoke(step));}catch(e){failure=error(e);}results.push({value,error:failure,state:state(),host:snapshot()});}
  return{constructor,initial,results,host:snapshot()};
}
