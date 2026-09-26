// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { InstallDimensionFields, DimensionChecks as Check } from '../../runtime/DimensionStyleFields.js';
import { AciColor } from '../AciColor.js';
import { AngleUnitType } from '../Units/AngleUnitType.js';
import { DimensionStyleAlternateUnits } from './DimensionStyleAlternateUnits.js';
import { DimensionStyleFitOptions } from './DimensionStyleFitOptions.js';
import { DimensionStyleFitTextMove } from './DimensionStyleFitTextMove.js';
import { DimensionStyleTextDirection } from './DimensionStyleTextDirection.js';
import { DimensionStyleTextHorizontalPlacement } from './DimensionStyleTextHorizontalPlacement.js';
import { DimensionStyleTextVerticalPlacement } from './DimensionStyleTextVerticalPlacement.js';
import { DimensionStyleTolerances } from './DimensionStyleTolerances.js';
import { FractionFormatType } from '../Units/FractionFormatType.js';
import { LinearUnitType } from '../Units/LinearUnitType.js';
import { Linetype } from './Linetype.js';
import { Lineweight } from '../Lineweight.js';
import { TextStyle } from './TextStyle.js';
import { TableObject } from './TableObject.js';
import { TableObjectChangedEventArgs } from './TableObjectChangedEventArgs.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentException, ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
import { InstallStoredSettings } from './DimensionStyle.StoredSettings.js';
const required = value => { if (value == null) throw new NullReferenceException(); return value; };
export class DimensionStyle extends TableObject {
  constructor(name, checkName = true) {
    super(name, DxfObjectCode.DimStyle, checkName);
    if (name == null || name === '') throw new ArgumentNullException('name');
    this.IsReserved = OrdinalIgnoreCaseEquals(name, DimensionStyle.DefaultName);
    for (const event of ['LinetypeChanged', 'TextStyleChanged', 'BlockChanged'])
      Object.defineProperty(this, event, { value: new EventHook(), enumerable: true });
    initialize(this);
  }
  static CreateOverload(signature, ...args) {
    if (signature === 'string' || signature === 'string,bool') return new DimensionStyle(...args);
    throw new ArgumentException('Unknown dimension-style constructor signature.', 'signature');
  }
  static get DefaultName() { return 'Standard'; }
  static get Default() { return new DimensionStyle(DimensionStyle.DefaultName); }
  static get Iso25() {
    const style = new DimensionStyle('ISO-25');
    Object.assign(style, { DimBaselineSpacing: 3.75, ExtLineExtend: 1.25, ExtLineOffset: 0.625,
      ArrowSize: 2.5, CenterMarkSize: 2.5, TextHeight: 2.5, TextOffset: 0.625,
      TextOutsideAlign: true, TextInsideAlign: true, TextVerticalPlacement: DimensionStyleTextVerticalPlacement.Above,
      FitDimLineForce: true, DecimalSeparator: ',', LengthPrecision: 2, SuppressLinearTrailingZeros: true });
    Object.assign(style.AlternateUnits, { LengthPrecision: 3, Multiplier: 0.0394 });
    Object.assign(style.Tolerances, { VerticalPlacement: 0, Precision: 2,
      SuppressLinearTrailingZeros: true, AlternatePrecision: 3 });
    return style;
  }
  OnLinetypeChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.LinetypeChanged.Invoke(this, e); return e.NewValue;
  }
  OnTextStyleChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.TextStyleChanged.Invoke(this, e); return e.NewValue;
  }
  OnBlockChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.BlockChanged.Invoke(this, e); return e.NewValue;
  }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner === null ? null : this.Owner.GetReferences(this.Name); }
  Clone(newName = this.Name) {
    const copy = new DimensionStyle(newName);
    copy.DimLineColor = required(this.DimLineColor).Clone();
    copy.DimLineLinetype = required(this.DimLineLinetype).Clone();
    copy.DimLineLineweight = this.DimLineLineweight;
    copy.DimLine1Off = this.DimLine1Off;
    copy.DimLine2Off = this.DimLine2Off;
    copy.DimBaselineSpacing = this.DimBaselineSpacing;
    copy.DimLineExtend = this.DimLineExtend;
    copy.ExtLineColor = required(this.ExtLineColor).Clone();
    copy.ExtLine1Linetype = required(this.ExtLine1Linetype).Clone();
    copy.ExtLine2Linetype = required(this.ExtLine2Linetype).Clone();
    copy.ExtLineLineweight = this.ExtLineLineweight;
    copy.ExtLine1Off = this.ExtLine1Off;
    copy.ExtLine2Off = this.ExtLine2Off;
    copy.ExtLineOffset = this.ExtLineOffset;
    copy.ExtLineExtend = this.ExtLineExtend;
    copy.ExtLineFixed = this.ExtLineFixed;
    copy.ExtLineFixedLength = this.ExtLineFixedLength;
    copy.ArrowSize = this.ArrowSize;
    copy.CenterMarkSize = this.CenterMarkSize;
    copy.TickSize = this.TickSize;
    copy.LeaderArrow = this.LeaderArrow?.Clone() ?? null;
    copy.DimArrow1 = this.DimArrow1?.Clone() ?? null;
    copy.DimArrow2 = this.DimArrow2?.Clone() ?? null;
    copy.TextStyle = required(this.TextStyle).Clone();
    copy.TextColor = required(this.TextColor).Clone();
    copy.TextFillColor = this.TextFillColor?.Clone() ?? null;
    copy.TextHeight = this.TextHeight;
    copy.TextHorizontalPlacement = this.TextHorizontalPlacement;
    copy.TextVerticalPlacement = this.TextVerticalPlacement;
    copy.TextOffset = this.TextOffset;
    copy.TextFractionHeightScale = this.TextFractionHeightScale;
    copy.TextInsideAlign = this.TextInsideAlign;
    copy.TextOutsideAlign = this.TextOutsideAlign;
    copy.TextDirection = this.TextDirection;
    copy.TextVerticalPosition = this.TextVerticalPosition;
    copy.FitDimLineForce = this.FitDimLineForce;
    copy.FitDimLineInside = this.FitDimLineInside;
    copy.DimScaleOverall = this.DimScaleOverall;
    copy.FitOptions = this.FitOptions;
    copy.FitTextInside = this.FitTextInside;
    copy.FitTextMove = this.FitTextMove;
    copy.UserPositionedText = this.UserPositionedText;
    copy.AngularPrecision = this.AngularPrecision;
    copy.LengthPrecision = this.LengthPrecision;
    copy.DimPrefix = this.DimPrefix;
    copy.DimSuffix = this.DimSuffix;
    copy.DecimalSeparator = this.DecimalSeparator;
    copy.DimScaleLinear = this.DimScaleLinear;
    copy.DimLengthUnits = this.DimLengthUnits;
    copy.DimAngularUnits = this.DimAngularUnits;
    copy.FractionType = this.FractionType;
    copy.SuppressLinearLeadingZeros = this.SuppressLinearLeadingZeros;
    copy.SuppressLinearTrailingZeros = this.SuppressLinearTrailingZeros;
    copy.SuppressZeroFeet = this.SuppressZeroFeet;
    copy.SuppressZeroInches = this.SuppressZeroInches;
    copy.SuppressAngularLeadingZeros = this.SuppressAngularLeadingZeros;
    copy.SuppressAngularTrailingZeros = this.SuppressAngularTrailingZeros;
    copy.DimRoundoff = this.DimRoundoff;
    copy.AlternateUnits = required(this.AlternateUnits).Clone();
    copy.Tolerances = required(this.Tolerances).Clone();
    for (const data of this.XData.Values) copy.XData.Add(data.Clone());
    return copy;
  }
}
const initialize = InstallDimensionFields(DimensionStyle, {
  DimLineColor: [() => AciColor.ByBlock, false, Check.Required],
  DimLineLinetype: [() => Linetype.ByBlock, false, Check.LinetypeResource],
  DimLineLineweight: [() => Lineweight.ByBlock, false],
  DimLine1Off: [() => false, false],
  DimLine2Off: [() => false, false],
  DimLineExtend: [() => 0.0, true, Check.Nonnegative],
  DimBaselineSpacing: [() => 0.38, true, Check.Nonnegative],
  ExtLineColor: [() => AciColor.ByBlock, false, Check.Required],
  ExtLine1Linetype: [() => Linetype.ByBlock, false, Check.LinetypeResource],
  ExtLine2Linetype: [() => Linetype.ByBlock, false, Check.LinetypeResource],
  ExtLineLineweight: [() => Lineweight.ByBlock, false],
  ExtLine1Off: [() => false, false],
  ExtLine2Off: [() => false, false],
  ExtLineOffset: [() => 0.0625, true, Check.Nonnegative],
  ExtLineExtend: [() => 0.18, true, Check.Nonnegative],
  ExtLineFixed: [() => false, false],
  ExtLineFixedLength: [() => 1.0, true, Check.Nonnegative],
  DimArrow1: [() => null, false, Check.BlockResource],
  DimArrow2: [() => null, false, Check.BlockResource],
  LeaderArrow: [() => null, false, Check.BlockResource],
  ArrowSize: [() => 0.18, true, Check.Nonnegative],
  CenterMarkSize: [() => 0.09, true],
  TextStyle: [() => TextStyle.Default, false, Check.TextStyleResource],
  TextColor: [() => AciColor.ByBlock, false, Check.Required],
  TextFillColor: [() => null, false],
  TextHeight: [() => 0.18, true, Check.Positive],
  TextHorizontalPlacement: [() => DimensionStyleTextHorizontalPlacement.Centered, false],
  TextVerticalPlacement: [() => DimensionStyleTextVerticalPlacement.Centered, false],
  TextOffset: [() => 0.09, true],
  TextInsideAlign: [() => false, false],
  TextOutsideAlign: [() => false, false],
  TextDirection: [() => DimensionStyleTextDirection.LeftToRight, false],
  TextFractionHeightScale: [() => 1.0, true, Check.Positive],
  FitDimLineForce: [() => false, false],
  FitDimLineInside: [() => true, false],
  DimScaleOverall: [() => 1.0, true, Check.Positive],
  FitOptions: [() => DimensionStyleFitOptions.BestFit, false],
  FitTextInside: [() => false, false],
  FitTextMove: [() => DimensionStyleFitTextMove.BesideDimLine, false],
  AngularPrecision: [() => 0, false, Check.AtLeastMinusOne],
  LengthPrecision: [() => 4, false, Check.Nonnegative],
  DimPrefix: [() => '', false, Check.EmptyString],
  DimSuffix: [() => '', false, Check.EmptyString],
  DecimalSeparator: [() => '.', false],
  DimScaleLinear: [() => 1.0, true, Check.Nonzero],
  DimLengthUnits: [() => LinearUnitType.Decimal, false],
  DimAngularUnits: [() => AngleUnitType.DecimalDegrees, false, Check.AngularUnits],
  FractionType: [() => FractionFormatType.Horizontal, false],
  SuppressLinearLeadingZeros: [() => false, false],
  SuppressLinearTrailingZeros: [() => false, false],
  SuppressZeroFeet: [() => true, false],
  SuppressZeroInches: [() => true, false],
  SuppressAngularLeadingZeros: [() => false, false],
  SuppressAngularTrailingZeros: [() => false, false],
  DimRoundoff: [() => 0.0, true, Check.Roundoff],
  AlternateUnits: [() => new DimensionStyleAlternateUnits(), false, Check.Required],
  Tolerances: [() => new DimensionStyleTolerances(), false, Check.Required],
});
InstallStoredSettings(DimensionStyle);
