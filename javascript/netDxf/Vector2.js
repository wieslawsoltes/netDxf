// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { MathHelper } from './MathHelper.js';

export class Vector2 {
  // C# backing state is prefixed with $; public members retain their original names.
  $x = 0;
  $y = 0;
  $isNormalized = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) return;
    if (args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array)) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number')) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 1 && (typeof args[0] === 'number')) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching Vector2 constructor. Use Vector2.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(value) {
    this.$x = value;
    this.$y = value;
    this.$isNormalized = false;
  }
  $ctor1(x, y) {
    this.$x = x;
    this.$y = y;
    this.$isNormalized = false;
  }
  $ctor2(array) {
    if ((array === null))
    {
      throw new Errors.ArgumentNullException("array");
    }
    if ((array.length !== 2))
    {
      throw new Errors.ArgumentOutOfRangeException("array", array.length, "The dimension of the array must be two.");
    }
    this.$x = GetElement(array, 0);
    this.$y = GetElement(array, 1);
    this.$isNormalized = false;
  }
  static CreateOverload(signature, ...args) {
    if (signature === '' && args.length === 0) return new Vector2();
    if (signature === "double") {
      if (!(args.length === 1 && (typeof args[0] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Vector2.$create0(...args);
    }
    if (signature === "double,double") {
      if (!(args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Vector2.$create1(...args);
    }
    if (signature === "double[]") {
      if (!(args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Vector2.$create2(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new Vector2(ConstructorTag, 0, args); }
  static $create1(...args) { return new Vector2(ConstructorTag, 1, args); }
  static $create2(...args) { return new Vector2(ConstructorTag, 2, args); }
  [CopyValue]() {
    const value = new Vector2();
    value.$x = this.$x;
    value.$y = this.$y;
    value.$isNormalized = this.$isNormalized;
    return value;
  }
  $assign(value) {
    this.$x = value.$x;
    this.$y = value.$y;
    this.$isNormalized = value.$isNormalized;
    return this;
  }
  static get Zero() {
    return Vector2.$create1(0, 0);
  }
  static get UnitX() {
    return Init(Vector2.$create1(1, 0), $new => { $new.$isNormalized = true; });
  }
  static get UnitY() {
    return Init(Vector2.$create1(0, 1), $new => { $new.$isNormalized = true; });
  }
  static get NaN() {
    return Vector2.$create1(DotNetNaN, DotNetNaN);
  }
  get X() {
    return this.$x;
  }
  set X(value) {
    this.$isNormalized = false;
    this.$x = value;
  }
  get Y() {
    return this.$y;
  }
  set Y(value) {
    this.$isNormalized = false;
    this.$y = value;
  }
  get_Item(index) {
    { const $switch0 = index;
    switch (true) {
      case $switch0 === 0:
        return this.$x;
      case $switch0 === 1:
        return this.$y;
      default:
        throw new Errors.ArgumentOutOfRangeException("index");
    } }
  }
  set_Item(index, value) {
    this.$isNormalized = false;
    { const $switch1 = index;
    switch (true) {
      case $switch1 === 0:
        this.$x = value;
        break;
      case $switch1 === 1:
        this.$y = value;
        break;
      default:
        throw new Errors.ArgumentOutOfRangeException("index");
    } }
  }
  get IsNormalized() {
    return this.$isNormalized;
  }
  static IsNaN(u) {
    return (Number.isNaN(u.X) || Number.isNaN(u.Y));
  }
  static IsZero(u) {
    return (MathHelper.$IsZero0(u.X) && MathHelper.$IsZero0(u.Y));
  }
  static DotProduct(u, v) {
    return (MultiplyDouble(u.X, v.X) + MultiplyDouble(u.Y, v.Y));
  }
  static CrossProduct(u, v) {
    return (MultiplyDouble(u.X, v.Y) - MultiplyDouble(u.Y, v.X));
  }
  static Perpendicular(u) {
    return Init(Vector2.$create1((-u.Y), u.X), $new => { $new.$isNormalized = u.IsNormalized; });
  }
  static Rotate(u, angle) {
    let sin = DotNetMath.Sin(angle);
    let cos = DotNetMath.Cos(angle);
    return Init(Vector2.$create1((MultiplyDouble(u.X, cos) - MultiplyDouble(u.Y, sin)), (MultiplyDouble(u.X, sin) + MultiplyDouble(u.Y, cos))), $new => { $new.$isNormalized = u.IsNormalized; });
  }
  static Polar(u, distance, angle) {
    let dir = Vector2.$create1(DotNetMath.Cos(angle), DotNetMath.Sin(angle));
    return Vector2.op_Addition(u, Vector2.$op_Multiply0(dir, distance));
  }
  static SquareDistance(u, v) {
    return (MultiplyDouble(((u.X - v.X)), ((u.X - v.X))) + MultiplyDouble(((u.Y - v.Y)), ((u.Y - v.Y))));
  }
  static Distance(u, v) {
    return DotNetMath.Sqrt(Vector2.SquareDistance(u, v));
  }
  static $Angle0(u) {
    let angle = DotNetMath.Atan2(u.Y, u.X);
    if ((angle < 0))
    {
      return (6.283185307179586 + angle);
    }
    return angle;
  }
  static $Angle1(u, v) {
    let dir = Vector2.op_Subtraction(v, u);
    return Vector2.$Angle0(dir);
  }
  static AngleBetween(u, v) {
    let cos = (Vector2.DotProduct(u, v) / (MultiplyDouble(u.Modulus(), v.Modulus())));
    if ((cos >= 1))
    {
      return 0;
    }
    if ((cos <= (-1)))
    {
      return 3.141592653589793;
    }
    return DotNetMath.Acos(cos);
  }
  static MidPoint(u, v) {
    return Vector2.$create1((((v.X + u.X)) * 0.5), (((v.Y + u.Y)) * 0.5));
  }
  static $ArePerpendicular0(u, v) {
    return Vector2.$ArePerpendicular1(u, v, MathHelper.Epsilon);
  }
  static $ArePerpendicular1(u, v, threshold) {
    return MathHelper.$IsZero1(Vector2.DotProduct(u, v), threshold);
  }
  static $AreParallel0(u, v) {
    return Vector2.$AreParallel1(u, v, MathHelper.Epsilon);
  }
  static $AreParallel1(u, v, threshold) {
    return MathHelper.$IsZero1(Vector2.CrossProduct(u, v), threshold);
  }
  static Round(u, numDigits) {
    return Vector2.$create1(DotNetMath.Round(u.X, numDigits), DotNetMath.Round(u.Y, numDigits));
  }
  static Normalize(u) {
    if (u.$isNormalized)
    {
      return Copy(u);
    }
    let mod = u.Modulus();
    if (MathHelper.$IsZero0(mod))
    {
      return Vector2.Zero;
    }
    let modInv = (1 / mod);
    return Init(Vector2.$create1(MultiplyDouble(u.$x, modInv), MultiplyDouble(u.$y, modInv)), $new => { $new.$isNormalized = true; });
  }
  static op_Equality(u, v) {
    return Vector2.$Equals0(u, v);
  }
  static op_Inequality(u, v) {
    return (!Vector2.$Equals0(u, v));
  }
  static op_Addition(u, v) {
    return Vector2.$create1((u.X + v.X), (u.Y + v.Y));
  }
  static Add(u, v) {
    return Vector2.$create1((u.X + v.X), (u.Y + v.Y));
  }
  static op_Subtraction(u, v) {
    return Vector2.$create1((u.X - v.X), (u.Y - v.Y));
  }
  static Subtract(u, v) {
    return Vector2.$create1((u.X - v.X), (u.Y - v.Y));
  }
  static op_UnaryNegation(u) {
    return Init(Vector2.$create1((-u.X), (-u.Y)), $new => { $new.$isNormalized = u.IsNormalized; });
  }
  static Negate(u) {
    return Init(Vector2.$create1((-u.X), (-u.Y)), $new => { $new.$isNormalized = u.IsNormalized; });
  }
  static $op_Multiply0(u, a) {
    return Vector2.$create1(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a));
  }
  static $Multiply0(u, a) {
    return Vector2.$create1(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a));
  }
  static $op_Multiply1(a, u) {
    return Vector2.$create1(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a));
  }
  static $Multiply1(a, u) {
    return Vector2.$create1(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a));
  }
  static $op_Multiply2(u, v) {
    return Vector2.$create1(MultiplyDouble(u.X, v.X), MultiplyDouble(u.Y, v.Y));
  }
  static $Multiply2(u, v) {
    return Vector2.$create1(MultiplyDouble(u.X, v.X), MultiplyDouble(u.Y, v.Y));
  }
  static $op_Division0(u, a) {
    let invScalar = (1 / a);
    return Vector2.$create1(MultiplyDouble(u.X, invScalar), MultiplyDouble(u.Y, invScalar));
  }
  static $Divide0(u, a) {
    let invScalar = (1 / a);
    return Vector2.$create1(MultiplyDouble(u.X, invScalar), MultiplyDouble(u.Y, invScalar));
  }
  static $op_Division1(u, v) {
    return Vector2.$create1((u.X / v.X), (u.Y / v.Y));
  }
  static $Divide1(u, v) {
    return Vector2.$create1((u.X / v.X), (u.Y / v.Y));
  }
  Normalize() {
    if (this.$isNormalized)
    {
      return;
    }
    let mod = this.Modulus();
    if (MathHelper.$IsZero0(mod))
    {
      this.$assign(Vector2.Zero);
      return;
    }
    let modInv = (1 / mod);
    this.$x *= modInv;
    this.$y *= modInv;
    this.$isNormalized = true;
  }
  Modulus() {
    return (this.$isNormalized ? 1 : DotNetMath.Sqrt(Vector2.DotProduct(this, this)));
  }
  ToArray() {
    return [this.$x, this.$y];
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
    return (MathHelper.$IsEqual1(other.X, this.$x, threshold) && MathHelper.$IsEqual1(other.Y, this.$y, threshold));
  }
  $Equals2(other) {
    let vector;
    if ((other instanceof Vector2 && ((vector = other), true)))
    {
      return this.$Equals0(vector);
    }
    return false;
  }
  GetHashCode() {
    return (DoubleHash(this.X) ^ DoubleHash(this.Y));
  }
  $ToString0() {
    return Format("{0}{2} {1}", this.$x, this.$y, Culture.ListSeparator);
  }
  $ToString1(provider) {
    return Format("{0}{2} {1}", NumberText(this.$x, provider), NumberText(this.$y, provider), Culture.ListSeparator);
  }
  static Angle(...args) {
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) return Vector2.$Angle1(...args);
    if (args.length === 1 && (args[0] instanceof Vector2)) return Vector2.$Angle0(...args);
    throw new ArgumentException("No matching Vector2.Angle overload. Consult native-port-manifest.json.");
  }
  static ArePerpendicular(...args) {
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number')) return Vector2.$ArePerpendicular1(...args);
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) return Vector2.$ArePerpendicular0(...args);
    throw new ArgumentException("No matching Vector2.ArePerpendicular overload. Consult native-port-manifest.json.");
  }
  static AreParallel(...args) {
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number')) return Vector2.$AreParallel1(...args);
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) return Vector2.$AreParallel0(...args);
    throw new ArgumentException("No matching Vector2.AreParallel overload. Consult native-port-manifest.json.");
  }
  static op_Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) return Vector2.$op_Multiply2(...args);
    if (args.length === 2 && (args[0] instanceof Vector2) && (typeof args[1] === 'number')) return Vector2.$op_Multiply0(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (args[1] instanceof Vector2)) return Vector2.$op_Multiply1(...args);
    throw new ArgumentException("No matching Vector2.op_Multiply overload. Consult native-port-manifest.json.");
  }
  static Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) return Vector2.$Multiply2(...args);
    if (args.length === 2 && (args[0] instanceof Vector2) && (typeof args[1] === 'number')) return Vector2.$Multiply0(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (args[1] instanceof Vector2)) return Vector2.$Multiply1(...args);
    throw new ArgumentException("No matching Vector2.Multiply overload. Consult native-port-manifest.json.");
  }
  static op_Division(...args) {
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) return Vector2.$op_Division1(...args);
    if (args.length === 2 && (args[0] instanceof Vector2) && (typeof args[1] === 'number')) return Vector2.$op_Division0(...args);
    throw new ArgumentException("No matching Vector2.op_Division overload. Consult native-port-manifest.json.");
  }
  static Divide(...args) {
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) return Vector2.$Divide1(...args);
    if (args.length === 2 && (args[0] instanceof Vector2) && (typeof args[1] === 'number')) return Vector2.$Divide0(...args);
    throw new ArgumentException("No matching Vector2.Divide overload. Consult native-port-manifest.json.");
  }
  static Equals(...args) {
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number')) return Vector2.$Equals1(...args);
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) return Vector2.$Equals0(...args);
    throw new ArgumentException("No matching Vector2.Equals overload. Consult native-port-manifest.json.");
  }
  Equals(...args) {
    if (args.length === 2 && (args[0] instanceof Vector2) && (typeof args[1] === 'number')) return this.$Equals1(...args);
    if (args.length === 1 && (args[0] instanceof Vector2)) return this.$Equals0(...args);
    if (args.length === 1 && (true)) return this.$Equals2(...args);
    throw new ArgumentException("No matching Vector2.Equals overload. Consult native-port-manifest.json.");
  }
  ToString(...args) {
    if (args.length === 1 && (args[0] === null || typeof args[0] === 'object' || typeof args[0] === 'string')) return this.$ToString1(...args);
    if (args.length === 0) return this.$ToString0(...args);
    throw new ArgumentException("No matching Vector2.ToString overload. Consult native-port-manifest.json.");
  }
}
