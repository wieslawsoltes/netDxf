// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
export class TextStyleFontData {
  constructor(familyName, flags) {
    if (familyName == null) throw new ArgumentNullException('familyName');
    if (familyName.length > 255) throw new ArgumentOutOfRangeException('familyName');
    Object.defineProperties(this, { FamilyName: { value: familyName, enumerable: true },
      Flags: { value: RequireInteger(flags, -2147483648, 2147483647, 'flags'), enumerable: true } });
    Object.freeze(this);
  }
  get FontStyle() { return (this.Flags >> 24) & 3; }
}
