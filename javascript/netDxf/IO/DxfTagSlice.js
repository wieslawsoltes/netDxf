// Copyright (c) Daniel Carvajal. MIT License; see ../LICENSE and package LICENSE.

import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
/** Zero-copy read-only view; C# indexers map to get_Item(index) (also at(index)). */
export class DxfTagSlice {
  #tags; #start;
  constructor(tags, start, count) {
    this.#tags = tags; this.#start = start; this.Count = count; Object.freeze(this);
  }
  get length() { return this.Count; }
  get_Item(index) {
    if (!Number.isInteger(index) || index < 0 || index >= this.Count)
      throw new ArgumentOutOfRangeException('index', index);
    return this.#tags[this.#start + index];
  }
  at(index) { return this.get_Item(index); }
  *GetEnumerator() { for (let i = 0; i < this.Count; i++) yield this.#tags[this.#start + i]; }
  [Symbol.iterator]() { return this.GetEnumerator(); }
}
