import fs from 'node:fs';
import path from 'node:path';
import { ArgumentException, ArgumentNullException, IOException, FileNotFoundException,
  DirectoryNotFoundException, UnauthorizedAccessException, NotSupportedException,
  ObjectDisposedException, RequireInteger } from './Errors.js';

export function FullPath(value) {
  if (value == null) throw new ArgumentNullException('path');
  if (typeof value !== 'string' || value.length === 0 || value.includes('\0'))
    throw new ArgumentException('A nonempty filesystem path without NUL is required.', 'path');
  return path.resolve(value);
}
export function FileError(error, filename) {
  if (!error?.code) return error;
  let Type = IOException;
  if (error.code === 'EACCES' || error.code === 'EPERM' || error.code === 'EROFS') Type = UnauthorizedAccessException;
  else if (error.code === 'ENOTDIR') Type = DirectoryNotFoundException;
  else if (error.code === 'ENOENT') {
    try { Type = fs.statSync(path.dirname(filename)).isDirectory() ? FileNotFoundException : DirectoryNotFoundException; }
    catch { Type = DirectoryNotFoundException; }
  }
  return new Type(error.message, { cause: error });
}
/** Synchronous host Stream adapter, not a claim of complete System.IO.FileStream parity. */
export class FileStream {
  #fd = null; #position = 0; #readable; #writable;
  constructor(filename, mode = 'Open', access = 'Read') {
    Object.defineProperty(this, 'Name', { value: FullPath(filename), enumerable: true });
    if (!['Open', 'CreateNew', 'Create'].includes(mode)) throw new NotSupportedException('Supported modes: Open, CreateNew, Create.');
    if (!['Read', 'Write', 'ReadWrite'].includes(access)) throw new ArgumentException('Invalid access.', 'access');
    this.#readable = access !== 'Write'; this.#writable = access !== 'Read';
    if (mode !== 'Open' && !this.#writable) throw new ArgumentException('Creation requires write access.', 'access');
    const c = fs.constants;
    const flags = (access === 'Read' ? c.O_RDONLY : access === 'Write' ? c.O_WRONLY : c.O_RDWR) |
      (mode === 'CreateNew' ? c.O_CREAT | c.O_EXCL : mode === 'Create' ? c.O_CREAT | c.O_TRUNC : 0);
    try { this.#fd = fs.openSync(this.Name, flags, 0o666); }
    catch (error) { throw FileError(error, this.Name); }
  }
  #check() { if (this.#fd === null) throw new ObjectDisposedException('The stream is closed.'); }
  #io(action) { this.#check(); try { return action(); } catch (error) { throw FileError(error, this.Name); } }
  get CanRead() { return this.#fd !== null && this.#readable; }
  get CanWrite() { return this.#fd !== null && this.#writable; }
  get CanSeek() { return this.#fd !== null; }
  get Position() { this.#check(); return this.#position; }
  set Position(value) { this.#check(); this.#position = RequireInteger(value, 0, Number.MAX_SAFE_INTEGER, 'value'); }
  get Length() { return this.#io(() => fs.fstatSync(this.#fd).size); }
  #range(buffer, offset, count) {
    if (buffer == null) throw new ArgumentNullException('buffer');
    if (!(buffer instanceof Uint8Array)) throw new ArgumentException('A Uint8Array is required.', 'buffer');
    RequireInteger(offset, 0, buffer.length, 'offset'); RequireInteger(count, 0, buffer.length - offset, 'count');
  }
  Read(buffer, offset = 0, count = buffer?.length - offset) {
    this.#check(); if (!this.#readable) throw new NotSupportedException('Stream is not readable.');
    this.#range(buffer, offset, count);
    const size = this.#io(() => fs.readSync(this.#fd, buffer, offset, count, this.#position));
    this.#position += size; return size;
  }
  ReadByte() { const byte = new Uint8Array(1); return this.Read(byte) ? byte[0] : -1; }
  Write(buffer, offset = 0, count = buffer?.length - offset) {
    this.#check(); if (!this.#writable) throw new NotSupportedException('Stream is not writable.');
    this.#range(buffer, offset, count);
    RequireInteger(this.#position + count, 0, Number.MAX_SAFE_INTEGER, 'count');
    while (count > 0) {
      const size = this.#io(() => fs.writeSync(this.#fd, buffer, offset, count, this.#position));
      if (size <= 0) throw new IOException('The filesystem write made no progress.');
      offset += size; count -= size; this.#position += size;
    }
  }
  WriteByte(value) { this.Write(Uint8Array.of(RequireInteger(value, 0, 255))); }
  SetLength(value) {
    this.#check(); if (!this.#writable) throw new NotSupportedException('Stream is not writable.');
    RequireInteger(value, 0, Number.MAX_SAFE_INTEGER, 'value');
    this.#io(() => fs.ftruncateSync(this.#fd, value));
    if (this.#position > value) this.#position = value;
  }
  Flush(flushToDisk = false) { this.#check(); if (flushToDisk) this.#io(() => fs.fsyncSync(this.#fd)); }
  Dispose() {
    if (this.#fd === null) return;
    const fd = this.#fd; this.#fd = null;
    try { fs.closeSync(fd); } catch (error) { throw FileError(error, this.Name); }
  }
  Close() { this.Dispose(); }
}
