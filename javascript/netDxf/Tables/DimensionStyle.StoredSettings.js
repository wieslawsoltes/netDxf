// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { InstallDimensionFields } from '../../runtime/DimensionStyleFields.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
/** Original partial-class stored settings, joined to the main DimensionStyle type. */
export function InstallStoredSettings(DimensionStyle) {
  DimensionStyle.ValidateStoredDimensionReal = function(value, nonnegative, parameter) {
    if (!Number.isFinite(value) || (nonnegative && value < 0)) throw new ArgumentOutOfRangeException(parameter, value);
  };
  const checked = nonnegative => value => { DimensionStyle.ValidateStoredDimensionReal(value, nonnegative, 'value'); return value; };
  InstallDimensionFields(DimensionStyle, {
    TickSize: [() => 0, true, checked(true)],
    TextVerticalPosition: [() => 0, true, checked(false)],
    UserPositionedText: [() => false, false],
  });
}
