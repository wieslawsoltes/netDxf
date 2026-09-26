// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { Copy } from '../../runtime/GeometryRuntime.js';
export class TableObjectChangedEventArgs {
  #oldValue; #newValue;
  constructor(oldTable, newTable) { this.#oldValue = Copy(oldTable); this.#newValue = Copy(newTable); }
  get OldValue() { return Copy(this.#oldValue); }
  get NewValue() { return Copy(this.#newValue); }
  set NewValue(value) { this.#newValue = Copy(value); }
}
