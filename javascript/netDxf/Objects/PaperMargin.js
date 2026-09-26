// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { CopyValue } from '../../runtime/GeometryRuntime.js';
import { ArgumentException } from '../../runtime/Errors.js';
/** Mutable value type: owners copy on assignment and access, as with Vector2. */
export class PaperMargin {
  #data = new DataView(new ArrayBuffer(32));
  constructor(left = 0, bottom = 0, right = 0, top = 0) {
    if (arguments.length !== 0 && arguments.length !== 4) throw new ArgumentException('No matching PaperMargin constructor.');
    this.Left = left; this.Bottom = bottom; this.Right = right; this.Top = top;
  }
  get Left() { return this.#data.getFloat64(0); } set Left(value) { this.#data.setFloat64(0, value); }
  get Bottom() { return this.#data.getFloat64(8); } set Bottom(value) { this.#data.setFloat64(8, value); }
  get Right() { return this.#data.getFloat64(16); } set Right(value) { this.#data.setFloat64(16, value); }
  get Top() { return this.#data.getFloat64(24); } set Top(value) { this.#data.setFloat64(24, value); }
  [CopyValue]() { return new PaperMargin(this.Left, this.Bottom, this.Right, this.Top); }
  Equals(other) {
    return other instanceof PaperMargin && ['Left', 'Bottom', 'Right', 'Top'].every(key =>
      this[key] === other[key] || (Number.isNaN(this[key]) && Number.isNaN(other[key])));
  }
  ToString() { return 'netDxf.Objects.PaperMargin'; }
}
