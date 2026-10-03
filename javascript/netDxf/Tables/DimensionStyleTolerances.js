// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { InstallDimensionFields, DimensionChecks as Check } from '../../runtime/DimensionStyleFields.js';
import { DimensionStyleTolerancesDisplayMethod } from './DimensionStyleTolerancesDisplayMethod.js';
import { DimensionStyleTolerancesVerticalPlacement } from './DimensionStyleTolerancesVerticalPlacement.js';
export class DimensionStyleTolerances {
  constructor() { initialize(this); }
  Clone() {
    const copy = new DimensionStyleTolerances();
    for (const name of ["DisplayMethod", "UpperLimit", "LowerLimit", "VerticalPlacement", "Precision", "SuppressLinearLeadingZeros", "SuppressLinearTrailingZeros", "SuppressZeroFeet", "SuppressZeroInches", "AlternatePrecision", "AlternateSuppressLinearLeadingZeros", "AlternateSuppressLinearTrailingZeros", "AlternateSuppressZeroFeet", "AlternateSuppressZeroInches"]) copy[name] = this[name];
    return copy;
  }
}
const initialize = InstallDimensionFields(DimensionStyleTolerances, {
  DisplayMethod: [() => DimensionStyleTolerancesDisplayMethod.None, false],
  UpperLimit: [() => 0.0, true],
  LowerLimit: [() => 0.0, true],
  VerticalPlacement: [() => DimensionStyleTolerancesVerticalPlacement.Middle, false],
  Precision: [() => 4, false, Check.Nonnegative],
  SuppressLinearLeadingZeros: [() => false, false],
  SuppressLinearTrailingZeros: [() => false, false],
  SuppressZeroFeet: [() => true, false],
  SuppressZeroInches: [() => true, false],
  AlternatePrecision: [() => 2, false, Check.Nonnegative],
  AlternateSuppressLinearLeadingZeros: [() => false, false],
  AlternateSuppressLinearTrailingZeros: [() => false, false],
  AlternateSuppressZeroFeet: [() => true, false],
  AlternateSuppressZeroInches: [() => true, false],
});
