// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class UnitStyleFormat {
  // C# backing state is prefixed with $; public members retain their original names.
  $linearDecimalPlaces = 0;
  $angularDecimalPlaces = 0;
  $decimalSeparator = null;
  $feetInchesSeparator = null;
  $degreesSymbol = null;
  $minutesSymbol = null;
  $secondsSymbol = null;
  $radiansSymbol = null;
  $gradiansSymbol = null;
  $feetSymbol = null;
  $inchesSymbol = null;
  $fractionHeightScale = 0;
  $fractionType = 0;
  $suppressLinearLeadingZeros = false;
  $suppressLinearTrailingZeros = false;
  $suppressAngularLeadingZeros = false;
  $suppressAngularTrailingZeros = false;
  $suppressZeroFeet = false;
  $suppressZeroInches = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching UnitStyleFormat constructor. Use UnitStyleFormat.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$linearDecimalPlaces = 2;
    this.$angularDecimalPlaces = 0;
    this.$decimalSeparator = ".";
    this.$feetInchesSeparator = "-";
    this.$degreesSymbol = "\u00B0";
    this.$minutesSymbol = "\u0027";
    this.$secondsSymbol = "\u0022";
    this.$radiansSymbol = "r";
    this.$gradiansSymbol = "g";
    this.$feetSymbol = "\u0027";
    this.$inchesSymbol = "\u0022";
    this.$fractionHeightScale = 1;
    this.$fractionType = 0;
    this.$suppressLinearLeadingZeros = false;
    this.$suppressLinearTrailingZeros = false;
    this.$suppressAngularLeadingZeros = false;
    this.$suppressAngularTrailingZeros = false;
    this.$suppressZeroFeet = true;
    this.$suppressZeroInches = true;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return UnitStyleFormat.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new UnitStyleFormat(ConstructorTag, 0, args); }
  get LinearDecimalPlaces() {
    return this.$linearDecimalPlaces;
  }
  set LinearDecimalPlaces(value) {
    if ((value < 0))
    {
      throw new Errors.ArgumentOutOfRangeException("value", value, "The number of decimal places must be equals or greater than zero.");
    }
    this.$linearDecimalPlaces = value;
  }
  get AngularDecimalPlaces() {
    return this.$angularDecimalPlaces;
  }
  set AngularDecimalPlaces(value) {
    if ((value < 0))
    {
      throw new Errors.ArgumentOutOfRangeException("value", value, "The number of decimal places must be equals or greater than zero.");
    }
    this.$angularDecimalPlaces = value;
  }
  get DecimalSeparator() {
    return this.$decimalSeparator;
  }
  set DecimalSeparator(value) {
    this.$decimalSeparator = value;
  }
  get FeetInchesSeparator() {
    return this.$feetInchesSeparator;
  }
  set FeetInchesSeparator(value) {
    this.$feetInchesSeparator = value;
  }
  get DegreesSymbol() {
    return this.$degreesSymbol;
  }
  set DegreesSymbol(value) {
    this.$degreesSymbol = value;
  }
  get MinutesSymbol() {
    return this.$minutesSymbol;
  }
  set MinutesSymbol(value) {
    this.$minutesSymbol = value;
  }
  get SecondsSymbol() {
    return this.$secondsSymbol;
  }
  set SecondsSymbol(value) {
    this.$secondsSymbol = value;
  }
  get RadiansSymbol() {
    return this.$radiansSymbol;
  }
  set RadiansSymbol(value) {
    this.$radiansSymbol = value;
  }
  get GradiansSymbol() {
    return this.$gradiansSymbol;
  }
  set GradiansSymbol(value) {
    this.$gradiansSymbol = value;
  }
  get FeetSymbol() {
    return this.$feetSymbol;
  }
  set FeetSymbol(value) {
    this.$feetSymbol = value;
  }
  get InchesSymbol() {
    return this.$inchesSymbol;
  }
  set InchesSymbol(value) {
    this.$inchesSymbol = value;
  }
  get FractionHeightScale() {
    return this.$fractionHeightScale;
  }
  set FractionHeightScale(value) {
    if ((value <= 0))
    {
      throw new Errors.ArgumentOutOfRangeException("value", value, "The fraction height scale must be greater than zero.");
    }
    this.$fractionHeightScale = value;
  }
  get FractionType() {
    return this.$fractionType;
  }
  set FractionType(value) {
    this.$fractionType = value;
  }
  get SuppressLinearLeadingZeros() {
    return this.$suppressLinearLeadingZeros;
  }
  set SuppressLinearLeadingZeros(value) {
    this.$suppressLinearLeadingZeros = value;
  }
  get SuppressLinearTrailingZeros() {
    return this.$suppressLinearTrailingZeros;
  }
  set SuppressLinearTrailingZeros(value) {
    this.$suppressLinearTrailingZeros = value;
  }
  get SuppressAngularLeadingZeros() {
    return this.$suppressAngularLeadingZeros;
  }
  set SuppressAngularLeadingZeros(value) {
    this.$suppressAngularLeadingZeros = value;
  }
  get SuppressAngularTrailingZeros() {
    return this.$suppressAngularTrailingZeros;
  }
  set SuppressAngularTrailingZeros(value) {
    this.$suppressAngularTrailingZeros = value;
  }
  get SuppressZeroFeet() {
    return this.$suppressZeroFeet;
  }
  set SuppressZeroFeet(value) {
    this.$suppressZeroFeet = value;
  }
  get SuppressZeroInches() {
    return this.$suppressZeroInches;
  }
  set SuppressZeroInches(value) {
    this.$suppressZeroInches = value;
  }
}
