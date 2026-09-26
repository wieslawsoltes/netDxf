// Copyright (c) Daniel Carvajal. MIT License; see ../LICENSE and package LICENSE.

import { DxfTagSlice } from './DxfTagSlice.js';
import { DxfRawRecord } from './DxfRawRecord.js';
import { ReadOnlyList, OrdinalIgnoreCaseEquals as Is } from '../../runtime/Collections.js';
export class DxfRawSection {
  #records; #tags; #first; #marker;
  constructor(name, tags, start, content, end) {
    this.Name = name; this.StartTagIndex = start; this.ContentStartTagIndex = content; this.EndTagIndex = end;
    this.#tags = tags; this.#marker = Is(name, 'HEADER') ? 9 : 0;
    this.Content = new DxfTagSlice(tags, content, end - content - 1);
    let first = content;
    while (first < end - 1 && tags[first].Code !== this.#marker) first++;
    this.#first = first;
    this.Preamble = new DxfTagSlice(tags, content, first - content);
    Object.freeze(this);
  }
  get Records() {
    if (this.#records === undefined) {
      const result = []; const end = this.EndTagIndex - 1; let previous = this.#first;
      if (previous < end) {
        for (let i = previous + 1; i < end; i++) {
          if (this.#tags[i].Code !== this.#marker) continue;
          result.push(new DxfRawRecord(this.Name, this.#tags, previous, i)); previous = i;
        }
        result.push(new DxfRawRecord(this.Name, this.#tags, previous, end));
      }
      this.#records = ReadOnlyList(result);
    }
    return this.#records;
  }
}
