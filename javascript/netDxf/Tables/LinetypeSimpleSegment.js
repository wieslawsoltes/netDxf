// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { LinetypeSegment } from './LinetypeSegment.js';
import { LinetypeSegmentType } from './LinetypeSegmentType.js';
export class LinetypeSimpleSegment extends LinetypeSegment {
  constructor(length = 0) { super(LinetypeSegmentType.Simple, length); }
  Clone() { return new LinetypeSimpleSegment(this.Length); }
}
