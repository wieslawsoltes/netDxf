// Copyright (c) Daniel Carvajal. MIT License; see ../LICENSE and package LICENSE.

import { DxfTagSlice } from './DxfTagSlice.js';
export class DxfRawRecord {
  #sourceTags;
  constructor(sectionName, tags, start, end) {
    this.#sourceTags = tags;
    this.SectionName = sectionName; this.StartTagIndex = start; this.EndTagIndex = end;
    this.MarkerCode = tags[start].Code; this.Name = tags[start].Value;
    this.Tags = new DxfTagSlice(tags, start, end - start);
    this.Content = new DxfTagSlice(tags, start + 1, end - start - 1);
    Object.freeze(this);
  }
  /** Internal identity check. */
  IsFromSnapshot(tags) { return this.#sourceTags === tags; }
}
