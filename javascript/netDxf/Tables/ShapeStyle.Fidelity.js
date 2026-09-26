// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ArgumentException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
import { TextStyleFlags } from './TextStyleFlags.js';
export function InstallShapeStyleFidelity(Type) {
  Object.defineProperties(Type.prototype, {
    Flags: { get() { return this.$flags; }, set(value) {
      if ((value & TextStyleFlags.Shape) === 0) throw new ArgumentException('A ShapeStyle requires the shape flag.', 'value'); this.$flags = value;
    } },
    TextGenerationFlags: { get() { return this.$generation; }, set(value) { this.$generation = RequireInteger(value, -32768, 32767); } },
    LastHeight: { get() { return this.$lastHeight; }, set(value) {
      if (value !== null && !Number.isFinite(value)) throw new ArgumentOutOfRangeException('value'); this.$lastHeight = value;
    } }
  });
}
