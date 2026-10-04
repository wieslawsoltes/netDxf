// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { MathHelper } from './MathHelper.js';

export class Vector4 {
  // C# backing state is prefixed with $; public members retain their original names.
  $x = 0;
  $y = 0;
  $z = 0;
  $w = 0;
  $isNormalized = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) return;
    if (args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number')) {
      this.$ctor0(...args);
      return;
    }
    if (args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array)) {
      this.$ctor1(...args);
      return;
    }
    throw new ArgumentException("No matching Vector4 constructor. Use Vector4.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(x, y, z, w) {
    this.$x = x;
    this.$y = y;
    this.$z = z;
    this.$w = w;
    this.$isNormalized = false;
  }
  $ctor1(array) {
    if ((array === null))
    {
      throw new Errors.ArgumentNullException("array");
    }
    if ((array.length !== 4))
    {
      throw new Errors.ArgumentOutOfRangeException("array", array.length, "The dimension of the array must be four.");
    }
    this.$x = GetElement(array, 0);
    this.$y = GetElement(array, 1);
    this.$z = GetElement(array, 2);
    this.$w = GetElement(array, 3);
    this.$isNormalized = false;
  }
  static CreateOverload(signature, ...args) {
    if (signature === '' && args.length === 0) return new Vector4();
    if (signature === "double,double,double,double") {
      if (!(args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Vector4.$create0(...args);
    }
    if (signature === "double[]") {
      if (!(args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Vector4.$create1(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new Vector4(ConstructorTag, 0, args); }
  static $create1(...args) { return new Vector4(ConstructorTag, 1, args); }
  [CopyValue]() {
    const value = new Vector4();
    value.$x = this.$x;
    value.$y = this.$y;
    value.$z = this.$z;
    value.$w = this.$w;
    value.$isNormalized = this.$isNormalized;
    return value;
  }
  $assign(value) {
    this.$x = value.$x;
    this.$y = value.$y;
    this.$z = value.$z;
    this.$w = value.$w;
    this.$isNormalized = value.$isNormalized;
    return this;
  }
  static get Zero() {
    return Vector4.$create0(0, 0, 0, 0);
  }
  static get UnitX() {
    return Init(Vector4.$create0(1, 0, 0, 0), $new => { $new.$isNormalized = true; });
  }
  static get UnitY() {
    return Init(Vector4.$create0(0, 1, 0, 0), $new => { $new.$isNormalized = true; });
  }
  static get UnitZ() {
    return Init(Vector4.$create0(0, 0, 1, 0), $new => { $new.$isNormalized = true; });
  }
  static get UnitW() {
    return Init(Vector4.$create0(0, 0, 0, 1), $new => { $new.$isNormalized = true; });
  }
  static get NaN() {
    return Vector4.$create0(DotNetNaN, DotNetNaN, DotNetNaN, DotNetNaN);
  }
  get X() {
    return this.$x;
  }
  set X(value) {
    this.$x = value;
    this.$isNormalized = false;
  }
  get Y() {
    return this.$y;
  }
  set Y(value) {
    this.$y = value;
    this.$isNormalized = false;
  }
  get Z() {
    return this.$z;
  }
  set Z(value) {
    this.$z = value;
    this.$isNormalized = false;
  }
  get W() {
    return this.$w;
  }
  set W(value) {
    this.$w = value;
    this.$isNormalized = false;
  }
  get_Item(index) {
    { const $switch0 = index;
    switch (true) {
      case $switch0 === 0:
        return this.$x;
      case $switch0 === 1:
        return this.$y;
      case $switch0 === 2:
        return this.$z;
      case $switch0 === 3:
        return this.$w;
      default:
        throw new Errors.ArgumentOutOfRangeException("index");
    } }
  }
  set_Item(index, value) {
    { const $switch1 = index;
    switch (true) {
      case $switch1 === 0:
        this.$x = value;
        break;
      case $switch1 === 1:
        this.$y = value;
        break;
      case $switch1 === 2:
        this.$z = value;
        break;
      case $switch1 === 3:
        this.$w = value;
        break;
      default:
        throw new Errors.ArgumentOutOfRangeException("index");
    } }
    this.$isNormalized = false;
  }
  get IsNormalized() {
    return this.$isNormalized;
  }
  static IsNaN(u) {
    return (((Number.isNaN(u.X) || Number.isNaN(u.Y)) || Number.isNaN(u.Z)) || Number.isNaN(u.W));
  }
  static IsZero(u) {
    return (((MathHelper.$IsZero0(u.X) && MathHelper.$IsZero0(u.Y)) && MathHelper.$IsZero0(u.Z)) && MathHelper.$IsZero0(u.W));
  }
  static DotProduct(u, v) {
    return (((MultiplyDouble(u.X, v.X) + MultiplyDouble(u.Y, v.Y)) + MultiplyDouble(u.Z, v.Z)) + MultiplyDouble(u.W, v.W));
  }
  static Distance(u, v) {
    return DotNetMath.Sqrt(Vector4.SquareDistance(u, v));
  }
  static SquareDistance(u, v) {
    return (((MultiplyDouble(((u.X - v.X)), ((u.X - v.X))) + MultiplyDouble(((u.Y - v.Y)), ((u.Y - v.Y)))) + MultiplyDouble(((u.Z - v.Z)), ((u.Z - v.Z)))) + MultiplyDouble(((u.W - v.Z)), ((u.W - v.W))));
  }
  static Round(u, numDigits) {
    return Vector4.$create0(DotNetMath.Round(u.X, numDigits), DotNetMath.Round(u.Y, numDigits), DotNetMath.Round(u.Z, numDigits), DotNetMath.Round(u.W, numDigits));
  }
  static Normalize(u) {
    if (u.$isNormalized)
    {
      return Copy(u);
    }
    let mod = u.Modulus();
    if (MathHelper.$IsZero0(mod))
    {
      return Vector4.Zero;
    }
    let modInv = (1 / mod);
    return Init(Vector4.$create0(MultiplyDouble(u.X, modInv), MultiplyDouble(u.Y, modInv), MultiplyDouble(u.Z, modInv), MultiplyDouble(u.W, modInv)), $new => { $new.$isNormalized = true; });
  }
  static op_Equality(u, v) {
    return Vector4.$Equals0(u, v);
  }
  static op_Inequality(u, v) {
    return (!Vector4.$Equals0(u, v));
  }
  static op_Addition(u, v) {
    return Vector4.$create0((u.X + v.X), (u.Y + v.Y), (u.Z + v.Z), (u.W + v.W));
  }
  static Add(u, v) {
    return Vector4.$create0((u.X + v.X), (u.Y + v.Y), (u.Z + v.Z), (u.W + v.W));
  }
  static op_Subtraction(u, v) {
    return Vector4.$create0((u.X - v.X), (u.Y - v.Y), (u.Z - v.Z), (u.W - v.W));
  }
  static Subtract(u, v) {
    return Vector4.$create0((u.X - v.X), (u.Y - v.Y), (u.Z - v.Z), (u.W - v.W));
  }
  static op_UnaryNegation(u) {
    return Init(Vector4.$create0((-u.X), (-u.Y), (-u.Z), (-u.W)), $new => { $new.$isNormalized = u.IsNormalized; });
  }
  static Negate(u) {
    return Init(Vector4.$create0((-u.X), (-u.Y), (-u.Z), (-u.W)), $new => { $new.$isNormalized = u.IsNormalized; });
  }
  static $op_Multiply0(u, a) {
    return Vector4.$create0(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a), MultiplyDouble(u.Z, a), MultiplyDouble(u.W, a));
  }
  static $Multiply0(u, a) {
    return Vector4.$create0(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a), MultiplyDouble(u.Z, a), MultiplyDouble(u.W, a));
  }
  static $op_Multiply1(a, u) {
    return Vector4.$create0(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a), MultiplyDouble(u.Z, a), MultiplyDouble(u.W, a));
  }
  static $Multiply1(a, u) {
    return Vector4.$create0(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a), MultiplyDouble(u.Z, a), MultiplyDouble(u.W, a));
  }
  static $op_Multiply2(u, v) {
    return Vector4.$create0(MultiplyDouble(u.X, v.X), MultiplyDouble(u.Y, v.Y), MultiplyDouble(u.Z, v.Z), MultiplyDouble(u.W, v.W));
  }
  static $Multiply2(u, v) {
    return Vector4.$create0(MultiplyDouble(u.X, v.X), MultiplyDouble(u.Y, v.Y), MultiplyDouble(u.Z, v.Z), MultiplyDouble(u.W, v.W));
  }
  static $op_Division0(u, a) {
    let invScalar = (1 / a);
    return Vector4.$create0(MultiplyDouble(u.X, invScalar), MultiplyDouble(u.Y, invScalar), MultiplyDouble(u.Z, invScalar), MultiplyDouble(u.W, invScalar));
  }
  static $Divide0(u, a) {
    let invScalar = (1 / a);
    return Vector4.$create0(MultiplyDouble(u.X, invScalar), MultiplyDouble(u.Y, invScalar), MultiplyDouble(u.Z, invScalar), MultiplyDouble(u.W, invScalar));
  }
  static $op_Division1(u, v) {
    return Vector4.$create0((u.X / v.X), (u.Y / v.Y), (u.Z / v.Z), (u.W / v.W));
  }
  static $Divide1(u, v) {
    return Vector4.$create0((u.X / v.X), (u.Y / v.Y), (u.Z / v.Z), (u.W / v.W));
  }
  Normalize() {
    if (this.$isNormalized)
    {
      return;
    }
    let mod = this.Modulus();
    if (MathHelper.$IsZero0(mod))
    {
      this.$assign(Vector4.Zero);
      return;
    }
    let modInv = (1 / mod);
    this.$x *= modInv;
    this.$y *= modInv;
    this.$z *= modInv;
    this.$w *= modInv;
    this.$isNormalized = true;
  }
  Modulus() {
    return (this.$isNormalized ? 1 : DotNetMath.Sqrt(Vector4.DotProduct(this, this)));
  }
  ToArray() {
    return [this.$x, this.$y, this.$z];
  }
  static $Equals0(a, b) {
    return a.$Equals1(b, MathHelper.Epsilon);
  }
  static $Equals1(a, b, threshold) {
    return a.$Equals1(b, threshold);
  }
  $Equals0(other) {
    return this.$Equals1(other, MathHelper.Epsilon);
  }
  $Equals1(other, threshold) {
    return (((MathHelper.$IsEqual1(other.X, this.X, threshold) && MathHelper.$IsEqual1(other.Y, this.Y, threshold)) && MathHelper.$IsEqual1(other.Z, this.Z, threshold)) && MathHelper.$IsEqual1(other.W, this.W, threshold));
  }
  $Equals2(other) {
    let vector;
    if ((other instanceof Vector4 && ((vector = other), true)))
    {
      return this.$Equals0(vector);
    }
    return false;
  }
  GetHashCode() {
    return (((DoubleHash(this.X) ^ DoubleHash(this.Y)) ^ DoubleHash(this.Z)) ^ DoubleHash(this.W));
  }
  $ToString0() {
    return Format("{0}{4} {1}{4} {2}{4} {3}", this.$x, this.$y, this.$z, this.$w, Culture.ListSeparator);
  }
  $ToString1(provider) {
    return Format("{0}{4} {1}{4} {2}{4} {3}", NumberText(this.$x, provider), NumberText(this.$y, provider), NumberText(this.$z, provider), NumberText(this.$w, provider), Culture.ListSeparator);
  }
  static op_Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Vector4) && (args[1] instanceof Vector4)) return Vector4.$op_Multiply2(...args);
    if (args.length === 2 && (args[0] instanceof Vector4) && (typeof args[1] === 'number')) return Vector4.$op_Multiply0(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (args[1] instanceof Vector4)) return Vector4.$op_Multiply1(...args);
    throw new ArgumentException("No matching Vector4.op_Multiply overload. Consult native-port-manifest.json.");
  }
  static Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Vector4) && (args[1] instanceof Vector4)) return Vector4.$Multiply2(...args);
    if (args.length === 2 && (args[0] instanceof Vector4) && (typeof args[1] === 'number')) return Vector4.$Multiply0(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (args[1] instanceof Vector4)) return Vector4.$Multiply1(...args);
    throw new ArgumentException("No matching Vector4.Multiply overload. Consult native-port-manifest.json.");
  }
  static op_Division(...args) {
    if (args.length === 2 && (args[0] instanceof Vector4) && (args[1] instanceof Vector4)) return Vector4.$op_Division1(...args);
    if (args.length === 2 && (args[0] instanceof Vector4) && (typeof args[1] === 'number')) return Vector4.$op_Division0(...args);
    throw new ArgumentException("No matching Vector4.op_Division overload. Consult native-port-manifest.json.");
  }
  static Divide(...args) {
    if (args.length === 2 && (args[0] instanceof Vector4) && (args[1] instanceof Vector4)) return Vector4.$Divide1(...args);
    if (args.length === 2 && (args[0] instanceof Vector4) && (typeof args[1] === 'number')) return Vector4.$Divide0(...args);
    throw new ArgumentException("No matching Vector4.Divide overload. Consult native-port-manifest.json.");
  }
  static Equals(...args) {
    if (args.length === 3 && (args[0] instanceof Vector4) && (args[1] instanceof Vector4) && (typeof args[2] === 'number')) return Vector4.$Equals1(...args);
    if (args.length === 2 && (args[0] instanceof Vector4) && (args[1] instanceof Vector4)) return Vector4.$Equals0(...args);
    throw new ArgumentException("No matching Vector4.Equals overload. Consult native-port-manifest.json.");
  }
  Equals(...args) {
    if (args.length === 2 && (args[0] instanceof Vector4) && (typeof args[1] === 'number')) return this.$Equals1(...args);
    if (args.length === 1 && (args[0] instanceof Vector4)) return this.$Equals0(...args);
    if (args.length === 1 && (true)) return this.$Equals2(...args);
    throw new ArgumentException("No matching Vector4.Equals overload. Consult native-port-manifest.json.");
  }
  ToString(...args) {
    if (args.length === 1 && (args[0] === null || typeof args[0] === 'object' || typeof args[0] === 'string')) return this.$ToString1(...args);
    if (args.length === 0) return this.$ToString0(...args);
    throw new ArgumentException("No matching Vector4.ToString overload. Consult native-port-manifest.json.");
  }
}
