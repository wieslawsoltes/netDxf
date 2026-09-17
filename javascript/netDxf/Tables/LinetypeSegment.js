// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { NotSupportedException } from '../../runtime/Errors.js';
export class LinetypeSegment {
  #type; Length;
  constructor(type, length) {
    if (new.target === LinetypeSegment) throw new NotSupportedException('LinetypeSegment is abstract.');
    this.#type = type; this.Length = length;
  }
  get Type() { return this.#type; }
}
