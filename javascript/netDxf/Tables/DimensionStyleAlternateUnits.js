// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { InstallDimensionFields, DimensionChecks as Check } from '../../runtime/DimensionStyleFields.js';
import { LinearUnitType } from '../Units/LinearUnitType.js';
export class DimensionStyleAlternateUnits {
  constructor() { initialize(this); }
  Clone() {
    const copy = new DimensionStyleAlternateUnits();
    for (const name of ["Enabled", "LengthUnits", "StackUnits", "LengthPrecision", "Multiplier", "Roundoff", "Prefix", "Suffix", "SuppressLinearLeadingZeros", "SuppressLinearTrailingZeros", "SuppressZeroFeet", "SuppressZeroInches"]) copy[name] = this[name];
    return copy;
  }
}
const initialize = InstallDimensionFields(DimensionStyleAlternateUnits, {
  Enabled: [() => false, false],
  LengthPrecision: [() => 2, false, Check.Nonnegative],
  Prefix: [() => '', false, Check.EmptyString],
  Suffix: [() => '', false, Check.EmptyString],
  Multiplier: [() => 25.4, true, Check.Positive],
  LengthUnits: [() => LinearUnitType.Decimal, false],
  StackUnits: [() => false, false],
  SuppressLinearLeadingZeros: [() => false, false],
  SuppressLinearTrailingZeros: [() => false, false],
  SuppressZeroFeet: [() => true, false],
  SuppressZeroInches: [() => true, false],
  Roundoff: [() => 0.0, true, Check.Roundoff],
});
