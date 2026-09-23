// Copyright (c) Daniel Carvajal. MIT License; see ../LICENSE and package LICENSE.

import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { DxfGroupCode, DxfTagValueType, DxfHandleKind } from './DxfGroupCode.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
const arrowName = Symbol('DIMSTYLE group-5 name');
export class DxfTag {
  #value;
  constructor(code, value, context) {
    const type = context === arrowName ? DxfTagValueType.String : DxfGroupCode.GetValueType(code);
    if (value == null) throw new ArgumentNullException('value');
    // Explicit boxes retain CLR object-type distinctions; primitive Numbers keep
    // the established code-directed JavaScript overload.
    if (value instanceof BoxedScalar) {
      const expected = new Map([[DxfTagValueType.Double, 'Double'], [DxfTagValueType.Int16, 'Int16'],
        [DxfTagValueType.Int32, 'Int32'], [DxfTagValueType.Int64, 'Int64']]).get(type);
      if (value.Type !== expected) throw new ArgumentException('DXF tag value does not match its mapped primitive type.', 'value');
      value = value.Value;
    }
    const kind = typeof value;
    let valid;
    switch (type) {
      case DxfTagValueType.String: case DxfTagValueType.Handle: valid = kind === 'string'; break;
      case DxfTagValueType.Double: valid = kind === 'number'; break;
      case DxfTagValueType.Int16: valid = Number.isInteger(value) && value >= -32768 && value <= 32767; break;
      case DxfTagValueType.Int32: valid = Number.isInteger(value) && value >= -2147483648 && value <= 2147483647; break;
      case DxfTagValueType.Int64: valid = kind === 'bigint' && value >= -9223372036854775808n && value <= 9223372036854775807n; break;
      case DxfTagValueType.Boolean: valid = kind === 'boolean'; break;
      default: valid = value instanceof Uint8Array; break;
    }
    if (!valid) throw new ArgumentException(`DXF group code ${code} requires its mapped primitive type and range.`, 'value');
    if (type === DxfTagValueType.Double && !Number.isFinite(value))
      throw new ArgumentOutOfRangeException('value', value, 'DXF numeric values must be finite in this library.');
    if (kind === 'string') {
      if (value.includes('\0')) throw new ArgumentException('A DXF tag string cannot contain NUL.', 'value');
      if (type === DxfTagValueType.Handle && !/^[0-9a-fA-F]{1,16}$/.test(value))
        throw new ArgumentException('A DXF handle requires one through sixteen ASCII hexadecimal digits.', 'value');
    }
    this.Code = code;
    this.ValueType = type;
    this.#value = type === DxfTagValueType.BinaryData ? new Uint8Array(value) : value;
    Object.freeze(this);
  }
  static CreateDimensionStyleArrowName(name) { return new DxfTag(5, name, arrowName); }
  get Value() { return this.ValueType === DxfTagValueType.BinaryData ? new Uint8Array(this.#value) : this.#value; }
  get HandleKind() { return this.ValueType === DxfTagValueType.Handle ? DxfGroupCode.GetHandleKind(this.Code) : DxfHandleKind.None; }
}
