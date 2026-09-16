// Copyright (c) Daniel Carvajal. MIT License; see ../LICENSE and package LICENSE.

import { RequireInteger } from '../../runtime/Errors.js';
export class DxfRawOptions {
  constructor(maximumBytes = 64 * 1024 * 1024, maximumTags = 1000000, maximumStringLength = 1024 * 1024) {
    this.MaximumBytes = RequireInteger(maximumBytes, 1, 2147483647, 'maximumBytes');
    this.MaximumTags = RequireInteger(maximumTags, 1, 2147483647, 'maximumTags');
    this.MaximumStringLength = RequireInteger(maximumStringLength, 1, 2147483647, 'maximumStringLength');
    Object.freeze(this);
  }
}
