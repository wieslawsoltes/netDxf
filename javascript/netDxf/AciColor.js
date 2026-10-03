// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { MathHelper } from './MathHelper.js';
import { Vector3 } from './Vector3.js';

export class AciColor {
  // C# backing state is prefixed with $; public members retain their original names.
  static $indexRgb = new List([[255, 255, 255], [255, 0, 0], [255, 255, 0], [0, 255, 0], [0, 255, 255], [0, 0, 255], [255, 0, 255], [255, 255, 255], [128, 128, 128], [192, 192, 192], [255, 0, 0], [255, 127, 127], [165, 0, 0], [165, 82, 82], [127, 0, 0], [127, 63, 63], [76, 0, 0], [76, 38, 38], [38, 0, 0], [38, 19, 19], [255, 63, 0], [255, 159, 127], [165, 41, 0], [165, 103, 82], [127, 31, 0], [127, 79, 63], [76, 19, 0], [76, 47, 38], [38, 9, 0], [38, 28, 19], [255, 127, 0], [255, 191, 127], [165, 82, 0], [165, 124, 82], [127, 63, 0], [127, 95, 63], [76, 38, 0], [76, 57, 38], [38, 19, 0], [38, 28, 19], [255, 191, 0], [255, 223, 127], [165, 124, 0], [165, 145, 82], [127, 95, 0], [127, 111, 63], [76, 57, 0], [76, 66, 38], [38, 28, 0], [38, 33, 19], [255, 255, 0], [255, 255, 127], [165, 165, 0], [165, 165, 82], [127, 127, 0], [127, 127, 63], [76, 76, 0], [76, 76, 38], [38, 38, 0], [38, 38, 19], [191, 255, 0], [223, 255, 127], [124, 165, 0], [145, 165, 82], [95, 127, 0], [111, 127, 63], [57, 76, 0], [66, 76, 38], [28, 38, 0], [33, 38, 19], [127, 255, 0], [191, 255, 127], [82, 165, 0], [124, 165, 82], [63, 127, 0], [95, 127, 63], [38, 76, 0], [57, 76, 38], [19, 38, 0], [28, 38, 19], [63, 255, 0], [159, 255, 127], [41, 165, 0], [103, 165, 82], [31, 127, 0], [79, 127, 63], [19, 76, 0], [47, 76, 38], [9, 38, 0], [23, 38, 19], [0, 255, 0], [125, 255, 127], [0, 165, 0], [82, 165, 82], [0, 127, 0], [63, 127, 63], [0, 76, 0], [38, 76, 38], [0, 38, 0], [19, 38, 19], [0, 255, 63], [127, 255, 159], [0, 165, 41], [82, 165, 103], [0, 127, 31], [63, 127, 79], [0, 76, 19], [38, 76, 47], [0, 38, 9], [19, 88, 23], [0, 255, 127], [127, 255, 191], [0, 165, 82], [82, 165, 124], [0, 127, 63], [63, 127, 95], [0, 76, 38], [38, 76, 57], [0, 38, 19], [19, 88, 28], [0, 255, 191], [127, 255, 223], [0, 165, 124], [82, 165, 145], [0, 127, 95], [63, 127, 111], [0, 76, 57], [38, 76, 66], [0, 38, 28], [19, 88, 88], [0, 255, 255], [127, 255, 255], [0, 165, 165], [82, 165, 165], [0, 127, 127], [63, 127, 127], [0, 76, 76], [38, 76, 76], [0, 38, 38], [19, 88, 88], [0, 191, 255], [127, 223, 255], [0, 124, 165], [82, 145, 165], [0, 95, 127], [63, 111, 217], [0, 57, 76], [38, 66, 126], [0, 28, 38], [19, 88, 88], [0, 127, 255], [127, 191, 255], [0, 82, 165], [82, 124, 165], [0, 63, 127], [63, 95, 127], [0, 38, 76], [38, 57, 126], [0, 19, 38], [19, 28, 88], [0, 63, 255], [127, 159, 255], [0, 41, 165], [82, 103, 165], [0, 31, 127], [63, 79, 127], [0, 19, 76], [38, 47, 126], [0, 9, 38], [19, 23, 88], [0, 0, 255], [127, 127, 255], [0, 0, 165], [82, 82, 165], [0, 0, 127], [63, 63, 127], [0, 0, 76], [38, 38, 126], [0, 0, 38], [19, 19, 88], [63, 0, 255], [159, 127, 255], [41, 0, 165], [103, 82, 165], [31, 0, 127], [79, 63, 127], [19, 0, 76], [47, 38, 126], [9, 0, 38], [23, 19, 88], [127, 0, 255], [191, 127, 255], [165, 0, 82], [124, 82, 165], [63, 0, 127], [95, 63, 127], [38, 0, 76], [57, 38, 126], [19, 0, 38], [28, 19, 88], [191, 0, 255], [223, 127, 255], [124, 0, 165], [142, 82, 165], [95, 0, 127], [111, 63, 127], [57, 0, 76], [66, 38, 76], [28, 0, 38], [88, 19, 88], [255, 0, 255], [255, 127, 255], [165, 0, 165], [165, 82, 165], [127, 0, 127], [127, 63, 127], [76, 0, 76], [76, 38, 76], [38, 0, 38], [88, 19, 88], [255, 0, 191], [255, 127, 223], [165, 0, 124], [165, 82, 145], [127, 0, 95], [127, 63, 111], [76, 0, 57], [76, 38, 66], [38, 0, 28], [88, 19, 88], [255, 0, 127], [255, 127, 191], [165, 0, 82], [165, 82, 124], [127, 0, 63], [127, 63, 95], [76, 0, 38], [76, 38, 57], [38, 0, 19], [88, 19, 28], [255, 0, 63], [255, 127, 159], [165, 0, 41], [165, 82, 103], [127, 0, 31], [127, 63, 79], [76, 0, 19], [76, 38, 47], [38, 0, 9], [88, 19, 23], [0, 0, 0], [101, 101, 101], [102, 102, 102], [153, 153, 153], [204, 204, 204], [255, 255, 255]]);
  $index = 0;
  $r = 0;
  $g = 0;
  $b = 0;
  $useTrueColor = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 1 && (args[0] instanceof Color)) {
      this.$ctor5(...args);
      return;
    }
    if (args.length === 3 && (Number.isInteger(args[0]) && args[0] >= 0 && args[0] <= 255) && (Number.isInteger(args[1]) && args[1] >= 0 && args[1] <= 255) && (Number.isInteger(args[2]) && args[2] >= 0 && args[2] <= 255)) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 3 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number')) {
      this.$ctor4(...args);
      return;
    }
    if (args.length === 1 && (args[0] === null || args[0] instanceof Uint8Array)) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array)) {
      this.$ctor3(...args);
      return;
    }
    if (args.length === 1 && (Number.isInteger(args[0]) && args[0] >= -32768 && args[0] <= 32767)) {
      this.$ctor6(...args);
      return;
    }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching AciColor constructor. Use AciColor.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$ctor6(7);
  }
  $ctor1(rgb) {
    this.$ctor2(GetElement(rgb, 0), GetElement(rgb, 1), GetElement(rgb, 2));
  }
  $ctor2(r, g, b) {
    this.$r = r;
    this.$g = g;
    this.$b = b;
    this.$useTrueColor = true;
    this.$index = AciColor.RgbToAci(this.$r, this.$g, this.$b);
  }
  $ctor3(rgb) {
    this.$ctor4(GetElement(rgb, 0), GetElement(rgb, 1), GetElement(rgb, 2));
  }
  $ctor4(r, g, b) {
    if (((r < 0) || (r > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("r", r, "Red component input values range from 0 to 1.");
    }
    if (((g < 0) || (g > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("g", g, "Green component input values range from 0 to 1.");
    }
    if (((b < 0) || (b > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("b", b, "Blue component input values range from 0 to 1.");
    }
    this.$r = Byte(DotNetMath.Round((r * 255)));
    this.$g = Byte(DotNetMath.Round((g * 255)));
    this.$b = Byte(DotNetMath.Round((b * 255)));
    this.$useTrueColor = true;
    this.$index = AciColor.RgbToAci(this.$r, this.$g, this.$b);
  }
  $ctor5(color) {
    this.$ctor2(color.R, color.G, color.B);
  }
  $ctor6(index) {
    if (((index <= 0) || (index >= 256)))
    {
      throw new Errors.ArgumentOutOfRangeException("index", index, "Accepted color index values range from 1 to 255.");
    }
    let rgb = GetElement(AciColor.IndexRgb, Byte(index));
    this.$r = GetElement(rgb, 0);
    this.$g = GetElement(rgb, 1);
    this.$b = GetElement(rgb, 2);
    this.$useTrueColor = false;
    this.$index = index;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return AciColor.$create0(...args);
    }
    if (signature === "byte[]") {
      if (!(args.length === 1 && (args[0] === null || args[0] instanceof Uint8Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return AciColor.$create1(...args);
    }
    if (signature === "byte,byte,byte") {
      if (!(args.length === 3 && (Number.isInteger(args[0]) && args[0] >= 0 && args[0] <= 255) && (Number.isInteger(args[1]) && args[1] >= 0 && args[1] <= 255) && (Number.isInteger(args[2]) && args[2] >= 0 && args[2] <= 255))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return AciColor.$create2(...args);
    }
    if (signature === "double[]") {
      if (!(args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return AciColor.$create3(...args);
    }
    if (signature === "double,double,double") {
      if (!(args.length === 3 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return AciColor.$create4(...args);
    }
    if (signature === "System.Drawing.Color") {
      if (!(args.length === 1 && (args[0] instanceof Color))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return AciColor.$create5(...args);
    }
    if (signature === "short") {
      if (!(args.length === 1 && (Number.isInteger(args[0]) && args[0] >= -32768 && args[0] <= 32767))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return AciColor.$create6(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new AciColor(ConstructorTag, 0, args); }
  static $create1(...args) { return new AciColor(ConstructorTag, 1, args); }
  static $create2(...args) { return new AciColor(ConstructorTag, 2, args); }
  static $create3(...args) { return new AciColor(ConstructorTag, 3, args); }
  static $create4(...args) { return new AciColor(ConstructorTag, 4, args); }
  static $create5(...args) { return new AciColor(ConstructorTag, 5, args); }
  static $create6(...args) { return new AciColor(ConstructorTag, 6, args); }
  static get ByLayer() {
    return Init(AciColor.$create0(), $new => { $new.$index = 256; });
  }
  static get ByBlock() {
    return Init(AciColor.$create0(), $new => { $new.$index = 0; });
  }
  static get Red() {
    return AciColor.$create6(1);
  }
  static get Yellow() {
    return AciColor.$create6(2);
  }
  static get Green() {
    return AciColor.$create6(3);
  }
  static get Cyan() {
    return AciColor.$create6(4);
  }
  static get Blue() {
    return AciColor.$create6(5);
  }
  static get Magenta() {
    return AciColor.$create6(6);
  }
  static get Default() {
    return AciColor.$create6(7);
  }
  static get DarkGray() {
    return AciColor.$create6(8);
  }
  static get LightGray() {
    return AciColor.$create6(9);
  }
  static get IndexRgb() {
    return AciColor.$indexRgb;
  }
  get IsByLayer() {
    return (this.$index === 256);
  }
  get IsByBlock() {
    return (this.$index === 0);
  }
  get R() {
    return this.$r;
  }
  get G() {
    return this.$g;
  }
  get B() {
    return this.$b;
  }
  get UseTrueColor() {
    return this.$useTrueColor;
  }
  set UseTrueColor(value) {
    this.$useTrueColor = value;
  }
  get Index() {
    return this.$index;
  }
  set Index(value) {
    if (((value <= 0) || (value >= 256)))
    {
      throw new Errors.ArgumentOutOfRangeException("value", value, "Accepted color index values range from 1 to 255.");
    }
    this.$index = value;
    let rgb = GetElement(AciColor.IndexRgb, Byte(this.$index));
    this.$r = GetElement(rgb, 0);
    this.$g = GetElement(rgb, 1);
    this.$b = GetElement(rgb, 2);
    this.$useTrueColor = false;
  }
  static RgbToAci(r, g, b) {
    let prevDist = 2147483647;
    let index = 0;
    for (let i = 1; (i < 256); (i++))
    {
      let color = GetElement(AciColor.IndexRgb, i);
      let red = (r - GetElement(color, 0));
      let green = (g - GetElement(color, 1));
      let blue = (b - GetElement(color, 2));
      let dist = (((red * red) + (green * green)) + (blue * blue));
      if ((dist === 0))
      {
        return Byte(i);
      }
      if ((dist < prevDist))
      {
        prevDist = dist;
        index = Byte(i);
      }
    }
    return index;
  }
  static $FromHsl0(hsl) {
    return AciColor.$FromHsl1(hsl.X, hsl.Y, hsl.Z);
  }
  static $FromHsl1(hue, saturation, lightness) {
    if (((hue < 0) || (hue > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("hue", hue, "Hue input values range from 0 to 1.");
    }
    if (((saturation < 0) || (saturation > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("saturation", saturation, "Saturation input values range from 0 to 1.");
    }
    if (((lightness < 0) || (lightness > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("lightness", lightness, "Lightness input values range from 0 to 1.");
    }
    let red = lightness;
    let green = lightness;
    let blue = lightness;
    let v = ((lightness <= 0.5) ? MultiplyDouble(lightness, ((1 + saturation))) : ((lightness + saturation) - MultiplyDouble(lightness, saturation)));
    if ((v > 0))
    {
      let m = ((lightness + lightness) - v);
      let sv = (((v - m)) / v);
      hue *= 6;
      let sextant = Int32(hue);
      let fract = (hue - sextant);
      let vsf = MultiplyDouble(MultiplyDouble(v, sv), fract);
      let mid1 = (m + vsf);
      let mid2 = (v - vsf);
      { const $switch0 = sextant;
      switch (true) {
        case $switch0 === 0:
          red = v;
          green = mid1;
          blue = m;
          break;
        case $switch0 === 1:
          red = mid2;
          green = v;
          blue = m;
          break;
        case $switch0 === 2:
          red = m;
          green = v;
          blue = mid1;
          break;
        case $switch0 === 3:
          red = m;
          green = mid2;
          blue = v;
          break;
        case $switch0 === 4:
          red = mid1;
          green = m;
          blue = v;
          break;
        case $switch0 === 5:
          red = v;
          green = m;
          blue = mid2;
          break;
        case $switch0 === 6:
          red = v;
          green = mid1;
          blue = m;
          break;
      } }
    }
    return AciColor.$create4(red, green, blue);
  }
  static $ToHsl0(color, hsl) {
    let h, s, l;
    AciColor.$ToHsl1(color, { get value() { return h; }, set value(v) { h = v; } }, { get value() { return s; }, set value(v) { s = v; } }, { get value() { return l; }, set value(v) { l = v; } });
    hsl.value = Vector3.$create1(h, s, l);
  }
  static $ToHsl1(color, hue, saturation, lightness) {
    if ((color === null))
    {
      throw new Errors.ArgumentNullException("color");
    }
    let red = (color.R / 255);
    let green = (color.G / 255);
    let blue = (color.B / 255);
    hue.value = 0;
    saturation.value = 0;
    let v = DotNetMath.Max(red, green);
    v = DotNetMath.Max(v, blue);
    let m = DotNetMath.Min(red, green);
    m = DotNetMath.Min(m, blue);
    lightness.value = (((m + v)) / 2);
    if ((lightness.value <= 0))
    {
      return;
    }
    let vm = (v - m);
    saturation.value = vm;
    if ((saturation.value > 0))
    {
      saturation.value /= (((lightness.value <= 0.5)) ? (v + m) : ((2 - v) - m));
    }
    else
    {
      return;
    }
    let red2 = (((v - red)) / vm);
    let green2 = (((v - green)) / vm);
    let blue2 = (((v - blue)) / vm);
    if (MathHelper.$IsEqual0(red, v))
    {
      hue.value = (MathHelper.$IsEqual0(green, m) ? (5 + blue2) : (1 - green2));
    }
    else
    if (MathHelper.$IsEqual0(green, v))
    {
      hue.value = (MathHelper.$IsEqual0(blue, m) ? (1 + red2) : (3 - blue2));
    }
    else
    {
      hue.value = (MathHelper.$IsEqual0(red, m) ? (3 + green2) : (5 - red2));
    }
    hue.value /= 6;
  }
  static $ToHsl2(color) {
    let h, s, l;
    AciColor.$ToHsl1(color, { get value() { return h; }, set value(v) { h = v; } }, { get value() { return s; }, set value(v) { s = v; } }, { get value() { return l; }, set value(v) { l = v; } });
    return Vector3.$create1(h, s, l);
  }
  ToColor() {
    if (((this.$index < 1) || (this.$index > 255)))
    {
      return Color.White;
    }
    return Color.FromArgb(this.$r, this.$g, this.$b);
  }
  FromColor(color) {
    this.$r = color.R;
    this.$g = color.G;
    this.$b = color.B;
    this.$useTrueColor = true;
    this.$index = AciColor.RgbToAci(this.$r, this.$g, this.$b);
  }
  static FromCadIndex(index) {
    if (((index < 0) || (index > 256)))
    {
      throw new Errors.ArgumentOutOfRangeException("index", index, "Accepted CAD indexed AciColor values range from 0 to 256.");
    }
    if ((index === 0))
    {
      return AciColor.ByBlock;
    }
    if ((index === 256))
    {
      return AciColor.ByLayer;
    }
    return AciColor.$create6(index);
  }
  static FromTrueColor(value) {
    let bytes = Array.from(Uint8Array.of(Byte(value), Byte((value) >> 8), Byte((value) >> 16), Byte((value) >> 24)));
    return AciColor.$create2(GetElement(bytes, 2), GetElement(bytes, 1), GetElement(bytes, 0));
  }
  static ToTrueColor(color) {
    if ((color === null))
    {
      throw new Errors.ArgumentNullException("color");
    }
    return Int32FromBytes([color.B, color.G, color.R, 194], 0);
  }
  ToString() {
    if ((this.$index === 0))
    {
      return "ByBlock";
    }
    if ((this.$index === 256))
    {
      return "ByLayer";
    }
    if (this.$useTrueColor)
    {
      return Format("{0}{3}{1}{3}{2}", this.$r, this.$g, this.$b, Culture.ListSeparator);
    }
    return NumberText(this.$index, Culture.Current);
  }
  Clone() {
    let color = Init(AciColor.$create0(), $new => { $new.$r = this.$r; $new.$g = this.$g; $new.$b = this.$b; $new.$useTrueColor = this.$useTrueColor; $new.$index = this.$index; });
    return color;
  }
  Equals(other) {
    if ((other === null))
    {
      return false;
    }
    return ((((other.$r === this.$r)) && ((other.$g === this.$g))) && ((other.$b === this.$b)));
  }
  static FromHsl(...args) {
    if (args.length === 1 && (args[0] instanceof Vector3)) return AciColor.$FromHsl0(...args);
    if (args.length === 3 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number')) return AciColor.$FromHsl1(...args);
    throw new ArgumentException("No matching AciColor.FromHsl overload. Consult native-port-manifest.json.");
  }
  static ToHsl(...args) {
    if (args.length === 2 && (args[0] === null || args[0] instanceof AciColor) && (args[1] != null && typeof args[1] === 'object' && 'value' in args[1])) return AciColor.$ToHsl0(...args);
    if (args.length === 4 && (args[0] === null || args[0] instanceof AciColor) && (args[1] != null && typeof args[1] === 'object' && 'value' in args[1]) && (args[2] != null && typeof args[2] === 'object' && 'value' in args[2]) && (args[3] != null && typeof args[3] === 'object' && 'value' in args[3])) return AciColor.$ToHsl1(...args);
    if (args.length === 1 && (args[0] === null || args[0] instanceof AciColor)) return AciColor.$ToHsl2(...args);
    throw new ArgumentException("No matching AciColor.ToHsl overload. Consult native-port-manifest.json.");
  }
}
