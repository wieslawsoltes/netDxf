// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { Copy } from '../../runtime/GeometryRuntime.js';

export class ObservableCollectionEventArgs {
  #item;
  constructor(item) { this.#item = Copy(item); this.Cancel = false; }
  get Item() { return Copy(this.#item); }
}
