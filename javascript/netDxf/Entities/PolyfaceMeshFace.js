// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { FixedArray } from '../../runtime/FixedArray.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';

export class PolyfaceMeshFace {
  #vertexIndexes; #layer = null; Color = null;
  constructor(vertexIndexes) {
    Object.defineProperty(this, 'LayerChanged', { value: new EventHook(), enumerable: true });
    if (arguments.length === 0) {
      this.#vertexIndexes = FixedArray([0, 0, 0, 0], value => RequireInteger(value, -32768, 32767));
      return; // The default face is an intentionally uninitialized, editable placeholder.
    }
    if (vertexIndexes == null) throw new ArgumentNullException('vertexIndexes');
    this.#vertexIndexes = FixedArray(vertexIndexes, value => RequireInteger(value, -32768, 32767));
    if (this.#vertexIndexes.length < 1 || this.#vertexIndexes.length > 4) throw new ArgumentOutOfRangeException('vertexIndexes');
    this.ValidateVertexIndexes(2147483647);
  }
  get VertexIndexes() { return this.#vertexIndexes; }
  get Layer() { return this.#layer; }
  set Layer(value) { this.#layer = this.OnLayerChangedEvent(this.#layer, value); }
  OnLayerChangedEvent(oldValue, newValue) {
    const event = new TableObjectChangedEventArgs(oldValue, newValue);
    this.LayerChanged.Invoke(this, event); return event.NewValue;
  }
  ValidateVertexIndexes(vertexCount) {
    let count = 0;
    for (const index of this.#vertexIndexes) {
      if (index === 0) break;
      if (Math.abs(index) > vertexCount) throw new ArgumentOutOfRangeException('VertexIndexes', index);
      count++;
    }
    if (count === 0) throw new ArgumentException('A polyface face must contain at least one active vertex index.', 'VertexIndexes');
    return count;
  }
  ToString() { return 'PolyfaceMeshFace'; }
  Clone() {
    const copy = new PolyfaceMeshFace(this.#vertexIndexes);
    copy.Layer = this.#layer == null ? null : this.#layer.Clone();
    copy.Color = this.Color == null ? null : this.Color.Clone();
    return copy;
  }
}
