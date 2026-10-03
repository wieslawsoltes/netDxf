// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { ToleranceEntry } from './ToleranceEntry.js';
import { ToleranceValue } from './ToleranceValue.js';
import { DatumReferenceValue } from './DatumReferenceValue.js';
import { DimensionStyle } from '../Tables/DimensionStyle.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy, Culture, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { EntityVector3 } from '../../runtime/EntityGeometry.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException } from '../../runtime/Errors.js';
const geometric = ['', 'j', 'r', 'i', 'f', 'b', 'a', 'g', 'c', 'e', 'u', 'd', 'k', 'h', 't'];
const material = ['', 'm', 'l', 's'];
const encoded = letter => letter ? '{\\Fgdt;' + letter + '}' : '';
const materialCode = letter => Math.max(0, material.indexOf(letter));
const geometricCode = letter => Math.max(0, geometric.indexOf(letter));
const nonempty = value => value !== null && value !== undefined && value !== '';
// The source uses current-culture StartsWith, but its delimiters/symbol scanner stay ordinal.
// Match only the two fixed ASCII prefixes used here, including ignorable collation weights,
// combining marks and locale-specific width handling. No input text is sanitized or replaced.
const collators = new Map();
function prefixMatches(text, prefix) {
  const name = Culture.Current.Name || 'en-US';
  if (!collators.has(name)) collators.set(name, new Intl.Collator(name, {usage:'sort',sensitivity:'variant',ignorePunctuation:false,numeric:false}));
  const collator = collators.get(name); let span = '', baseCount = 0;
  for (const scalar of text) {
    const ignorable = collator.compare(scalar, '') === 0, mark = /\p{Mark}/u.test(scalar);
    if (!ignorable && !mark) { if (baseCount === prefix.length) break; baseCount++; }
    span += scalar;
  }
  return collator.compare(span, prefix) === 0;
}
function cell(value, tolerance) {
  return '%%v' + (value === null ? '' : (tolerance && value.ShowDiameterSymbol ? encoded('n') : '') +
    (value.Value ?? '') + encoded(material[value.MaterialCondition]));
}
function entryText(entry) {
  return encoded(geometric[entry.GeometricSymbol]) + cell(entry.Tolerance1, true) + cell(entry.Tolerance2, true) +
    cell(entry.Datum1, false) + cell(entry.Datum2, false) + cell(entry.Datum3, false);
}
// The source CharEnumerator is a UTF-16 code-unit cursor, not a Unicode scalar iterator.
// Malformed symbols follow the source Release fallback; its Debug.Assert process failures
// are retained separately by the independent qualification harness, never counted as matches.
class Cursor {
  constructor(text) { this.text = text; this.index = -1; }
  next() { return ++this.index < this.text.length; }
  get current() { return this.text[this.index]; }
}
function symbol(cursor) {
  while (cursor.next()) if (cursor.current === ';') {
    if (cursor.next()) { const letter = cursor.current; if (cursor.next() && cursor.current === '}') return letter; }
    return '\0';
  }
  return '\0';
}
function parseCell(text, isTolerance) {
  if (!nonempty(text)) return null;
  const cursor = new Cursor(text); let value = '', condition = 0, diameter = false;
  while (cursor.next()) {
    if (cursor.current === '{') {
      const code = symbol(cursor);
      if (isTolerance && code === 'n') diameter = true;
      else condition = materialCode(code);
    } else value += cursor.current;
  }
  return isTolerance ? new ToleranceValue(diameter, value, condition) : new DatumReferenceValue(value, condition);
}
function parseEntry(line) {
  const parts = line.split('%%v'), entry = new ToleranceEntry();
  if (prefixMatches(parts[0], '{')) entry.GeometricSymbol = geometricCode(symbol(new Cursor(parts[0])));
  for (let i = 1; i < parts.length && i <= 5; i++) {
    const property = i <= 2 ? 'Tolerance' + i : 'Datum' + (i - 2);
    entry[property] = parseCell(parts[i], i <= 2);
  }
  return entry;
}
/** Geometric tolerance annotation with source-compatible text syntax and planar transforms. */
export class Tolerance extends EntityObject {
  Entry1; Entry2 = null; ProjectedToleranceZoneValue = ''; ShowProjectedToleranceZoneSymbol = false; DatumIdentifier = '';
  #style; #position; #height = new DataView(new ArrayBuffer(8)); #rotation = new DataView(new ArrayBuffer(8));
  constructor(entry = null, position = Vector3.Zero) {
    super(EntityType.Tolerance, DxfObjectCode.Tolerance);
    if (arguments.length > 2) throw new ArgumentException('No matching Tolerance constructor.');
    this.Entry1 = entry; this.#position = EntityVector3(position); this.#style = DimensionStyle.Default;
    this.#height.setFloat64(0, this.#style.TextHeight);
    Object.defineProperty(this, 'ToleranceStyleChanged', {value: new EventHook(), enumerable: true});
  }
  static CreateOverload(signature, ...args) {
    if (['', 'netDxf.Entities.ToleranceEntry', 'netDxf.Entities.ToleranceEntry,netDxf.Vector2', 'netDxf.Entities.ToleranceEntry,netDxf.Vector3'].includes(signature))
      return new Tolerance(...args);
    throw new ArgumentException('Unknown tolerance constructor signature.', 'signature');
  }
  get Position() { return Copy(this.#position); }
  set Position(value) { this.#position = Copy(value); }
  get Rotation() { return this.#rotation.getFloat64(0); }
  set Rotation(value) { this.#rotation.setFloat64(0, MathHelper.NormalizeAngle(value)); }
  get TextHeight() { return this.#height.getFloat64(0); }
  set TextHeight(value) {
    if (value <= 0) throw new ArgumentOutOfRangeException('value', value);
    this.#height.setFloat64(0, value);
  }
  get Style() { return this.#style; }
  set Style(value) {
    if (value === null) throw new ArgumentNullException('value');
    this.#style = this.OnDimensionStyleChangedEvent(this.#style, value);
  }
  OnDimensionStyleChangedEvent(oldStyle, newStyle) {
    const event = new TableObjectChangedEventArgs(oldStyle, newStyle);
    this.ToleranceStyleChanged.Invoke(this, event); return event.NewValue;
  }
  ToStringRepresentation() {
    const lines = [];
    if (this.Entry1 !== null) lines.push(entryText(this.Entry1));
    if (this.Entry2 !== null) lines.push(entryText(this.Entry2));
    if (nonempty(this.ProjectedToleranceZoneValue) || this.ShowProjectedToleranceZoneSymbol)
      lines.push((this.ProjectedToleranceZoneValue ?? '') + (this.ShowProjectedToleranceZoneSymbol ? encoded('p') : ''));
    if (nonempty(this.DatumIdentifier)) lines.push(this.DatumIdentifier);
    return lines.join('^J');
  }
  static ParseStringRepresentation(text) {
    if (text === null || text === undefined) throw new ArgumentNullException('input');
    const lines = text.split('^J'); let first = null, second = null, projected = '', show = false, datum = '';
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      if (prefixMatches(line, '{') || prefixMatches(line, '%%v')) {
        if (i === 0) first = parseEntry(line); else if (i === 1) second = parseEntry(line);
      } else if (i === lines.length - 1) datum = line;
      else {
        const cursor = new Cursor(line); let value = '';
        while (cursor.next()) {
          if (cursor.current === '{') { if (symbol(cursor) === 'p') show = true; }
          else value += cursor.current;
        }
        projected = value;
      }
    }
    return Object.assign(new Tolerance(), {Entry1: first, Entry2: second, ProjectedToleranceZoneValue: projected,
      ShowProjectedToleranceZoneSymbol: show, DatumIdentifier: datum});
  }
  static TryParseStringRepresentation(text, result) {
    try { result.value = Tolerance.ParseStringRepresentation(text); return true; }
    catch { result.value = null; return false; }
  }
  TransformBy(matrix, translation) {
    [matrix, translation] = this.$transformArguments(matrix, translation);
    const position = Vector3.Add(Matrix3.Multiply(matrix, this.Position), translation);
    let normal = Matrix3.Multiply(matrix, this.Normal);
    if (Vector3.Equals(Vector3.Zero, normal)) normal = this.Normal;
    const toWorld = Matrix3.Multiply(MathHelper.ArbitraryAxis(this.Normal), Matrix3.RotationZ(mul(this.Rotation, MathHelper.DegToRad)));
    const toObject = MathHelper.ArbitraryAxis(normal).Transpose();
    let axis = Matrix3.Multiply(toWorld, Vector3.UnitX);
    axis = Matrix3.Multiply(matrix, axis); axis = Matrix3.Multiply(toObject, axis);
    const point = new Vector2(axis.X, axis.Y), rotation = mul(Vector2.Angle(point), MathHelper.RadToDeg);
    let height = mul(this.TextHeight, point.Modulus());
    if (MathHelper.IsZero(height)) height = MathHelper.Epsilon;
    this.TextHeight = height; this.Position = position; this.Rotation = rotation; this.Normal = normal;
  }
  Clone() {
    const copy = this.$copyEntityAttributes(new Tolerance());
    copy.Entry1 = this.Entry1?.Clone() ?? null; copy.Entry2 = this.Entry2?.Clone() ?? null;
    copy.ProjectedToleranceZoneValue = this.ProjectedToleranceZoneValue;
    copy.ShowProjectedToleranceZoneSymbol = this.ShowProjectedToleranceZoneSymbol; copy.DatumIdentifier = this.DatumIdentifier;
    if (this.#style === null) throw new NullReferenceException();
    copy.Style = this.#style.Clone(); copy.TextHeight = this.TextHeight;
    copy.Position = this.#position; copy.Rotation = this.Rotation;
    return this.$finishEntityClone(copy);
  }
}
