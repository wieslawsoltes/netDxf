// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class MTextFormattingOptions {
  // C# backing state is prefixed with $; public members retain their original names.
  $superSubScriptHeightFactor = 0;
  $bold = false;
  $italic = false;
  $overline = false;
  $underline = false;
  $strikeThrough = false;
  $superscript = false;
  $subscript = false;
  $color = null;
  $fontName = null;
  $heightFactor = 0;
  $obliqueAngle = 0;
  $characterSpaceFactor = 0;
  $widthFactor = 0;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching MTextFormattingOptions constructor. Use MTextFormattingOptions.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$bold = false;
    this.$italic = false;
    this.$overline = false;
    this.$underline = false;
    this.$color = null;
    this.$fontName = null;
    this.$heightFactor = 1;
    this.$obliqueAngle = 0;
    this.$characterSpaceFactor = 1;
    this.$widthFactor = 1;
    this.$superSubScriptHeightFactor = 0.7;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return MTextFormattingOptions.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new MTextFormattingOptions(ConstructorTag, 0, args); }
  get Bold() {
    return this.$bold;
  }
  set Bold(value) {
    this.$bold = value;
  }
  get Italic() {
    return this.$italic;
  }
  set Italic(value) {
    this.$italic = value;
  }
  get Overline() {
    return this.$overline;
  }
  set Overline(value) {
    this.$overline = value;
  }
  get Underline() {
    return this.$underline;
  }
  set Underline(value) {
    this.$underline = value;
  }
  get StrikeThrough() {
    return this.$strikeThrough;
  }
  set StrikeThrough(value) {
    this.$strikeThrough = value;
  }
  get Superscript() {
    return this.$superscript;
  }
  set Superscript(value) {
    if (value)
    this.$subscript = false;
    this.$superscript = value;
  }
  get Subscript() {
    return this.$subscript;
  }
  set Subscript(value) {
    if (value)
    this.$superscript = false;
    this.$subscript = value;
  }
  get SuperSubScriptHeightFactor() {
    return this.$superSubScriptHeightFactor;
  }
  set SuperSubScriptHeightFactor(value) {
    if ((value <= 0))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The character percentage height must be greater than zero.");
    this.$superSubScriptHeightFactor = value;
  }
  get Color() {
    return this.$color;
  }
  set Color(value) {
    this.$color = value;
  }
  get FontName() {
    return this.$fontName;
  }
  set FontName(value) {
    this.$fontName = value;
  }
  get HeightFactor() {
    return this.$heightFactor;
  }
  set HeightFactor(value) {
    if ((value <= 0))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The character percentage height must be greater than zero.");
    this.$heightFactor = value;
  }
  get ObliqueAngle() {
    return this.$obliqueAngle;
  }
  set ObliqueAngle(value) {
    if (((value < (-85)) || (value > 85)))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The oblique angle valid values range from -85 to 85.");
    this.$obliqueAngle = value;
  }
  get CharacterSpaceFactor() {
    return this.$characterSpaceFactor;
  }
  set CharacterSpaceFactor(value) {
    if (((value < 0.75) || (value > 4)))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The character space valid values range from a minimum of .75 to 4");
    this.$characterSpaceFactor = value;
  }
  get WidthFactor() {
    return this.$widthFactor;
  }
  set WidthFactor(value) {
    if ((value <= 0))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The width factor should be greater than zero.");
    this.$widthFactor = value;
  }
}
