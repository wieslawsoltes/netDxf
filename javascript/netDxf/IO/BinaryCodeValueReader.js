import { CodecCastException } from '../../runtime/CodecValueType.js';
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { NullReferenceException } from '../../runtime/Errors.js';
import { DxfGroupCode, DxfTagValueType as T } from './DxfGroupCode.js';
import { ArgumentNullException, EndOfStreamException, InvalidDataException, Exception } from '../../runtime/Errors.js';
import { NormalizeHandle } from '../../runtime/NumberFormatting.js';
import { Format } from '../../runtime/DisplayFormatting.js';
import { Encoding } from '../../runtime/Encoding.js';
import { BinaryCursor, BinarySentinel } from '../../runtime/BinaryCursor.js';
import { StreamBinaryCursor } from '../../runtime/StreamBinaryCursor.js';
import { MemoryStream } from '../../runtime/MemoryStream.js';
export class BinaryCodeValueReader {
  #reader; #encoding; #legacy; #code = 0; #value = null; #valueType = -1; #valuePosition = -1;
  Code5IsString = false;
  constructor(reader, encoding = Encoding.UTF8, legacyGroupCodes = false) {
    if (reader == null) throw new ArgumentNullException('reader');
    if (encoding == null) throw new ArgumentNullException('encoding');
    this.#reader = reader instanceof BinaryCursor ? reader : new StreamBinaryCursor(reader instanceof Uint8Array ? new MemoryStream(reader,false) : reader);
    this.#encoding = encoding; this.#legacy = legacyGroupCodes;
    const sentinel = this.#reader.ReadBytes(22);
    if (sentinel.length !== 22) throw new EndOfStreamException('The binary DXF sentinel is incomplete; 22 bytes are required.');
    for (let i = 0; i < 22; i++) if (sentinel[i] !== BinarySentinel[i]) throw new InvalidDataException('Not a valid binary DXF sentinel.');
  }
  get Code() { return this.#code; }
  get Value() { return this.#value; }
  get CurrentPosition() { return this.#reader.Position; }
  get Length() { return this.#reader.Length; }
  Next() {
    if (this.#legacy) {
      this.#code = this.#reader.ReadByte();
      if (this.#code === 255) {
        this.#code = this.#reader.ReadInt16();
        if (this.#code < 255) throw new InvalidDataException('A legacy binary DXF escape requires a group code of at least 255.');
      }
    } else this.#code = this.#reader.ReadInt16();
    this.#valuePosition = this.#reader.CanSeek ? this.#reader.Position : -1;
    const type = {};
    // The source reads Position again for these structural errors, even when
    // CanSeek is false; preserve the resulting stream exception and prior Value.
    if (this.#code === 999) throw new Exception(`The comment group, 999, is not used in binary DXF files at byte address ${this.#reader.Position}`);
    if (!DxfGroupCode.TryGetValueType(this.#code,type)) throw new Exception(`Code ${this.#code} not valid at byte address ${this.#reader.Position}`);
    if (this.#code === 5 && this.Code5IsString) type.value = T.String;
    let value;
    switch (type.value) {
      case T.String: value = this.#reader.NullTerminatedString(this.#encoding); break;
      case T.Handle: {
        const handle = NormalizeHandle(this.#reader.NullTerminatedString(this.#encoding));
        if (handle == null) throw this.#invalid('hexadecimal handle (1 to 16 digits)');
        value = handle; break;
      }
      case T.Double: {
        value = this.#reader.ReadDouble();
        if (!Number.isFinite(value)) throw this.#invalid('finite double-precision number');
        break;
      }
      case T.Int16: value = this.#reader.ReadInt16(); break;
      case T.Int32: value = this.#reader.ReadInt32(); break;
      case T.Int64: value = this.#reader.ReadInt64(); break;
      case T.Boolean: {
        const flag = this.#reader.ReadByte();
        if (flag > 1) throw this.#invalid('boolean (0 or 1)');
        value = flag === 1; break;
      }
      case T.BinaryData: {
        const length = this.#reader.ReadByte();
        value = new Uint8Array(this.#reader.ReadBytes(length));
        if (value.length !== length) throw new EndOfStreamException(`Incomplete binary DXF chunk for group ${this.#code}: expected ${length} bytes, received ${value.length}.`);
        break;
      }
    }
    this.#value = value; this.#valueType = type.value;
  }
  #invalid(kind) { return new InvalidDataException(`Invalid ${kind} value for group code ${this.#code} at byte address ${this.#valuePosition < 0 ? 'unknown' : this.#valuePosition}.`); }
  #cast(type, reference = false) {
    if (this.#value === null) {
      if (reference) return null;
      throw new NullReferenceException('A null codec value cannot be unboxed.');
    }
    if (this.#valueType !== type && !(type === T.String && this.#valueType === T.Handle))
      throw CodecCastException(this.#valueType,type);
    return this.#value;
  }
  ReadByte() { return this.#cast(-2); }
  ReadBytes() { return this.#cast(T.BinaryData, true); }
  ReadShort() { return this.#cast(T.Int16); }
  ReadInt() { return this.#cast(T.Int32); }
  ReadLong() { return this.#cast(T.Int64); }
  ReadBool() { return this.#cast(T.Boolean); }
  ReadDouble() { return this.#cast(T.Double); }
  ReadString() { return this.#cast(T.String, true); }
  ReadHex() { return this.ReadString(); }
  ToString() { return Format('{0}:{1}', this.#code, this.#value instanceof Uint8Array ? 'System.Byte[]' : this.#value); }
}
