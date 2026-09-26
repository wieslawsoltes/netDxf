// Explicit CLR Char boxing for object-valued APIs; a Char is one UTF-16 code unit.
import { ArgumentException } from './Errors.js';
export class BoxedChar {
  #value;
  constructor(value) {
    if (typeof value !== 'string' || value.length !== 1) throw new ArgumentException('A single UTF-16 code unit is required.', 'value');
    this.#value = value; Object.freeze(this);
  }
  get Type() { return 'Char'; }
  get Value() { return this.#value; }
  ToString() { return this.#value; }
}
