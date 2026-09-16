// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfGroupCode, DxfTagValueType as T } from './DxfGroupCode.js';
import { ArgumentNullException, Exception } from '../../runtime/Errors.js';
import { FormatDouble, HexBytes } from '../../runtime/NumberFormatting.js';
import { Encoding } from '../../runtime/Encoding.js';
export class TextCodeValueWriter {
  #writer; #encoding; #code = 0; #value = null; #position = 0;
  constructor(writer, encoding = Encoding.UTF8) {
    if (writer == null) throw new ArgumentNullException('writer');
    this.#writer = writer; this.#encoding = encoding;
  }
  get Code() { return this.#code; }
  get Value() { return this.#value; }
  get CurrentPosition() { return this.#position; }
  #line(text) {
    if (typeof this.#writer.WriteLine === 'function') this.#writer.WriteLine(text);
    else this.#writer.Write(this.#encoding.GetBytes(String(text ?? '') + '\r\n'));
  }
  Write(code,value) {
    this.#code = code; this.#line(String(code)); this.#position++;
    const type = {};
    if (!DxfGroupCode.TryGetValueType(code,type)) throw new Exception(`Code ${code} not valid at line ${this.#position}`);
    switch (type.value) {
      case T.Double: this.WriteDouble(value); break;
      case T.Boolean: this.WriteBool(value); break;
      case T.BinaryData: this.WriteBytes(value); break;
      default: this.#line(String(value ?? '')); break;
    }
    this.#value = value; this.#position++;
  }
  WriteByte(value) { this.#line(String(value)); }
  WriteBytes(value) { this.#line(HexBytes(value)); }
  WriteShort(value) { this.#line(String(value)); }
  WriteInt(value) { this.#line(String(value)); }
  WriteLong(value) { this.#line(String(value)); }
  WriteBool(value) { this.#line(value ? '1' : '0'); }
  WriteDouble(value) { this.#line(FormatDouble(value)); }
  WriteString(value) { this.#line(value); }
  Flush() { this.#writer.Flush?.(); }
  ToString() { return `${this.#code}:${this.#value}`; }
}
