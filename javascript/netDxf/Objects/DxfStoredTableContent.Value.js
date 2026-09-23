// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Vector3 } from '../Vector3.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { BoxedString } from '../../runtime/BoxedString.js';
import { TableSnapshot, CheckTableText } from '../../runtime/TablePayload.js';
import { CheckGeometryFinite } from './DxfStoredTableGeometry.js';
import { ArgumentException, ArgumentNullException } from '../../runtime/Errors.js';
export const DxfStoredTableContentValueKind = Object.freeze({ Integer:1, Double:2, String:4, Point3D:32 });
const internal = Symbol('qualified scalar');
const scalarLength = (tags, at, kind) => at >= tags.length ? 0 :
  (kind === 1 && tags[at].Code === 91 || kind === 2 && tags[at].Code === 140 || kind === 4 && tags[at].Code === 1) ? 1 :
  kind === 32 && at + 2 < tags.length && tags[at].Code === 11 && tags[at+1].Code === 21 && tags[at+2].Code === 31 ? 3 : 0;
export function LegacyScalarEnd(tags, marker) {
  if (marker + 2 >= tags.length || tags[marker+1].Code !== 90) return null;
  const length = scalarLength(tags, marker+2, tags[marker+1].Value);
  return length === 0 ? null : marker + 1 + length;
}
/** An immutable projection tied to one retained snapshot. Plain numbers are
 * kind-directed; BoxedScalar preserves explicit Int32/Double distinctions. */
export class DxfStoredTableContentValue {
  #value;
  constructor(token, fields, value) {
    if (token !== internal) throw new ArgumentException('Values are projected from retained TABLECONTENT.');
    this.#value = Copy(value); Object.assign(this, fields); Object.freeze(this);
  }
  get Value() { return Copy(this.#value); }
  WithValue(value, formattedText = null) {
    if (value == null) throw new ArgumentNullException('value');
    let compatible;
    if (value instanceof BoxedScalar) { compatible = value.Type === (this.Kind === 1 ? 'Int32' : this.Kind === 2 ? 'Double' : ''); value = value.Value; }
    else if (this.Kind === 1) compatible = typeof value === 'number' && Number.isInteger(value) && value >= -2147483648 && value <= 2147483647;
    else if (this.Kind === 2) compatible = typeof value === 'number';
    else if (this.Kind === 4) { compatible = typeof value === 'string' || value instanceof BoxedString; if (value instanceof BoxedString) value = value.Value; }
    else compatible = value instanceof Vector3 && value.constructor === Vector3;
    if (!compatible) throw new ArgumentException('Replacement must retain the exact stored scalar kind.', 'value');
    if (typeof value === 'string') CheckTableText(value, 'value');
    if (this.Kind === 2 || value instanceof Vector3) CheckGeometryFinite(value, 'value');
    if (this.DisplayIndex >= 0) CheckTableText(formattedText, 'formattedText');
    else if (formattedText !== null) throw new ArgumentException('The compact value encoding has no stored display text.', 'formattedText');
    return new DxfStoredTableContentValueEdit(this, value, formattedText);
  }
  static TryRead(input, start, offset, version, decode) {
    const tags = Array.isArray(input) ? input : Array.from(input); let at = start + 1;
    if (at + 3 >= tags.length || tags[at].Code !== 90 || tags[at++].Value !== 1 || tags[at].Code !== 300 || tags[at++].Value !== 'VALUE') return null;
    const modern = version >= 15; let flags = null, units = null;
    if (modern) { if (tags[at].Code !== 93) return null; flags = tags[at++].Value; if (![2,4,6].includes(flags)) return null; }
    if (tags[at].Code !== 90) return null;
    const kind = tags[at++].Value, scalar = at, length = scalarLength(tags, scalar, kind); if (!length) return null;
    let value = length === 3 ? new Vector3(tags[at].Value, tags[at+1].Value, tags[at+2].Value) : tags[at].Value;
    if (typeof value === 'string') value = decode(value); at += length;
    let format = null, display = null, displayIndex = -1;
    if (modern) {
      if (at + 3 >= tags.length || tags[at].Code !== 94 || tags[at+1].Code !== 300 || tags[at+2].Code !== 302 || tags[at+3].Code !== 304 || tags[at+3].Value !== 'ACVALUE_END') return null;
      units = tags[at].Value; format = decode(tags[at+1].Value); display = decode(tags[at+2].Value); displayIndex = offset + at + 2; at += 4;
    }
    if (at + 1 >= tags.length || tags[at].Code !== 91 || tags[at].Value !== 0 || tags[at+1].Code !== 309 || tags[at+1].Value !== 'CELLCONTENT_END') return null;
    return new this(internal, {Kind:kind, PayloadIndex:offset+start, ScalarIndex:offset+scalar, DisplayIndex:displayIndex,
      StoredFormatFlags:flags, StoredUnitType:units, FormatString:format, FormattedText:display, Tags:TableSnapshot(tags.slice(start,at+2))}, value);
  }
}
export class DxfStoredTableContentValueEdit {
  #value;
  constructor(original, value, formattedText) { this.#value = Copy(value); this.Original = original; this.FormattedText = formattedText; Object.freeze(this); }
  get Value() { return Copy(this.#value); }
}
