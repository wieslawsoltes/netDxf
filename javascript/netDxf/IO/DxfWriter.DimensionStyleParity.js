// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DimensionStyle } from '../Tables/DimensionStyle.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { FormatException } from '../../runtime/Errors.js';
export function IsStoredDimensionHeader(name) {
  return ['$DIMTSZ', '$DIMTVP', '$DIMUPT'].some(value => OrdinalIgnoreCaseEquals(name, value));
}
export function ValidateStoredDimensionHeader(variable) {
  const value = variable.Value;
  if (OrdinalIgnoreCaseEquals(variable.Name, '$DIMUPT')) {
    if (variable.GroupCode !== 70 || !(value instanceof BoxedScalar) || value.Type !== 'Int16'
      || (value.Value !== 0 && value.Value !== 1))
      throw new FormatException('$DIMUPT requires group 70 with a short value of zero or one.');
  } else {
    const number = value instanceof BoxedScalar && value.Type === 'Double' ? value.Value : value;
    if (variable.GroupCode !== 40 || typeof number !== 'number')
      throw new FormatException(variable.Name + ' requires group 40 with a double value.');
    DimensionStyle.ValidateStoredDimensionReal(number, OrdinalIgnoreCaseEquals(variable.Name, '$DIMTSZ'), 'variable');
  }
}
export function ValidateStoredDimensionHeaders(document) {
  for (const variable of document.DrawingVariables.CustomValues())
    if (IsStoredDimensionHeader(variable.Name)) ValidateStoredDimensionHeader(variable);
}
export function WriteStoredDimensionHeader(chunk, document, name, code, fallback) {
  const output = {};
  const value = document.DrawingVariables.TryGetCustomVariable(name, output) ? output.value.Value : fallback;
  chunk.Write(9, name); chunk.Write(code, value instanceof BoxedScalar ? value.Value : value);
}
export function WriteStoredDimensionHeaders(chunk, document, style) {
  WriteStoredDimensionHeader(chunk, document, '$DIMTSZ', 40, style.TickSize);
  WriteStoredDimensionHeader(chunk, document, '$DIMTVP', 40, style.TextVerticalPosition);
  WriteStoredDimensionHeader(chunk, document, '$DIMUPT', 70, new BoxedScalar('Int16', style.UserPositionedText ? 1 : 0));
}
