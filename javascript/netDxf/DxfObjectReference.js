// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
export class DxfObjectReference {
  #reference; #uses;
  constructor(reference, uses) { this.#reference = reference; this.#uses = uses; }
  get Reference() { return this.#reference; }
  get Uses() { return this.#uses; }
}
