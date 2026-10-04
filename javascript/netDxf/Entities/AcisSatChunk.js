// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { AcisEntity } from './AcisEntity.js';
export class AcisSatChunk {
  constructor(groupCode, text) {
    if (groupCode !== 1 && groupCode !== 3) throw new ArgumentOutOfRangeException('groupCode');
    if (text == null) throw new ArgumentNullException('text');
    if (text.length > 255) throw new ArgumentOutOfRangeException('text');
    AcisEntity.ValidateAscii(text, 'text');
    Object.defineProperties(this, { GroupCode: {value:groupCode, enumerable:true}, Text: {value:text, enumerable:true} });
    Object.freeze(this);
  }
}
