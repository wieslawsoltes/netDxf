// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export const MTextBackgroundFillFlags = Object.freeze({"None":0,"UseColor":1,"UseDrawingWindowColor":2,"TextFrame":16});

export class MTextBackgroundFill {
  // C# backing state is prefixed with $; public members retain their original names.
  $flags = 1;
  $scaleFactor = 1.5;
  $colorIndex = 7;
  $trueColor = null;
  $colorName = null;
  $property_Transparency = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) return;
    throw new ArgumentException("No matching MTextBackgroundFill constructor. Use MTextBackgroundFill.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    if (signature === '' && args.length === 0) return new MTextBackgroundFill();
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  get Flags() {
    return this.$flags;
  }
  set Flags(value) {
    if ((((Int32(value) & (~19))) !== 0))
    throw new Errors.ArgumentOutOfRangeException("value", value, "Undefined MTEXT background flag bits.");
    this.$flags = value;
  }
  get ScaleFactor() {
    return this.$scaleFactor;
  }
  set ScaleFactor(value) {
    if (((value !== null) && (((Number.isNaN(value) || (value === Infinity || value === -Infinity)) || (value <= 0)))))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The fill-box scale must be positive and finite.");
    this.$scaleFactor = value;
  }
  get ColorIndex() {
    return this.$colorIndex;
  }
  set ColorIndex(value) {
    if (((value !== null) && (((value < 0) || (value > 256)))))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The color index must be in the range 0 through 256.");
    this.$colorIndex = value;
  }
  get TrueColor() {
    return this.$trueColor;
  }
  set TrueColor(value) {
    this.$trueColor = value;
  }
  get ColorName() {
    return this.$colorName;
  }
  set ColorName(value) {
    if (((value !== null) && (NativeString.IndexOfAny(value, ["\u0000", "\r", "\n"]) >= 0)))
    throw new Errors.ArgumentException("A background color name cannot contain NUL or line terminators.", "value");
    this.$colorName = value;
  }
  get Transparency() {
    return this.$property_Transparency;
  }
  set Transparency(value) {
    this.$property_Transparency = value;
  }
  static FromColor(color, scaleFactor = 1.5) {
    if ((color === null))
    throw new Errors.ArgumentNullException("color");
    return Init(new MTextBackgroundFill(), $new => { $new.ScaleFactor = scaleFactor; $new.ColorIndex = color.Index; $new.TrueColor = (color.UseTrueColor ? (((((color.R << 16)) | ((color.G << 8))) | color.B)) : null); });
  }
  static FromDrawingWindow(scaleFactor = 1.5) {
    return Init(new MTextBackgroundFill(), $new => { $new.Flags = (1 | 2); $new.ScaleFactor = scaleFactor; $new.ColorIndex = 0; });
  }
  static CreateTextFrame() {
    return Init(new MTextBackgroundFill(), $new => { $new.Flags = 16; $new.ScaleFactor = null; $new.ColorIndex = null; });
  }
  Clone() {
    return MemberwiseClone(this);
  }
}
