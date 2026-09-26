// Shared storage for dimension styles. Numeric fields retain exact binary64 payloads.
import { BoxedString } from './BoxedString.js';
import { MathHelper } from '../netDxf/MathHelper.js';
import { AngleUnitType } from '../netDxf/Units/AngleUnitType.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from './Errors.js';
const reject = value => { throw new ArgumentOutOfRangeException('value', value); };
export const DimensionChecks = Object.freeze({
  Required(value) { if (value == null) throw new ArgumentNullException('value'); return value; },
  // Typed string fields use text values; object-valued overrides keep their boxes.
  EmptyString(value) { return value instanceof BoxedString ? value.Value : value ?? ''; },
  Nonnegative(value) { if (value < 0) reject(value); return value; },
  AtLeastMinusOne(value) { if (value < -1) reject(value); return value; },
  Positive(value) { if (value <= 0) reject(value); return value; },
  Nonzero(value) { if (MathHelper.IsZero(value)) reject(value); return value; },
  Roundoff(value) { if (value < 0.000001 && !MathHelper.IsZero(value, Number.MIN_VALUE)) reject(value); return value; },
  AngularUnits(value) { if (value === AngleUnitType.SurveyorUnits) throw new ArgumentException("Surveyor's units are not applicable in angular dimensions."); return value; },
  LinetypeResource(value, previous) { return this.OnLinetypeChangedEvent(previous, DimensionChecks.Required(value)); },
  TextStyleResource(value, previous) { return this.OnTextStyleChangedEvent(previous, DimensionChecks.Required(value)); },
  BlockResource(value, previous) { return value == null ? null : this.OnBlockChangedEvent(previous, value); },
});
/** Definitions: property -> [fresh default factory, isReal, optional source guard]. */
export function InstallDimensionFields(Type, definitions) {
  const states = new WeakMap();
  const initialize = instance => {
    if (!states.has(instance)) {
      const state = new Map();
      for (const [key, [fresh, isReal]] of Object.entries(definitions)) {
        const value = fresh();
        if (isReal) { const bytes = new DataView(new ArrayBuffer(8)); bytes.setFloat64(0, value); state.set(key, bytes); }
        else state.set(key, value);
      }
      states.set(instance, state);
    }
    return states.get(instance);
  };
  for (const [key, [, isReal, check]] of Object.entries(definitions)) Object.defineProperty(Type.prototype, key, {
    get() { const value = initialize(this).get(key); return isReal ? value.getFloat64(0) : value; },
    set(value) {
      const state = initialize(this), old = state.get(key);
      const checked = check ? check.call(this, value, isReal ? old.getFloat64(0) : old) : value;
      if (isReal) old.setFloat64(0, checked); else state.set(key, checked);
    },
  });
  return initialize;
}
