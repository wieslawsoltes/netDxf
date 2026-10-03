// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { KeyValuePair } from '../../runtime/GenericDictionary.js';
export class ObservableDictionaryEventArgs {
  #item;
  constructor(item) { this.#item = KeyValuePair(item.Key, item.Value); this.Cancel = false; }
  get Item() { return KeyValuePair(this.#item.Key, this.#item.Value); }
}
