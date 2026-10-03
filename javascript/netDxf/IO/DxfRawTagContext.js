// Copyright (c) Daniel Carvajal. MIT License; see ../LICENSE and package LICENSE.

import { OrdinalIgnoreCaseEquals as Is } from '../../runtime/Collections.js';
export class DxfRawTagContext {
  #section; #table; #record; #sectionNamePending = false; #tableNamePending = false; #controlDepth = 0; #xdata = false;
  get Code5IsString() {
    return Is(this.#section,'TABLES') && Is(this.#table,'DIMSTYLE') && Is(this.#record,'DIMSTYLE') && this.#controlDepth === 0 && !this.#xdata;
  }
  Advance(tag) {
    const value = tag.Value;
    if (tag.Code === 999) return;
    if (tag.Code === 0) {
      this.#controlDepth = 0; this.#xdata = false; this.#record = value; this.#tableNamePending = false;
      if (Is(value,'ENDSEC') || Is(value,'EOF')) {
        this.#section = this.#table = null; this.#sectionNamePending = false;
      } else if (this.#section == null && Is(value,'SECTION')) this.#sectionNamePending = true;
      else if (Is(this.#section,'TABLES')) {
        if (Is(value,'TABLE')) { this.#table = null; this.#tableNamePending = true; }
        else if (Is(value,'ENDTAB')) this.#table = null;
      }
      return;
    }
    if (this.#sectionNamePending) {
      if (tag.Code === 2) this.#section = value;
      this.#sectionNamePending = false; return;
    }
    if (tag.Code === 1001) this.#xdata = true;
    if (this.#xdata) return;
    if (tag.Code === 102) {
      if (value.startsWith('{')) this.#controlDepth++;
      else if (value === '}' && this.#controlDepth > 0) this.#controlDepth--;
      return;
    }
    if (tag.Code === 2 && this.#tableNamePending && this.#controlDepth === 0) {
      this.#table = value; this.#tableNamePending = false;
    }
  }
}
