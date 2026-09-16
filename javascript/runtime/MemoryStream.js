import { ArgumentException, ArgumentNullException, NotSupportedException, ObjectDisposedException, RequireInteger } from './Errors.js';
/** Synchronous in-memory Stream adapter. Methods intentionally retain .NET casing. */
export class MemoryStream {
  #buffer; #length = 0; #position = 0; #open = true; #writable; #expandable;
  constructor(bufferOrCapacity = 0, writable = true) {
    this.#writable = writable;
    if (typeof bufferOrCapacity === 'number') {
      this.#buffer = new Uint8Array(RequireInteger(bufferOrCapacity,0,2147483647,'capacity'));
      this.#expandable = true;
    } else {
      if (bufferOrCapacity == null) throw new ArgumentNullException('buffer');
      if (!(bufferOrCapacity instanceof Uint8Array)) throw new ArgumentException('Expected Uint8Array.', 'buffer');
      this.#buffer = bufferOrCapacity; this.#length = bufferOrCapacity.length; this.#expandable = false;
    }
  }
  #check() { if (!this.#open) throw new ObjectDisposedException('The stream is closed.'); }
  get CanRead() { return this.#open; }
  get CanWrite() { return this.#open && this.#writable; }
  get CanSeek() { return this.#open; }
  get Length() { this.#check(); return this.#length; }
  get Position() { this.#check(); return this.#position; }
  set Position(value) { this.#check(); this.#position = RequireInteger(value,0,2147483647,'value'); }
  #ensure(size) {
    if (size > this.#buffer.length) {
      if (!this.#expandable) throw new NotSupportedException('Memory stream is not expandable.');
      const next = new Uint8Array(Math.max(size,Math.min(2147483647,Math.max(256,this.#buffer.length * 2))));
      next.set(this.#buffer.subarray(0,this.#length)); this.#buffer = next;
    }
  }
  Read(buffer, offset, count) {
    this.#check(); if (buffer == null) throw new ArgumentNullException('buffer');
    RequireInteger(offset,0,buffer.length,'offset'); RequireInteger(count,0,buffer.length-offset,'count');
    const size = Math.min(count,Math.max(0,this.#length-this.#position));
    buffer.set(this.#buffer.subarray(this.#position,this.#position+size),offset); this.#position += size; return size;
  }
  ReadByte() { this.#check(); return this.#position < this.#length ? this.#buffer[this.#position++] : -1; }
  Write(buffer, offset = 0, count = buffer?.length - offset) {
    this.#check(); if (!this.#writable) throw new NotSupportedException('Stream is not writable.');
    if (buffer == null) throw new ArgumentNullException('buffer');
    RequireInteger(offset,0,buffer.length,'offset'); RequireInteger(count,0,buffer.length-offset,'count');
    const end = RequireInteger(this.#position + count,0,2147483647,'count'); this.#ensure(end);
    if (this.#position > this.#length) this.#buffer.fill(0,this.#length,this.#position);
    this.#buffer.set(buffer.subarray(offset,offset+count),this.#position);
    this.#position = end; this.#length = Math.max(this.#length,end);
  }
  WriteByte(value) { this.Write(Uint8Array.of(RequireInteger(value,0,255))); }
  SetLength(value) {
    this.#check(); if (!this.#writable) throw new NotSupportedException('Stream is not writable.');
    RequireInteger(value,0,2147483647,'value'); this.#ensure(value);
    if (value > this.#length) this.#buffer.fill(0,this.#length,value);
    this.#length = value; if (this.#position > value) this.#position = value;
  }
  ToArray() { return new Uint8Array(this.#buffer.subarray(0,this.#length)); }
  Flush() { this.#check(); }
  Dispose() { this.#open = false; }
  Close() { this.Dispose(); }
}
