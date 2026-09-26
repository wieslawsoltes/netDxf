// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { ArgumentException, ArgumentNullException, EndOfStreamException } from './Errors.js';
export const BinarySentinel = Uint8Array.of(65,117,116,111,67,65,68,32,66,105,110,97,114,121,32,68,88,70,13,10,26,0);
/** Bounded little-endian byte view; no numeric values are read through Number for Int64. */
export class BinaryCursor {
  #bytes; #view; #stream; #origin = 0; #position = 0;
  constructor(input) {
    if (input == null) throw new ArgumentNullException('reader');
    if (input instanceof Uint8Array) this.#bytes = input;
    else if (typeof input.ToArray === 'function' && input.CanSeek) {
      this.#stream = input; this.#origin = input.Position; this.#bytes = input.ToArray().subarray(input.Position);
    } else throw new ArgumentException('A byte buffer or seekable MemoryStream is required by the low-level codec.');
    this.#view = new DataView(this.#bytes.buffer,this.#bytes.byteOffset,this.#bytes.byteLength);
  }
  get CanSeek() { return true; }
  get Position() { return this.#origin + this.#position; }
  get Length() { return this.#origin + this.#bytes.length; }
  #move(size) {
    if (size > this.#bytes.length-this.#position) throw new EndOfStreamException('The binary DXF value is incomplete.');
    const at = this.#position; this.#position += size;
    if (this.#stream) this.#stream.Position = this.Position;
    return at;
  }
  ReadBytes(size) { const at = this.#move(size); return this.#bytes.subarray(at,at+size); }
  ReadByte() { return this.#view.getUint8(this.#move(1)); }
  ReadInt16() { return this.#view.getInt16(this.#move(2),true); }
  ReadInt32() { return this.#view.getInt32(this.#move(4),true); }
  ReadInt64() { return this.#view.getBigInt64(this.#move(8),true); }
  ReadDouble() { return this.#view.getFloat64(this.#move(8),true); }
  NullTerminatedString(encoding) {
    const end = this.#bytes.indexOf(0,this.#position);
    if (end < 0) throw new EndOfStreamException('The binary DXF string has no terminator.');
    const start = this.#move(end-this.#position+1);
    return encoding.GetString(this.#bytes.subarray(start,end));
  }
}
