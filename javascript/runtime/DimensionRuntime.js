// Native language adapters shared by the source-lowered dimension entities.
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { BoxedScalar } from './BoxedScalar.js';
import { HeaderEnum } from './HeaderBox.js';
import { BoxedChar } from './BoxedChar.js';
import { BoxedBoolean } from './BoxedBoolean.js';
import { Culture } from './GeometryRuntime.js';
import { OrdinalIgnoreCaseEquals } from './Collections.js';
import { NullReferenceException, ArgumentNullException, ArgumentException, ArgumentOutOfRangeException } from './Errors.js';
export function RequireReference(value) { if (value == null) throw new NullReferenceException(); return value; }
export function DimensionValue(value) {
  RequireReference(value);
  return value instanceof BoxedScalar || value instanceof HeaderEnum || value instanceof BoxedChar || value instanceof BoxedBoolean ? value.Value : value;
}
export function Cloneable(value) { return value != null && typeof value.Clone === 'function'; }
export function StringEquals(left, right, comparison) {
  // The dimension builder uses ordinal-ignore-case only for known arrow names.
  if (comparison === 5) return OrdinalIgnoreCaseEquals(left, right);
  if (comparison !== undefined && comparison !== 4) throw new ArgumentException('Unsupported dimension string comparison.', 'comparison');
  return left === right;
}
export function StringReplace(text, oldValue, newValue) {
  RequireReference(text);
  if (oldValue == null) throw new ArgumentNullException('oldValue');
  if (oldValue === '') throw new ArgumentException('String cannot be empty.', 'oldValue');
  return text.split(oldValue).join(newValue ?? '');
}
export function StringSubstring(text, startIndex, length = undefined) {
  RequireReference(text);
  if (startIndex < 0 || startIndex > text.length) throw new ArgumentOutOfRangeException('startIndex', startIndex);
  length ??= text.length - startIndex;
  if (length < 0 || length > text.length - startIndex) throw new ArgumentOutOfRangeException('length', length);
  return text.slice(startIndex, startIndex + length);
}
// .NET 8 ICU profile: invariant uses two digits; named cultures use three.
// Hosts selecting another globalization backend can supply NumberDecimalDigits.
export function DimensionCulture() { return { NumberDecimalDigits: Culture.Current.NumberDecimalDigits ?? (Culture.Current.Name === '' ? 2 : 3), NumberDecimalSeparator: Culture.Current.NumberDecimalSeparator }; }
