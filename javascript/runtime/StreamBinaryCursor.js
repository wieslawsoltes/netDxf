// Real synchronous stream reads for the binary codec and header probe.
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { BinaryCursor } from './BinaryCursor.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, EndOfStreamException, IOException } from './Errors.js';
/** BinaryReader.ReadBytes semantics: EOF returns a short array, not a synthetic fill. */
export function ReadStreamBytes(stream,count){
  if(!Number.isInteger(count)||count<0)throw new ArgumentOutOfRangeException('count',count);
  const bytes=new Uint8Array(count);let used=0;
  while(used<count){
    const n=stream.Read(bytes,used,count-used);
    if(!Number.isInteger(n)||n<0||n>count-used)throw new IOException('The stream returned an invalid byte count.');
    if(n===0)break;used+=n;
  }
  return used===count?bytes:bytes.slice(0,used);
}
/** The adapter never seeks, copies the complete input, or closes the caller stream. */
export class StreamBinaryCursor extends BinaryCursor {
  #stream;
  constructor(stream){
    super(new Uint8Array());
    if(stream==null)throw new ArgumentNullException('stream');
    if(!stream.CanRead||typeof stream.Read!=='function'||typeof stream.ReadByte!=='function')throw new ArgumentException('Stream was not readable.');
    this.#stream=stream;
  }
  get CanSeek(){return this.#stream.CanSeek;}
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
