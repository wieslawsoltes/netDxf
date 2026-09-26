import {BoxedBoolean} from '../../runtime/BoxedBoolean.js';
import {BoxedString} from '../../runtime/BoxedString.js';
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { AciColor } from '../AciColor.js';
import { Block } from '../Blocks/Block.js';
import { Linetype } from './Linetype.js';
import { TextStyle } from './TextStyle.js';
import { DimensionStyle } from './DimensionStyle.js';
import { DimensionStyleOverrideType } from './DimensionStyleOverrideType.js';
import { DimensionChecks } from '../../runtime/DimensionStyleFields.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { BoxedChar } from '../../runtime/BoxedChar.js';
import { HeaderEnum } from '../../runtime/HeaderBox.js';
import { Format } from '../../runtime/GeometryRuntime.js';
import { ArgumentException } from '../../runtime/Errors.js';
// Resolve cyclic model imports only when validating an override, not during ESM
// initialization. This preserves deep Block imports as well as the package entry.
const references = {
  get AciColor() { return AciColor; }, get Block() { return Block; },
  get Linetype() { return Linetype; }, get TextStyle() { return TextStyle; }
};
const rules = {
  TickSize: ["double", false, "StoredNonnegative"],
  TextVerticalPosition: ["double", false, "StoredReal"],
  UserPositionedText: ["bool", false, ""],
  DimLineColor: ["AciColor", false, ""],
  DimLineLinetype: ["Linetype", false, ""],
  DimLineLineweight: ["Lineweight", false, ""],
  DimLine1Off: ["bool", false, ""],
  DimLine2Off: ["bool", false, ""],
  DimLineExtend: ["double", false, "Nonnegative"],
  ExtLineColor: ["AciColor", false, ""],
  ExtLine1Linetype: ["Linetype", false, ""],
  ExtLine2Linetype: ["Linetype", false, ""],
  ExtLineLineweight: ["Lineweight", false, ""],
  ExtLine1Off: ["bool", false, ""],
  ExtLine2Off: ["bool", false, ""],
  ExtLineOffset: ["double", false, "Nonnegative"],
  ExtLineExtend: ["double", false, "Nonnegative"],
  ExtLineFixed: ["bool", false, ""],
  ExtLineFixedLength: ["double", false, "Nonnegative"],
  ArrowSize: ["double", false, "Nonnegative"],
  CenterMarkSize: ["double", false, ""],
  LeaderArrow: ["Block", true, ""],
  DimArrow1: ["Block", true, ""],
  DimArrow2: ["Block", true, ""],
  TextStyle: ["TextStyle", false, ""],
  TextColor: ["AciColor", false, ""],
  TextFillColor: ["AciColor", true, ""],
  TextHeight: ["double", false, "Positive"],
  TextOffset: ["double", false, ""],
  TextVerticalPlacement: ["DimensionStyleTextVerticalPlacement", false, ""],
  TextHorizontalPlacement: ["DimensionStyleTextHorizontalPlacement", false, ""],
  TextInsideAlign: ["bool", false, ""],
  TextOutsideAlign: ["bool", false, ""],
  TextDirection: ["DimensionStyleTextDirection", false, ""],
  TextFractionHeightScale: ["double", false, "Positive"],
  FitDimLineForce: ["bool", false, ""],
  FitDimLineInside: ["bool", false, ""],
  DimScaleOverall: ["double", false, "Nonnegative"],
  FitOptions: ["DimensionStyleFitOptions", false, ""],
  FitTextInside: ["bool", false, ""],
  FitTextMove: ["DimensionStyleFitTextMove", false, ""],
  AngularPrecision: ["short", false, "AtLeastMinusOne"],
  LengthPrecision: ["short", false, "Nonnegative"],
  DimPrefix: ["string", false, ""],
  DimSuffix: ["string", false, ""],
  DecimalSeparator: ["char", false, ""],
  DimScaleLinear: ["double", false, "Nonzero"],
  DimLengthUnits: ["LinearUnitType", false, ""],
  DimAngularUnits: ["AngleUnitType", false, ""],
  FractionalType: ["FractionFormatType", false, ""],
  SuppressLinearLeadingZeros: ["bool", false, ""],
  SuppressLinearTrailingZeros: ["bool", false, ""],
  SuppressAngularLeadingZeros: ["bool", false, ""],
  SuppressAngularTrailingZeros: ["bool", false, ""],
  SuppressZeroFeet: ["bool", false, ""],
  SuppressZeroInches: ["bool", false, ""],
  DimRoundoff: ["double", false, "Roundoff"],
  AltUnitsEnabled: ["bool", false, ""],
  AltUnitsLengthUnits: ["LinearUnitType", false, ""],
  AltUnitsStackedUnits: ["bool", false, ""],
  AltUnitsLengthPrecision: ["short", false, "Nonnegative"],
  AltUnitsMultiplier: ["double", false, "Positive"],
  AltUnitsRoundoff: ["double", false, "Roundoff"],
  AltUnitsPrefix: ["string", false, ""],
  AltUnitsSuffix: ["string", false, ""],
  AltUnitsSuppressLinearLeadingZeros: ["bool", false, ""],
  AltUnitsSuppressLinearTrailingZeros: ["bool", false, ""],
  AltUnitsSuppressZeroFeet: ["bool", false, ""],
  AltUnitsSuppressZeroInches: ["bool", false, ""],
  TolerancesDisplayMethod: ["DimensionStyleTolerancesDisplayMethod", false, ""],
  TolerancesUpperLimit: ["double", false, ""],
  TolerancesLowerLimit: ["double", false, ""],
  TolerancesVerticalPlacement: ["DimensionStyleTolerancesVerticalPlacement", false, ""],
  TolerancesPrecision: ["short", false, "Nonnegative"],
  TolerancesSuppressLinearLeadingZeros: ["bool", false, ""],
  TolerancesSuppressLinearTrailingZeros: ["bool", false, ""],
  TolerancesSuppressZeroFeet: ["bool", false, ""],
  TolerancesSuppressZeroInches: ["bool", false, ""],
  TolerancesAlternatePrecision: ["short", false, "Nonnegative"],
  TolerancesAltSuppressLinearLeadingZeros: ["bool", false, ""],
  TolerancesAltSuppressLinearTrailingZeros: ["bool", false, ""],
  TolerancesAltSuppressZeroFeet: ["bool", false, ""],
  TolerancesAltSuppressZeroInches: ["bool", false, ""],
};
const names = new Map(Object.entries(DimensionStyleOverrideType).map(([name, value]) => [value, name]));
function accepts(kind, value) {
  if (kind === 'double') return typeof value === 'number' || value instanceof BoxedScalar && value.Type === 'Double';
  if (kind === 'short') return value instanceof BoxedScalar && value.Type === 'Int16';
  if (kind === 'char') return value instanceof BoxedChar;
  if (kind === 'bool') return typeof value === 'boolean' || value instanceof BoxedBoolean;
  if (kind === 'string') return typeof value === 'string' || value instanceof BoxedString;
  if (references[kind]) return value instanceof references[kind];
  return value instanceof HeaderEnum && value.Type === kind;
}
/** Object-valued overrides retain explicit short/enum/Char boxes, not implicit Number coercions. */
export class DimensionStyleOverride {
  #type; #value;
  constructor(type, value) {
    const rule = rules[names.get(type)];
    if (rule) {
      const [kind, nullable, guard] = rule;
      if (!(nullable && value === null)) {
        if (!accepts(kind, value)) throw new ArgumentException('The dimension override requires a value of type ' + kind + '.', 'value');
        const number = value instanceof BoxedScalar ? value.Value : value;
        if (guard === 'StoredNonnegative' || guard === 'StoredReal')
          DimensionStyle.ValidateStoredDimensionReal(number, guard === 'StoredNonnegative', 'value');
        else if (guard) DimensionChecks[guard](number);
      }
    }
    // Undefined enum members are retained without validation by the original switch.
    this.#type = type; this.#value = value;
  }
  get Type() { return this.#type; }
  get Value() { return this.#value; }
  ToString() { return Format('{0} : {1}', names.get(this.#type) ?? this.#type, this.#value); }
}
