// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { MathHelper } from './MathHelper.js';

export class Vector3 {
  // C# backing state is prefixed with $; public members retain their original names.
  $x = 0;
  $y = 0;
  $z = 0;
  $isNormalized = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) return;
    if (args.length === 3 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number')) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array)) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 1 && (typeof args[0] === 'number')) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching Vector3 constructor. Use Vector3.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(value) {
    this.$x = value;
    this.$y = value;
    this.$z = value;
    this.$isNormalized = false;
  }
  $ctor1(x, y, z) {
    this.$x = x;
    this.$y = y;
    this.$z = z;
    this.$isNormalized = false;
  }
  $ctor2(array) {
    if ((array === null))
    {
      throw new Errors.ArgumentNullException("array");
    }
    if ((array.length !== 3))
    {
      throw new Errors.ArgumentOutOfRangeException("array", array.length, "The dimension of the array must be three.");
    }
    this.$x = GetElement(array, 0);
    this.$y = GetElement(array, 1);
    this.$z = GetElement(array, 2);
    this.$isNormalized = false;
  }
  static CreateOverload(signature, ...args) {
    if (signature === '' && args.length === 0) return new Vector3();
    if (signature === "double") {
      if (!(args.length === 1 && (typeof args[0] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Vector3.$create0(...args);
    }
    if (signature === "double,double,double") {
      if (!(args.length === 3 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Vector3.$create1(...args);
    }
    if (signature === "double[]") {
      if (!(args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Vector3.$create2(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new Vector3(ConstructorTag, 0, args); }
  static $create1(...args) { return new Vector3(ConstructorTag, 1, args); }
  static $create2(...args) { return new Vector3(ConstructorTag, 2, args); }
  [CopyValue]() {
    const value = new Vector3();
    value.$x = this.$x;
    value.$y = this.$y;
    value.$z = this.$z;
    value.$isNormalized = this.$isNormalized;
    return value;
  }
  $assign(value) {
    this.$x = value.$x;
    this.$y = value.$y;
    this.$z = value.$z;
    this.$isNormalized = value.$isNormalized;
    return this;
  }
  static get Zero() {
    return Vector3.$create1(0, 0, 0);
  }
  static get UnitX() {
    return Init(Vector3.$create1(1, 0, 0), $new => { $new.$isNormalized = true; });
  }
  static get UnitY() {
    return Init(Vector3.$create1(0, 1, 0), $new => { $new.$isNormalized = true; });
  }
  static get UnitZ() {
    return Init(Vector3.$create1(0, 0, 1), $new => { $new.$isNormalized = true; });
  }
  static get NaN() {
    return Vector3.$create1(DotNetNaN, DotNetNaN, DotNetNaN);
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
  get_Item(index) {
    { const $switch0 = index;
    switch (true) {
      case $switch0 === 0:
        return this.$x;
      case $switch0 === 1:
        return this.$y;
      case $switch0 === 2:
        return this.$z;
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
      default:
        throw new Errors.ArgumentOutOfRangeException("index");
    } }
    this.$isNormalized = false;
  }
  get IsNormalized() {
    return this.$isNormalized;
  }
  static IsNaN(u) {
    return ((Number.isNaN(u.X) || Number.isNaN(u.Y)) || Number.isNaN(u.Z));
  }
  static IsZero(u) {
    return ((MathHelper.$IsZero0(u.X) && MathHelper.$IsZero0(u.Y)) && MathHelper.$IsZero0(u.Z));
  }
  static DotProduct(u, v) {
    return ((MultiplyDouble(u.X, v.X) + MultiplyDouble(u.Y, v.Y)) + MultiplyDouble(u.Z, v.Z));
  }
  static CrossProduct(u, v) {
    let a = (MultiplyDouble(u.Y, v.Z) - MultiplyDouble(u.Z, v.Y));
    let b = (MultiplyDouble(u.Z, v.X) - MultiplyDouble(u.X, v.Z));
    let c = (MultiplyDouble(u.X, v.Y) - MultiplyDouble(u.Y, v.X));
    return Vector3.$create1(a, b, c);
  }
  static Distance(u, v) {
    return DotNetMath.Sqrt(Vector3.SquareDistance(u, v));
  }
  static SquareDistance(u, v) {
    return ((MultiplyDouble(((u.X - v.X)), ((u.X - v.X))) + MultiplyDouble(((u.Y - v.Y)), ((u.Y - v.Y)))) + MultiplyDouble(((u.Z - v.Z)), ((u.Z - v.Z))));
  }
  static AngleBetween(u, v) {
    let cos = (Vector3.DotProduct(u, v) / (MultiplyDouble(u.Modulus(), v.Modulus())));
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
  static RotateAroundAxis(v, axis, angle) {
    axis = Copy(axis);
    let q = new Vector3();
    axis.Normalize();
    let cos = DotNetMath.Cos(angle);
    let sin = DotNetMath.Sin(angle);
    q.X += MultiplyDouble(((cos + MultiplyDouble(MultiplyDouble(((1 - cos)), axis.X), axis.X))), v.X);
    q.X += MultiplyDouble(((MultiplyDouble(MultiplyDouble(((1 - cos)), axis.X), axis.Y) - MultiplyDouble(axis.Z, sin))), v.Y);
    q.X += MultiplyDouble(((MultiplyDouble(MultiplyDouble(((1 - cos)), axis.X), axis.Z) + MultiplyDouble(axis.Y, sin))), v.Z);
    q.Y += MultiplyDouble(((MultiplyDouble(MultiplyDouble(((1 - cos)), axis.X), axis.Y) + MultiplyDouble(axis.Z, sin))), v.X);
    q.Y += MultiplyDouble(((cos + MultiplyDouble(MultiplyDouble(((1 - cos)), axis.Y), axis.Y))), v.Y);
    q.Y += MultiplyDouble(((MultiplyDouble(MultiplyDouble(((1 - cos)), axis.Y), axis.Z) - MultiplyDouble(axis.X, sin))), v.Z);
    q.Z += MultiplyDouble(((MultiplyDouble(MultiplyDouble(((1 - cos)), axis.X), axis.Z) - MultiplyDouble(axis.Y, sin))), v.X);
    q.Z += MultiplyDouble(((MultiplyDouble(MultiplyDouble(((1 - cos)), axis.Y), axis.Z) + MultiplyDouble(axis.X, sin))), v.Y);
    q.Z += MultiplyDouble(((cos + MultiplyDouble(MultiplyDouble(((1 - cos)), axis.Z), axis.Z))), v.Z);
    return Copy(q);
  }
  static MidPoint(u, v) {
    return Vector3.$create1((((v.X + u.X)) * 0.5), (((v.Y + u.Y)) * 0.5), (((v.Z + u.Z)) * 0.5));
  }
  static $ArePerpendicular0(u, v) {
    return Vector3.$ArePerpendicular1(u, v, MathHelper.Epsilon);
  }
  static $ArePerpendicular1(u, v, threshold) {
    return MathHelper.$IsZero1(Vector3.DotProduct(u, v), threshold);
  }
  static $AreParallel0(u, v) {
    return Vector3.$AreParallel1(u, v, MathHelper.Epsilon);
  }
  static $AreParallel1(u, v, threshold) {
    let cross = Vector3.CrossProduct(u, v);
    if ((!MathHelper.$IsZero1(cross.X, threshold)))
    {
      return false;
    }
    if ((!MathHelper.$IsZero1(cross.Y, threshold)))
    {
      return false;
    }
    if ((!MathHelper.$IsZero1(cross.Z, threshold)))
    {
      return false;
    }
    return true;
  }
  static Round(u, numDigits) {
    return Vector3.$create1(DotNetMath.Round(u.X, numDigits), DotNetMath.Round(u.Y, numDigits), DotNetMath.Round(u.Z, numDigits));
  }
  static Normalize(u) {
    if (u.$isNormalized)
    {
      return Copy(u);
    }
    let mod = u.Modulus();
    if (MathHelper.$IsZero0(mod))
    {
      return Vector3.NaN;
    }
    let modInv = (1 / mod);
    return Init(Vector3.$create1(MultiplyDouble(u.$x, modInv), MultiplyDouble(u.$y, modInv), MultiplyDouble(u.$z, modInv)), $new => { $new.$isNormalized = true; });
  }
  static op_Equality(u, v) {
    return Vector3.$Equals0(u, v);
  }
  static op_Inequality(u, v) {
    return (!Vector3.$Equals0(u, v));
  }
  static op_Addition(u, v) {
    return Vector3.$create1((u.X + v.X), (u.Y + v.Y), (u.Z + v.Z));
  }
  static Add(u, v) {
    return Vector3.$create1((u.X + v.X), (u.Y + v.Y), (u.Z + v.Z));
  }
  static op_Subtraction(u, v) {
    return Vector3.$create1((u.X - v.X), (u.Y - v.Y), (u.Z - v.Z));
  }
  static Subtract(u, v) {
    return Vector3.$create1((u.X - v.X), (u.Y - v.Y), (u.Z - v.Z));
  }
  static op_UnaryNegation(u) {
    return Init(Vector3.$create1((-u.X), (-u.Y), (-u.Z)), $new => { $new.$isNormalized = u.IsNormalized; });
  }
  static Negate(u) {
    return Init(Vector3.$create1((-u.X), (-u.Y), (-u.Z)), $new => { $new.$isNormalized = u.IsNormalized; });
  }
  static $op_Multiply0(u, a) {
    return Vector3.$create1(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a), MultiplyDouble(u.Z, a));
  }
  static $Multiply0(u, a) {
    return Vector3.$create1(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a), MultiplyDouble(u.Z, a));
  }
  static $op_Multiply1(a, u) {
    return Vector3.$create1(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a), MultiplyDouble(u.Z, a));
  }
  static $Multiply1(a, u) {
    return Vector3.$create1(MultiplyDouble(u.X, a), MultiplyDouble(u.Y, a), MultiplyDouble(u.Z, a));
  }
  static $op_Multiply2(u, v) {
    return Vector3.$create1(MultiplyDouble(u.X, v.X), MultiplyDouble(u.Y, v.Y), MultiplyDouble(u.Z, v.Z));
  }
  static $Multiply2(u, v) {
    return Vector3.$create1(MultiplyDouble(u.X, v.X), MultiplyDouble(u.Y, v.Y), MultiplyDouble(u.Z, v.Z));
  }
  static $op_Division0(u, a) {
    let invScalar = (1 / a);
    return Vector3.$create1(MultiplyDouble(u.X, invScalar), MultiplyDouble(u.Y, invScalar), MultiplyDouble(u.Z, invScalar));
  }
  static $Divide0(u, a) {
    let invScalar = (1 / a);
    return Vector3.$create1(MultiplyDouble(u.X, invScalar), MultiplyDouble(u.Y, invScalar), MultiplyDouble(u.Z, invScalar));
  }
  static $op_Division1(u, v) {
    return Vector3.$create1((u.X / v.X), (u.Y / v.Y), (u.Z / v.Z));
  }
  static $Divide1(u, v) {
    return Vector3.$create1((u.X / v.X), (u.Y / v.Y), (u.Z / v.Z));
  }
  Normalize() {
    if (this.$isNormalized)
    {
      return;
    }
    let mod = this.Modulus();
    if (MathHelper.$IsZero0(mod))
    {
      this.$assign(Vector3.Zero);
      return;
    }
    let modInv = (1 / mod);
    this.$x *= modInv;
    this.$y *= modInv;
    this.$z *= modInv;
    this.$isNormalized = true;
  }
  Modulus() {
    return (this.$isNormalized ? 1 : DotNetMath.Sqrt(Vector3.DotProduct(this, this)));
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
    return ((MathHelper.$IsEqual1(other.X, this.$x, threshold) && MathHelper.$IsEqual1(other.Y, this.$y, threshold)) && MathHelper.$IsEqual1(other.Z, this.$z, threshold));
  }
  $Equals2(other) {
    let vector;
    if ((other instanceof Vector3 && ((vector = other), true)))
    {
      return this.$Equals0(vector);
    }
    return false;
  }
  GetHashCode() {
    return ((DoubleHash(this.X) ^ DoubleHash(this.Y)) ^ DoubleHash(this.Z));
  }
  $ToString0() {
    return Format("{0}{3} {1}{3} {2}", this.$x, this.$y, this.$z, Culture.ListSeparator);
  }
  $ToString1(provider) {
    return Format("{0}{3} {1}{3} {2}", NumberText(this.$x, provider), NumberText(this.$y, provider), NumberText(this.$z, provider), Culture.ListSeparator);
  }
  static ArePerpendicular(...args) {
    if (args.length === 3 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (typeof args[2] === 'number')) return Vector3.$ArePerpendicular1(...args);
    if (args.length === 2 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3)) return Vector3.$ArePerpendicular0(...args);
    throw new ArgumentException("No matching Vector3.ArePerpendicular overload. Consult native-port-manifest.json.");
  }
  static AreParallel(...args) {
    if (args.length === 3 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (typeof args[2] === 'number')) return Vector3.$AreParallel1(...args);
    if (args.length === 2 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3)) return Vector3.$AreParallel0(...args);
    throw new ArgumentException("No matching Vector3.AreParallel overload. Consult native-port-manifest.json.");
  }
  static op_Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3)) return Vector3.$op_Multiply2(...args);
    if (args.length === 2 && (args[0] instanceof Vector3) && (typeof args[1] === 'number')) return Vector3.$op_Multiply0(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (args[1] instanceof Vector3)) return Vector3.$op_Multiply1(...args);
    throw new ArgumentException("No matching Vector3.op_Multiply overload. Consult native-port-manifest.json.");
  }
  static Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3)) return Vector3.$Multiply2(...args);
    if (args.length === 2 && (args[0] instanceof Vector3) && (typeof args[1] === 'number')) return Vector3.$Multiply0(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (args[1] instanceof Vector3)) return Vector3.$Multiply1(...args);
    throw new ArgumentException("No matching Vector3.Multiply overload. Consult native-port-manifest.json.");
  }
  static op_Division(...args) {
    if (args.length === 2 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3)) return Vector3.$op_Division1(...args);
    if (args.length === 2 && (args[0] instanceof Vector3) && (typeof args[1] === 'number')) return Vector3.$op_Division0(...args);
    throw new ArgumentException("No matching Vector3.op_Division overload. Consult native-port-manifest.json.");
  }
  static Divide(...args) {
    if (args.length === 2 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3)) return Vector3.$Divide1(...args);
    if (args.length === 2 && (args[0] instanceof Vector3) && (typeof args[1] === 'number')) return Vector3.$Divide0(...args);
    throw new ArgumentException("No matching Vector3.Divide overload. Consult native-port-manifest.json.");
  }
  static Equals(...args) {
    if (args.length === 3 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (typeof args[2] === 'number')) return Vector3.$Equals1(...args);
    if (args.length === 2 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3)) return Vector3.$Equals0(...args);
    throw new ArgumentException("No matching Vector3.Equals overload. Consult native-port-manifest.json.");
  }
  Equals(...args) {
    if (args.length === 2 && (args[0] instanceof Vector3) && (typeof args[1] === 'number')) return this.$Equals1(...args);
    if (args.length === 1 && (args[0] instanceof Vector3)) return this.$Equals0(...args);
    if (args.length === 1 && (true)) return this.$Equals2(...args);
    throw new ArgumentException("No matching Vector3.Equals overload. Consult native-port-manifest.json.");
  }
  ToString(...args) {
    if (args.length === 1 && (args[0] === null || typeof args[0] === 'object' || typeof args[0] === 'string')) return this.$ToString1(...args);
    if (args.length === 0) return this.$ToString0(...args);
    throw new ArgumentException("No matching Vector3.ToString overload. Consult native-port-manifest.json.");
  }
}
