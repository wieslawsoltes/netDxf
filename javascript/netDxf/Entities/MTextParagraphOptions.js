// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { MathHelper } from './../MathHelper.js';

export class MTextParagraphOptions {
  // C# backing state is prefixed with $; public members retain their original names.
  $heightFactor = 0;
  $alignment = 0;
  $verticalAlignment = 0;
  $spaceBefore = 0;
  $spaceAfter = 0;
  $firstLineIndent = 0;
  $leftIndent = 0;
  $rightIndent = 0;
  $lineSpacing = 0;
  $lineSpacingStyle = 0;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching MTextParagraphOptions constructor. Use MTextParagraphOptions.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$heightFactor = 1;
    this.$alignment = 1;
    this.$verticalAlignment = 1;
    this.$spaceBefore = 0;
    this.$spaceAfter = 0;
    this.$firstLineIndent = 0;
    this.$leftIndent = 0;
    this.$rightIndent = 0;
    this.$lineSpacing = 1;
    this.$lineSpacingStyle = 0;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return MTextParagraphOptions.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new MTextParagraphOptions(ConstructorTag, 0, args); }
  get HeightFactor() {
    return this.$heightFactor;
  }
  set HeightFactor(value) {
    if ((value <= 0))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The character percentage height must be greater than zero.");
    this.$heightFactor = value;
  }
  get Alignment() {
    return this.$alignment;
  }
  set Alignment(value) {
    this.$alignment = value;
  }
  get VerticalAlignment() {
    return this.$verticalAlignment;
  }
  set VerticalAlignment(value) {
    this.$verticalAlignment = value;
  }
  get SpacingBefore() {
    return this.$spaceBefore;
  }
  set SpacingBefore(value) {
    if (MathHelper.$IsZero0(value))
    {
      this.$spaceBefore = 0;
    }
    else
    {
      if (((value < 0.25) || (value > 4)))
      throw new Errors.ArgumentOutOfRangeException("value", value, "The paragraph spacing valid values range from 0.25 to 4.0");
      this.$spaceBefore = value;
    }
  }
  get SpacingAfter() {
    return this.$spaceAfter;
  }
  set SpacingAfter(value) {
    if (MathHelper.$IsZero0(value))
    {
      this.$spaceAfter = 0;
    }
    else
    {
      if (((value < 0.25) || (value > 4)))
      throw new Errors.ArgumentOutOfRangeException("value", value, "The paragraph spacing valid values range from 0.25 to 4.0");
      this.$spaceAfter = value;
    }
  }
  get FirstLineIndent() {
    return this.$firstLineIndent;
  }
  set FirstLineIndent(value) {
    if (((value < (-10000)) || (value > 10000)))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The paragraph indent valid values range from -10000.0 to 10000.0");
    this.$firstLineIndent = value;
  }
  get LeftIndent() {
    return this.$leftIndent;
  }
  set LeftIndent(value) {
    if (((value < 0) || (value > 10000)))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The paragraph indent valid values range from 0.0 to 10000.0");
    this.$leftIndent = value;
  }
  get RightIndent() {
    return this.$rightIndent;
  }
  set RightIndent(value) {
    if (((value < 0) || (value > 10000)))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The paragraph indent valid values range from 0.0 to 10000.0");
    this.$rightIndent = value;
  }
  get LineSpacingFactor() {
    return this.$lineSpacing;
  }
  set LineSpacingFactor(value) {
    if (((value < 0.25) || (value > 4)))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The MText LineSpacingFactor valid values range from 0.25 to 4.0");
    this.$lineSpacing = value;
  }
  get LineSpacingStyle() {
    return this.$lineSpacingStyle;
  }
  set LineSpacingStyle(value) {
    this.$lineSpacingStyle = value;
  }
}
