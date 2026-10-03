// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { UnitFactors } from '../../runtime/UnitFactors.generated.js';
import { IndexOutOfRangeException, ArgumentException } from '../../runtime/Errors.js';
const imageToDrawing = [0,4,5,6,7,1,2,10,3];
function image(value) { return Number.isInteger(value) && value >= 0 && value < imageToDrawing.length ? imageToDrawing[value] : 0; }

export class UnitHelper {
  static ConvertUnit(value, from, to) { return value * UnitHelper.ConversionFactor(from, to); }
  /** Numeric enum overloads need explicit type names when an ImageUnits argument is used. */
  static ConversionFactor(from, to, fromType = 'DrawingUnits', toType = 'DrawingUnits') {
    if (!['DrawingUnits','ImageUnits'].includes(fromType) || !['DrawingUnits','ImageUnits'].includes(toType) ||
      (fromType === 'ImageUnits' && toType === 'ImageUnits')) throw new ArgumentException('No matching conversion overload.');
    if (fromType === 'ImageUnits') from = image(from);
    if (toType === 'ImageUnits') to = image(to);
    if (from === 0 || to === 0) return 1;
    if (!Number.isInteger(from) || !Number.isInteger(to) || from < 0 || from >= 25 || to < 0 || to >= 25)
      throw new IndexOutOfRangeException('Index was outside the bounds of the array.');
    return UnitFactors[from * 25 + to];
  }
}
