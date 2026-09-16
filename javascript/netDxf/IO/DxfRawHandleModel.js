// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { ArgumentException, ArgumentNullException, RequireInteger } from '../../runtime/Errors.js';

export const DxfRawHandleRole = Object.freeze({ Identity: 0, Owner: 1, SoftPointer: 2, HardPointer: 3,
  SoftOwner: 4, HardOwner: 5, Reactor: 6, ExtensionDictionary: 7, XData: 8, Arbitrary: 9,
  HeaderSeed: 10, HeaderReference: 11, Opaque: 12 });
export const DxfRawHandleDiagnosticKind = Object.freeze({ DuplicateIdentity: 0, MultipleIdentities: 1,
  NullIdentity: 2, UnresolvedReference: 3, AmbiguousReference: 4, MultipleOwners: 5,
  InvalidControlGroup: 6, OwnerCycle: 7 });

/** Shared partial-class helper, not a package-root export. */
export function ParseHandle(value, parameter) {
  if (value == null) throw new ArgumentNullException(parameter);
  if (typeof value !== 'string' || !/^[0-9a-fA-F]{1,16}$/.test(value))
    throw new ArgumentException('Expected one through sixteen hexadecimal digits.', parameter);
  return BigInt('0x' + value);
}
export class DxfRawHandleOccurrence {
  constructor(record, index, tag, role, context, subclass) {
    this.Record = record; this.TagIndex = index; this.Code = tag.Code; this.Handle = tag.Value;
    this.NumericHandle = BigInt('0x' + this.Handle);
    this.CanonicalHandle = this.NumericHandle.toString(16).toUpperCase();
    this.Role = role; this.Context = context; this.Subclass = subclass;
    Object.freeze(this);
  }
  get IsReference() {
    const R = DxfRawHandleRole;
    return this.Role !== R.Identity && this.Role !== R.Arbitrary && this.Role !== R.HeaderSeed && this.Role !== R.Opaque;
  }
}
export class DxfRawHandleDiagnostic {
  constructor(kind, record, tagIndex, handle, message) {
    this.Kind = kind; this.Record = record; this.TagIndex = tagIndex; this.Handle = handle; this.Message = message;
    Object.freeze(this);
  }
}
export class DxfRawHandleIndexOptions {
  constructor(maximumOccurrences = 1000000, maximumDiagnostics = 100000) {
    this.MaximumOccurrences = RequireInteger(maximumOccurrences, 1, 2147483647, 'maximumOccurrences');
    this.MaximumDiagnostics = RequireInteger(maximumDiagnostics, 1, 2147483647, 'maximumDiagnostics');
    Object.freeze(this);
  }
}
