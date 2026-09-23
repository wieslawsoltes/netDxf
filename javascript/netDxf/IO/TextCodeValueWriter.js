// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfGroupCode, DxfTagValueType as T } from './DxfGroupCode.js';
import { NullReferenceException, Exception } from '../../runtime/Errors.js';
import { FormatDouble, HexBytes } from '../../runtime/NumberFormatting.js';
import { Format } from '../../runtime/DisplayFormatting.js';
import { Encoding } from '../../runtime/Encoding.js';
export class TextCodeValueWriter {
  #writer; #encoding; #code = 0; #value = null; #position = 0;
  constructor(writer, encoding = Encoding.UTF8) {
    this.#writer = writer; this.#encoding = encoding;
  }
  get Code() { return this.#code; }
  get Value() { return this.#value; }
  get CurrentPosition() { return this.#position; }
  #line(text) {
    if (this.#writer == null) throw new NullReferenceException();
    if (typeof this.#writer.WriteLine === 'function') this.#writer.WriteLine(text);
    else this.#writer.Write(this.#encoding.GetBytes(String(text ?? '') + '\r\n'));
  }
  Write(code,value) {
    this.#code = code; this.#line(code); this.#position++;
    const type = {};
    if (!DxfGroupCode.TryGetValueType(code,type)) throw new Exception(`Code ${this.#code} not valid at line ${this.#position}`);
    switch (type.value) {
      case T.Double: this.WriteDouble(value); break;
      case T.Boolean: this.WriteBool(value); break;
      case T.BinaryData: this.WriteBytes(value); break;
      default: this.#line(String(value ?? '')); break;
    }
    this.#value = value; this.#position++;
  }
  WriteByte(value) { this.#line(String(value)); }
  WriteBytes(value) { if (value == null) throw new NullReferenceException(); this.#line(HexBytes(value)); }
  WriteShort(value) { this.#line(String(value)); }
  WriteInt(value) { this.#line(String(value)); }
  WriteLong(value) { this.#line(String(value)); }
  WriteBool(value) { this.#line(value ? 1 : 0); }
  WriteDouble(value) { this.#line(FormatDouble(value)); }
  WriteString(value) { this.#line(value); }
  Flush() { if (this.#writer == null) throw new NullReferenceException(); this.#writer.Flush?.(); }
  ToString() { return Format('{0}:{1}', this.#code, this.#value instanceof Uint8Array ? 'System.Byte[]' : this.#value); }
}
