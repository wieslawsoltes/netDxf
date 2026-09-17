// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Vector2 } from '../Vector2.js';
import { Copy, Format } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentOutOfRangeException, NullReferenceException, RequireInteger } from '../../runtime/Errors.js';
function width(value) {
  // Same guard as the source's internal Polyline2D.ValidateWidth.
  if (!Number.isFinite(value) || value < 0) throw new ArgumentOutOfRangeException('value', value);
}
export class Polyline2DVertex {
  #position; #startWidth = 0; #endWidth = 0; #hasStartWidth = false; #hasEndWidth = false; #identifier = null; Bulge = 0;
  constructor(...args) {
    if (args.length === 1 && args[0] === null) throw new NullReferenceException();
    if (args.length === 1 && args[0] instanceof Polyline2DVertex) {
      const v = args[0]; this.#position = v.Position; this.Bulge = v.Bulge;
      this.#startWidth = v.#startWidth; this.#endWidth = v.#endWidth; this.#hasStartWidth = v.#hasStartWidth; this.#hasEndWidth = v.#hasEndWidth; this.#identifier = v.VertexIdentifier;
    } else if (args.length === 0) this.#position = Vector2.Zero;
    else if ((args.length === 1 || args.length === 2) && args[0] instanceof Vector2 && (args.length === 1 || typeof args[1] === 'number')) {
      this.#position = Copy(args[0]); this.Bulge = args.length === 1 ? 0 : args[1];
    } else if ((args.length === 2 || args.length === 3) && args.every(v => typeof v === 'number')) {
      this.#position = new Vector2(args[0], args[1]); this.Bulge = args.length === 2 ? 0 : args[2];
    } else throw new ArgumentException('No matching Polyline2DVertex constructor.');
  }
  get Position() { return Copy(this.#position); } set Position(value) { this.#position = Copy(value); }
  get StartWidth() { return this.#startWidth; } set StartWidth(value) { width(value); this.#startWidth = value; this.#hasStartWidth = true; }
  get EndWidth() { return this.#endWidth; } set EndWidth(value) { width(value); this.#endWidth = value; this.#hasEndWidth = true; }
  get StartWidthOverride() { return this.#hasStartWidth ? this.#startWidth : null; }
  set StartWidthOverride(value) { if (value !== null) this.StartWidth = value; else { this.#startWidth = 0; this.#hasStartWidth = false; } }
  get EndWidthOverride() { return this.#hasEndWidth ? this.#endWidth : null; }
  set EndWidthOverride(value) { if (value !== null) this.EndWidth = value; else { this.#endWidth = 0; this.#hasEndWidth = false; } }
  get VertexIdentifier() { return this.#identifier; }
  set VertexIdentifier(value) { this.#identifier = value === null ? null : RequireInteger(value, -2147483648, 2147483647); }
  ToString() { return Format('{0}: ({1})', 'Polyline2DVertex', this.#position); }
  Clone() { return new Polyline2DVertex(this); }
}
