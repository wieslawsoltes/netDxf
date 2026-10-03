// Shared adapters for source-bound dependency records. No evaluator or IO backend.
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ReadOnlyReferenceView } from './DatabaseModel.js';
import { ArgumentNullException, FormatException, OverflowException } from './Errors.js';

/** A live, read-only CLR list view, not a copied dependency array. */
export function DependencyView(list) { return ReadOnlyReferenceView(list); }
/** UInt64.Parse(AllowHexSpecifier): leading zeroes need not fit in sixteen digits. */
export function CanonicalDependencyHandle(value) {
  if (value == null) throw new ArgumentNullException('s');
  if (typeof value !== 'string' || !/^[0-9a-f]+$/i.test(value)) throw new FormatException('Invalid hexadecimal handle.');
  const number = BigInt('0x' + value);
  if (number > 0xffffffffffffffffn) throw new OverflowException('Handle exceeds UInt64.');
  return number.toString(16).toUpperCase();
}
