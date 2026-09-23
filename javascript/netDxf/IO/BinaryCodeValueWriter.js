// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfGroupCode, DxfTagValueType as T } from './DxfGroupCode.js';
import { NullReferenceException, ArgumentNullException, ArgumentException, ArgumentOutOfRangeException, Exception } from '../../runtime/Errors.js';
import { Format } from '../../runtime/DisplayFormatting.js';
import { WriteBinaryCharacters } from '../../runtime/BinaryWriteCharacters.js';
import { Encoding } from '../../runtime/Encoding.js';
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
export class BinaryCodeValueWriter {
  #writer; #encoding; #legacy; #code = 0; #value = null;
  constructor(writer, legacyGroupCodes = false, encoding = Encoding.UTF8) {
    if (writer == null) throw new NullReferenceException();
    this.#writer = writer; this.#encoding = encoding; this.#legacy = legacyGroupCodes;
    writer.Write(BinarySentinel.slice());
  }
  get Code() { return this.#code; }
  get Value() { return this.#value; }
  get CurrentPosition() { this.Flush(); return this.#writer.Position; }
  static #validateChunk(value) {
    if (value == null) throw new ArgumentNullException('value');
    if (!(value instanceof Uint8Array)) throw new ArgumentException('A binary DXF chunk requires a byte array.', 'value');
    if (value.length > 255) throw new ArgumentOutOfRangeException('value',value.length,'A binary DXF chunk cannot exceed its one-byte length prefix. Split the data into valid records explicitly.');
  }
  Write(code,value) {
    if ((code >= 310 && code <= 319) || code === 1004) BinaryCodeValueWriter.#validateChunk(value);
    const type = {};
    const known = DxfGroupCode.TryGetValueType(code,type);
    if (this.#legacy && (!known || code === 999)) throw new ArgumentOutOfRangeException('code',code,'No supported legacy binary DXF value encoding exists for this group code.');
    this.#code = code;
    if (this.#legacy && code < 255) this.WriteByte(code);
    else { if (this.#legacy) this.WriteByte(255); this.WriteShort(code); }
    if (code === 999) throw new Exception(`The comment group, 999, is not used in binary DXF files at byte address ${this.CurrentPosition}`);
    if (!known) throw new Exception(`Code ${this.#code} not valid at byte address ${this.CurrentPosition}`);
    switch (type.value) {
      case T.String: case T.Handle: this.WriteString(value); break;
      case T.Double: this.WriteDouble(value); break;
      case T.Int16: this.WriteShort(value); break;
      case T.Int32: this.WriteInt(value); break;
      case T.Int64: this.WriteLong(value); break;
      case T.Boolean: this.WriteBool(value); break;
      case T.BinaryData: this.WriteBytes(value); break;
    }
    this.#value = value;
  }
  WriteByte(value) { if (typeof this.#writer.WriteByte === 'function') this.#writer.WriteByte(value); else this.#writer.Write(Uint8Array.of(value)); }
  WriteBytes(value) { BinaryCodeValueWriter.#validateChunk(value); this.WriteByte(value.length); this.#writer.Write(value); }
  #number(value, length, method) {
    // A call-local buffer survives synchronous re-entry through caller Write.
    const bytes = new Uint8Array(length); new DataView(bytes.buffer)[method](0,value,true); this.#writer.Write(bytes);
  }
  WriteShort(value) { this.#number(value,2,'setInt16'); }
  WriteInt(value) { this.#number(value,4,'setInt32'); }
  WriteLong(value) { this.#number(value,8,'setBigInt64'); }
  WriteBool(value) { this.WriteByte(value ? 1 : 0); }
  WriteDouble(value) { this.#number(value,8,'setFloat64'); }
  WriteString(value) {
    if (value == null) throw new NullReferenceException();
    WriteBinaryCharacters(this.#writer,this.#encoding,value);
    // The native terminator is an encoded char, not BinaryWriter.Write(byte).
    this.#writer.Write(this.#encoding.GetBytes('\0'));
  }
  Flush() { this.#writer.Flush?.(); }
  ToString() { return Format('{0}:{1}', this.#code, this.#value instanceof Uint8Array ? 'System.Byte[]' : this.#value); }
}
