// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { InvalidCastException, NullReferenceException } from '../../runtime/Errors.js';
import { DxfGroupCode, DxfTagValueType as T } from './DxfGroupCode.js';
import { ArgumentNullException, Exception, FormatException, EndOfStreamException } from '../../runtime/Errors.js';
import { NormalizeHandle } from '../../runtime/NumberFormatting.js';
import { StringReader } from '../../runtime/StringReader.js';
import { Format } from '../../runtime/DisplayFormatting.js';
const integer = /^[\t\n\v\f\r ]*[+-]?\d+[\t\n\v\f\r ]*$/;
const real = /^[\t\n\v\f\r ]*[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?[\t\n\v\f\r ]*$/;
export class TextCodeValueReader {
  #reader; #code = 0; #value = null; #valueType = -1; #position = 0;
  Code5IsString = false; SkipComments = false;
  constructor(reader) {
    if (reader == null) throw new ArgumentNullException('reader');
    this.#reader = typeof reader === 'string' ? new StringReader(reader) : reader;
  }
  get Code() { return this.#code; }
  get Value() { return this.#value; }
  get CurrentPosition() { return this.#position; }
  get Reader() { return this.#reader; }
  Next() {
    do {
      const line = this.#reader.ReadLine();
      if (line == null) throw new EndOfStreamException(`Missing DXF group code at line ${this.#position + 1}.`);
      this.#position++;
      const codeText = line.replace(/\0+$/, '');
      const code = Number(codeText);
      const validCode = integer.test(codeText) && Number.isInteger(code) && code >= -32768 && code <= 32767;
      this.#code = validCode && code !== 0 ? code : 0;
      if (!validCode)
        throw new FormatException(`Invalid DXF group code at line ${this.#position}.`);
      const text = this.#reader.ReadLine();
      if (text == null) throw new EndOfStreamException(`Missing value for group code ${code} at line ${this.#position + 1}.`);
      this.#value = this.#readValue(text);
      this.#valueType = this.#code === 5 && this.Code5IsString ? T.String : DxfGroupCode.GetValueType(this.#code);
      this.#position++;
    } while (this.SkipComments && this.#code === 999);
  }
  #invalid(kind) { return new FormatException(`Invalid ${kind} value for group code ${this.#code} at line ${this.#position + 1}.`); }
  #readValue(text) {
    if (this.#code === 5 && this.Code5IsString) return text;
    const result = {};
    if (!DxfGroupCode.TryGetValueType(this.#code,result)) throw new Exception(`Code "${this.#code}" not valid at line ${this.#position}`);
    switch (result.value) {
      case T.String: return text;
      case T.Handle: { const handle = NormalizeHandle(text); if (handle == null) throw this.#invalid('hexadecimal handle (1 to 16 digits)'); return handle; }
      case T.Double: { const value = Number(text); if (!real.test(text) || !Number.isFinite(value)) throw this.#invalid('finite double-precision number'); return value; }
      case T.Int16: case T.Int32: case T.Boolean: {
        const value = Number(text); const [minimum,maximum] = result.value === T.Int16 ? [-32768,32767] : result.value === T.Int32 ? [-2147483648,2147483647] : [0,1];
        if (!integer.test(text) || !Number.isInteger(value) || value < minimum || value > maximum) throw this.#invalid(result.value === T.Int16 ? '16-bit integer' : result.value === T.Int32 ? '32-bit integer' : 'boolean (0 or 1)');
        return result.value === T.Boolean ? value === 1 : value === 0 ? 0 : value;
      }
      case T.Int64: {
        if (!integer.test(text)) throw this.#invalid('64-bit integer');
        const value = BigInt(text.trim());
        if (value < -9223372036854775808n || value > 9223372036854775807n) throw this.#invalid('64-bit integer');
        return value;
      }
      case T.BinaryData: {
        if (text.length & 1) throw new FormatException(`Binary chunk for group code ${this.#code} at line ${this.#position + 1} must contain an even number of hexadecimal digits.`);
        const bytes = new Uint8Array(text.length / 2);
        for (let i = 0; i < bytes.length; i++) {
          const pair = text.slice(i * 2, i * 2 + 2);
          if (!/^[0-9a-fA-F]{2}$/.test(pair)) throw new FormatException(`Invalid hexadecimal digit in binary chunk for group code ${this.#code} at line ${this.#position + 1}, byte ${i}.`);
          bytes[i] = parseInt(pair, 16);
        }
        return bytes;
      }
    }
  }
  #cast(type, reference = false) {
    if (this.#value === null) {
      if (reference) return null;
      throw new NullReferenceException('A null codec value cannot be unboxed.');
    }
    if (this.#valueType !== type && !(type === T.String && this.#valueType === T.Handle))
      throw new InvalidCastException('The current codec value has a different primitive type.');
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
