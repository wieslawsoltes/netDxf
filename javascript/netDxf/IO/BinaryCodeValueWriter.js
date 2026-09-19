// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfGroupCode, DxfTagValueType as T } from './DxfGroupCode.js';
import { ArgumentNullException, ArgumentException, ArgumentOutOfRangeException, Exception } from '../../runtime/Errors.js';
import { Encoding } from '../../runtime/Encoding.js';
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
export class BinaryCodeValueWriter {
  #writer; #encoding; #legacy; #code = 0; #value = null;
  #scratch = new Uint8Array(8); #view;
  constructor(writer, legacyGroupCodes = false, encoding = Encoding.UTF8) {
    if (writer == null) throw new ArgumentNullException('writer');
    this.#writer = writer; this.#encoding = encoding; this.#legacy = legacyGroupCodes;
    this.#view = new DataView(this.#scratch.buffer); writer.Write(BinarySentinel);
  }
  get Code() { return this.#code; }
  get Value() { return this.#value; }
  get CurrentPosition() { return this.#writer.Position; }
  static #validateChunk(value) {
    if (value == null) throw new ArgumentNullException('value');
    if (!(value instanceof Uint8Array)) throw new ArgumentException('A binary DXF chunk requires a byte array.', 'value');
    if (value.length > 255) throw new ArgumentOutOfRangeException('value',value.length,'A binary DXF chunk cannot exceed its one-byte length prefix.');
  }
  Write(code,value) {
    if ((code >= 310 && code <= 319) || code === 1004) BinaryCodeValueWriter.#validateChunk(value);
    const type = {};
    const known = DxfGroupCode.TryGetValueType(code,type);
    if (this.#legacy && (!known || code === 999)) throw new ArgumentOutOfRangeException('code',code);
    this.#code = code;
    if (this.#legacy && code < 255) this.WriteByte(code);
    else { if (this.#legacy) this.WriteByte(255); this.WriteShort(code); }
    if (!known || code === 999) throw new Exception(`Code ${code} not valid in binary DXF.`);
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
  WriteByte(value) { this.#view.setUint8(0,value); this.#writer.Write(this.#scratch,0,1); }
  WriteBytes(value) { BinaryCodeValueWriter.#validateChunk(value); this.WriteByte(value.length); this.#writer.Write(value); }
  WriteShort(value) { this.#view.setInt16(0,value,true); this.#writer.Write(this.#scratch,0,2); }
  WriteInt(value) { this.#view.setInt32(0,value,true); this.#writer.Write(this.#scratch,0,4); }
  WriteLong(value) { this.#view.setBigInt64(0,value,true); this.#writer.Write(this.#scratch,0,8); }
  WriteBool(value) { this.WriteByte(value ? 1 : 0); }
  WriteDouble(value) { this.#view.setFloat64(0,value,true); this.#writer.Write(this.#scratch,0,8); }
  WriteString(value) { this.#writer.Write(this.#encoding.GetBytes(value)); this.WriteByte(0); }
  Flush() { this.#writer.Flush?.(); }
  ToString() { return `${this.#code}:${this.#value}`; }
}
