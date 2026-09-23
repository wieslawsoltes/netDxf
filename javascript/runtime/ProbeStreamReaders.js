// Stream-based readers for the header probe. These never close caller-owned input.
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Encoding detection adapted from .NET Foundation and Contributors (MIT).
// See DOTNET-MIT-LICENSE.txt and THIRD_PARTY_NOTICES.md.
import { BinaryCursor } from './BinaryCursor.js';
import { EndOfStreamException } from './Errors.js';
export function ReadStreamBytes(stream,count){
  const bytes=new Uint8Array(count);let used=0;
  while(used<count){const n=stream.Read(bytes,used,count-used);if(n===0)break;used+=n;}
  return used===count?bytes:bytes.slice(0,used);
}
/** A BinaryCursor-compatible adapter which actually invokes the Stream APIs. */
export class ProbeBinaryCursor extends BinaryCursor {
  #stream;
  constructor(stream){super(new Uint8Array());this.#stream=stream;}
  get Position(){return this.#stream.Position;}
  get Length(){return this.#stream.Length;}
  ReadBytes(count){return ReadStreamBytes(this.#stream,count);}
  #value(size,kind){const bytes=this.ReadBytes(size);if(bytes.length!==size)throw new EndOfStreamException('Unable to read beyond the end of the stream.');return new DataView(bytes.buffer,bytes.byteOffset,size)[kind](0,true);}
  ReadByte(){const byte=this.#stream.ReadByte();if(byte<0)throw new EndOfStreamException('Unable to read beyond the end of the stream.');return byte;}
  ReadInt16(){return this.#value(2,'getInt16');}
  ReadInt32(){return this.#value(4,'getInt32');}
  ReadInt64(){return this.#value(8,'getBigInt64');}
  ReadDouble(){return this.#value(8,'getFloat64');}
  NullTerminatedString(encoding){const bytes=[];for(let byte;(byte=this.ReadByte())!==0;)bytes.push(byte);return encoding.GetString(Uint8Array.from(bytes));}
}
export const ProbeAscii=Object.freeze({GetString:bytes=>Array.from(bytes,b=>b<128?String.fromCharCode(b):'?').join('')});
/** UTF-8 replacement decoding with standard StreamReader BOM detection.
 * Buffered reading preserves early exit: no whole-file read or remainder parsing.
 */
export class ProbeTextReader {
  #stream;#decoder=new TextDecoder('utf-8',{fatal:false,ignoreBOM:true});#text='';#position=0;#ended=false;#prefix=[];#preamble=true;#detect=true;#utf32=null;#tail=[];
  constructor(stream){this.#stream=stream;}
  #decode(bytes,final){
    if(this.#utf32!==null){
      const input=Uint8Array.from([...this.#tail,...bytes]),view=new DataView(input.buffer);let at=0,text='';
      for(;at+4<=input.length;at+=4){const scalar=view.getUint32(at,this.#utf32);text+=scalar>0x10ffff||scalar>=0xd800&&scalar<=0xdfff?'\ufffd':String.fromCodePoint(scalar);}
      this.#tail=Array.from(input.subarray(at));if(final&&this.#tail.length){text+='\ufffd';this.#tail=[];}return text;
    }
    return this.#decoder.decode(bytes,{stream:!final});
  }
  #fill(){
    if(this.#ended)return false;
    // The default UTF-8 preamble is buffered separately from optional encoding
    // detection. A fragmented non-UTF8 BOM is NOT silently reassembled. This
    // preserves .NET StreamReader.IsPreamble / DetectEncoding / ReadBuffer.
    // MIT source: dotnet/runtime v8.0.0, System/IO/StreamReader.cs.
    const offset=this.#preamble?this.#prefix.length:0;
    const buffer=new Uint8Array(1024),count=this.#stream.Read(buffer,offset,buffer.length-offset),final=count===0;
    let bytes=buffer.subarray(offset,offset+count);
    if(this.#preamble){
      this.#prefix.push(...bytes);bytes=Uint8Array.from(this.#prefix);
      const preamble=[0xef,0xbb,0xbf],length=Math.min(bytes.length,3);
      if(Array.from(bytes.subarray(0,length)).some((value,i)=>value!==preamble[i]))this.#preamble=false;
      else if(length===3){bytes=bytes.subarray(3);this.#preamble=false;this.#detect=false;}
      else if(!final)return true;
      if(!this.#preamble||final)this.#prefix=[];
    }
    if(!final&&this.#detect&&bytes.length>=2){
      this.#detect=false;let skip=0,encoding=null;
      if(bytes[0]===0xfe&&bytes[1]===0xff){skip=2;encoding='utf-16be';}
      else if(bytes[0]===0xff&&bytes[1]===0xfe){
        if(bytes.length>=4&&bytes[2]===0&&bytes[3]===0){skip=4;this.#utf32=true;}
        else{skip=2;encoding='utf-16le';}
      }else if(bytes.length>=3&&bytes[0]===0xef&&bytes[1]===0xbb&&bytes[2]===0xbf){skip=3;encoding='utf-8';}
      else if(bytes.length>=4&&bytes[0]===0&&bytes[1]===0&&bytes[2]===0xfe&&bytes[3]===0xff){skip=4;this.#utf32=false;}
      else if(bytes.length===2)this.#detect=true;
      if(encoding!==null){this.#decoder=new TextDecoder(encoding,{fatal:false,ignoreBOM:true});this.#utf32=null;this.#tail=[];}
      bytes=bytes.subarray(skip);
    }
    this.#text=this.#text.slice(this.#position)+this.#decode(bytes,final);this.#position=0;this.#ended=final;
    return !final||this.#text.length>0;
  }
  #available(){while(this.#position>=this.#text.length&&!this.#ended)this.#fill();return this.#position<this.#text.length;}
  ReadLine(){
    if(!this.#available())return null;
    const parts=[];
    for(;;){
      const start=this.#position;
      while(this.#position<this.#text.length){
        const code=this.#text.charCodeAt(this.#position++);
        if(code===10||code===13){parts.push(this.#text.slice(start,this.#position-1));if(code===13&&this.#available()&&this.#text.charCodeAt(this.#position)===10)this.#position++;return parts.join('');}
      }
      parts.push(this.#text.slice(start,this.#position));if(!this.#available())return parts.join('');
    }
  }
}
