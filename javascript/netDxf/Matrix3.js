// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { MathHelper } from './MathHelper.js';
import { Vector3 } from './Vector3.js';

export class Matrix3 {
  // C# backing state is prefixed with $; public members retain their original names.
  $m11 = 0;
  $m12 = 0;
  $m13 = 0;
  $m21 = 0;
  $m22 = 0;
  $m23 = 0;
  $m31 = 0;
  $m32 = 0;
  $m33 = 0;
  $dirty = false;
  $isIdentity = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) return;
    if (args.length === 9 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number') && (typeof args[5] === 'number') && (typeof args[6] === 'number') && (typeof args[7] === 'number') && (typeof args[8] === 'number')) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching Matrix3 constructor. Use Matrix3.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(m11, m12, m13, m21, m22, m23, m31, m32, m33) {
    this.$m11 = m11;
    this.$m12 = m12;
    this.$m13 = m13;
    this.$m21 = m21;
    this.$m22 = m22;
    this.$m23 = m23;
    this.$m31 = m31;
    this.$m32 = m32;
    this.$m33 = m33;
    this.$dirty = true;
    this.$isIdentity = false;
  }
  static CreateOverload(signature, ...args) {
    if (signature === '' && args.length === 0) return new Matrix3();
    if (signature === "double,double,double,double,double,double,double,double,double") {
      if (!(args.length === 9 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number') && (typeof args[5] === 'number') && (typeof args[6] === 'number') && (typeof args[7] === 'number') && (typeof args[8] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Matrix3.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new Matrix3(ConstructorTag, 0, args); }
  [CopyValue]() {
    const value = new Matrix3();
    value.$m11 = this.$m11;
    value.$m12 = this.$m12;
    value.$m13 = this.$m13;
    value.$m21 = this.$m21;
    value.$m22 = this.$m22;
    value.$m23 = this.$m23;
    value.$m31 = this.$m31;
    value.$m32 = this.$m32;
    value.$m33 = this.$m33;
    value.$dirty = this.$dirty;
    value.$isIdentity = this.$isIdentity;
    return value;
  }
  $assign(value) {
    this.$m11 = value.$m11;
    this.$m12 = value.$m12;
    this.$m13 = value.$m13;
    this.$m21 = value.$m21;
    this.$m22 = value.$m22;
    this.$m23 = value.$m23;
    this.$m31 = value.$m31;
    this.$m32 = value.$m32;
    this.$m33 = value.$m33;
    this.$dirty = value.$dirty;
    this.$isIdentity = value.$isIdentity;
    return this;
  }
  static get Zero() {
    return Init(Matrix3.$create0(0, 0, 0, 0, 0, 0, 0, 0, 0), $new => { $new.$dirty = false; $new.$isIdentity = false; });
  }
  static get Identity() {
    return Init(Matrix3.$create0(1, 0, 0, 0, 1, 0, 0, 0, 1), $new => { $new.$dirty = false; $new.$isIdentity = true; });
  }
  get M11() {
    return this.$m11;
  }
  set M11(value) {
    this.$m11 = value;
    this.$dirty = true;
  }
  get M12() {
    return this.$m12;
  }
  set M12(value) {
    this.$m12 = value;
    this.$dirty = true;
  }
  get M13() {
    return this.$m13;
  }
  set M13(value) {
    this.$m13 = value;
    this.$dirty = true;
  }
  get M21() {
    return this.$m21;
  }
  set M21(value) {
    this.$m21 = value;
    this.$dirty = true;
  }
  get M22() {
    return this.$m22;
  }
  set M22(value) {
    this.$m22 = value;
    this.$dirty = true;
  }
  get M23() {
    return this.$m23;
  }
  set M23(value) {
    this.$m23 = value;
    this.$dirty = true;
  }
  get M31() {
    return this.$m31;
  }
  set M31(value) {
    this.$m31 = value;
    this.$dirty = true;
  }
  get M32() {
    return this.$m32;
  }
  set M32(value) {
    this.$m32 = value;
    this.$dirty = true;
  }
  get M33() {
    return this.$m33;
  }
  set M33(value) {
    this.$m33 = value;
    this.$dirty = true;
  }
  get_Item(row, column) {
    { const $switch0 = row;
    switch (true) {
      case $switch0 === 0:
        { const $switch1 = column;
        switch (true) {
          case $switch1 === 0:
            return this.$m11;
          case $switch1 === 1:
            return this.$m12;
          case $switch1 === 2:
            return this.$m13;
          default:
            throw new Errors.ArgumentOutOfRangeException("column");
        } }
      case $switch0 === 1:
        { const $switch2 = column;
        switch (true) {
          case $switch2 === 0:
            return this.$m21;
          case $switch2 === 1:
            return this.$m22;
          case $switch2 === 2:
            return this.$m23;
          default:
            throw new Errors.ArgumentOutOfRangeException("column");
        } }
      case $switch0 === 2:
        { const $switch3 = column;
        switch (true) {
          case $switch3 === 0:
            return this.$m31;
          case $switch3 === 1:
            return this.$m32;
          case $switch3 === 2:
            return this.$m33;
          default:
            throw new Errors.ArgumentOutOfRangeException("column");
        } }
      default:
        throw new Errors.ArgumentOutOfRangeException("row");
    } }
  }
  set_Item(row, column, value) {
    { const $switch4 = row;
    switch (true) {
      case $switch4 === 0:
        { const $switch5 = column;
        switch (true) {
          case $switch5 === 0:
            this.$m11 = value;
            break;
          case $switch5 === 1:
            this.$m12 = value;
            break;
          case $switch5 === 2:
            this.$m13 = value;
            break;
          default:
            throw new Errors.ArgumentOutOfRangeException("column");
        } }
        break;
      case $switch4 === 1:
        { const $switch6 = column;
        switch (true) {
          case $switch6 === 0:
            this.$m21 = value;
            break;
          case $switch6 === 1:
            this.$m22 = value;
            break;
          case $switch6 === 2:
            this.$m23 = value;
            break;
          default:
            throw new Errors.ArgumentOutOfRangeException("column");
        } }
        break;
      case $switch4 === 2:
        { const $switch7 = column;
        switch (true) {
          case $switch7 === 0:
            this.$m31 = value;
            break;
          case $switch7 === 1:
            this.$m32 = value;
            break;
          case $switch7 === 2:
            this.$m33 = value;
            break;
          default:
            throw new Errors.ArgumentOutOfRangeException("column");
        } }
        break;
      default:
        throw new Errors.ArgumentOutOfRangeException("row");
    } }
    this.$dirty = true;
  }
  get IsIdentity() {
    if (this.$dirty)
    {
      this.$dirty = false;
      if ((!MathHelper.$IsOne0(this.M11)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      if ((!MathHelper.$IsZero0(this.M12)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      if ((!MathHelper.$IsZero0(this.M13)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      if ((!MathHelper.$IsZero0(this.M21)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      if ((!MathHelper.$IsOne0(this.M22)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      if ((!MathHelper.$IsZero0(this.M23)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      if ((!MathHelper.$IsZero0(this.M31)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      if ((!MathHelper.$IsZero0(this.M32)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      if ((!MathHelper.$IsOne0(this.M33)))
      {
        this.$isIdentity = false;
        return this.$isIdentity;
      }
      this.$isIdentity = true;
      return this.$isIdentity;
    }
    return this.$isIdentity;
  }
  static op_Addition(a, b) {
    return Matrix3.$create0((a.M11 + b.M11), (a.M12 + b.M12), (a.M13 + b.M13), (a.M21 + b.M21), (a.M22 + b.M22), (a.M23 + b.M23), (a.M31 + b.M31), (a.M32 + b.M32), (a.M33 + b.M33));
  }
  static Add(a, b) {
    return Matrix3.$create0((a.M11 + b.M11), (a.M12 + b.M12), (a.M13 + b.M13), (a.M21 + b.M21), (a.M22 + b.M22), (a.M23 + b.M23), (a.M31 + b.M31), (a.M32 + b.M32), (a.M33 + b.M33));
  }
  static op_Subtraction(a, b) {
    return Matrix3.$create0((a.M11 - b.M11), (a.M12 - b.M12), (a.M13 - b.M13), (a.M21 - b.M21), (a.M22 - b.M22), (a.M23 - b.M23), (a.M31 - b.M31), (a.M32 - b.M32), (a.M33 - b.M33));
  }
  static Subtract(a, b) {
    return Matrix3.$create0((a.M11 - b.M11), (a.M12 - b.M12), (a.M13 - b.M13), (a.M21 - b.M21), (a.M22 - b.M22), (a.M23 - b.M23), (a.M31 - b.M31), (a.M32 - b.M32), (a.M33 - b.M33));
  }
  static $op_Multiply0(a, b) {
    a = Copy(a);
    b = Copy(b);
    if (a.IsIdentity)
    {
      return Copy(b);
    }
    if (b.IsIdentity)
    {
      return Copy(a);
    }
    return Matrix3.$create0(((MultiplyDouble(a.M11, b.M11) + MultiplyDouble(a.M12, b.M21)) + MultiplyDouble(a.M13, b.M31)), ((MultiplyDouble(a.M11, b.M12) + MultiplyDouble(a.M12, b.M22)) + MultiplyDouble(a.M13, b.M32)), ((MultiplyDouble(a.M11, b.M13) + MultiplyDouble(a.M12, b.M23)) + MultiplyDouble(a.M13, b.M33)), ((MultiplyDouble(a.M21, b.M11) + MultiplyDouble(a.M22, b.M21)) + MultiplyDouble(a.M23, b.M31)), ((MultiplyDouble(a.M21, b.M12) + MultiplyDouble(a.M22, b.M22)) + MultiplyDouble(a.M23, b.M32)), ((MultiplyDouble(a.M21, b.M13) + MultiplyDouble(a.M22, b.M23)) + MultiplyDouble(a.M23, b.M33)), ((MultiplyDouble(a.M31, b.M11) + MultiplyDouble(a.M32, b.M21)) + MultiplyDouble(a.M33, b.M31)), ((MultiplyDouble(a.M31, b.M12) + MultiplyDouble(a.M32, b.M22)) + MultiplyDouble(a.M33, b.M32)), ((MultiplyDouble(a.M31, b.M13) + MultiplyDouble(a.M32, b.M23)) + MultiplyDouble(a.M33, b.M33)));
  }
  static $Multiply0(a, b) {
    a = Copy(a);
    b = Copy(b);
    if (a.IsIdentity)
    {
      return Copy(b);
    }
    if (b.IsIdentity)
    {
      return Copy(a);
    }
    return Matrix3.$create0(((MultiplyDouble(a.M11, b.M11) + MultiplyDouble(a.M12, b.M21)) + MultiplyDouble(a.M13, b.M31)), ((MultiplyDouble(a.M11, b.M12) + MultiplyDouble(a.M12, b.M22)) + MultiplyDouble(a.M13, b.M32)), ((MultiplyDouble(a.M11, b.M13) + MultiplyDouble(a.M12, b.M23)) + MultiplyDouble(a.M13, b.M33)), ((MultiplyDouble(a.M21, b.M11) + MultiplyDouble(a.M22, b.M21)) + MultiplyDouble(a.M23, b.M31)), ((MultiplyDouble(a.M21, b.M12) + MultiplyDouble(a.M22, b.M22)) + MultiplyDouble(a.M23, b.M32)), ((MultiplyDouble(a.M21, b.M13) + MultiplyDouble(a.M22, b.M23)) + MultiplyDouble(a.M23, b.M33)), ((MultiplyDouble(a.M31, b.M11) + MultiplyDouble(a.M32, b.M21)) + MultiplyDouble(a.M33, b.M31)), ((MultiplyDouble(a.M31, b.M12) + MultiplyDouble(a.M32, b.M22)) + MultiplyDouble(a.M33, b.M32)), ((MultiplyDouble(a.M31, b.M13) + MultiplyDouble(a.M32, b.M23)) + MultiplyDouble(a.M33, b.M33)));
  }
  static $op_Multiply1(a, u) {
    a = Copy(a);
    return Copy((a.IsIdentity ? u : Vector3.$create1(((MultiplyDouble(a.M11, u.X) + MultiplyDouble(a.M12, u.Y)) + MultiplyDouble(a.M13, u.Z)), ((MultiplyDouble(a.M21, u.X) + MultiplyDouble(a.M22, u.Y)) + MultiplyDouble(a.M23, u.Z)), ((MultiplyDouble(a.M31, u.X) + MultiplyDouble(a.M32, u.Y)) + MultiplyDouble(a.M33, u.Z)))));
  }
  static $Multiply1(a, u) {
    a = Copy(a);
    return Copy((a.IsIdentity ? u : Vector3.$create1(((MultiplyDouble(a.M11, u.X) + MultiplyDouble(a.M12, u.Y)) + MultiplyDouble(a.M13, u.Z)), ((MultiplyDouble(a.M21, u.X) + MultiplyDouble(a.M22, u.Y)) + MultiplyDouble(a.M23, u.Z)), ((MultiplyDouble(a.M31, u.X) + MultiplyDouble(a.M32, u.Y)) + MultiplyDouble(a.M33, u.Z)))));
  }
  static $op_Multiply2(m, a) {
    return Matrix3.$create0(MultiplyDouble(m.M11, a), MultiplyDouble(m.M12, a), MultiplyDouble(m.M13, a), MultiplyDouble(m.M21, a), MultiplyDouble(m.M22, a), MultiplyDouble(m.M23, a), MultiplyDouble(m.M31, a), MultiplyDouble(m.M32, a), MultiplyDouble(m.M33, a));
  }
  static $Multiply2(m, a) {
    return Matrix3.$create0(MultiplyDouble(m.M11, a), MultiplyDouble(m.M12, a), MultiplyDouble(m.M13, a), MultiplyDouble(m.M21, a), MultiplyDouble(m.M22, a), MultiplyDouble(m.M23, a), MultiplyDouble(m.M31, a), MultiplyDouble(m.M32, a), MultiplyDouble(m.M33, a));
  }
  static op_Equality(u, v) {
    return Matrix3.$Equals0(u, v);
  }
  static op_Inequality(u, v) {
    return (!Matrix3.$Equals0(u, v));
  }
  Determinant() {
    if (this.IsIdentity)
    {
      return 1;
    }
    return (((((MultiplyDouble(MultiplyDouble(this.$m11, this.$m22), this.$m33) + MultiplyDouble(MultiplyDouble(this.$m12, this.$m23), this.$m31)) + MultiplyDouble(MultiplyDouble(this.$m13, this.$m21), this.$m32)) - MultiplyDouble(MultiplyDouble(this.$m13, this.$m22), this.$m31)) - MultiplyDouble(MultiplyDouble(this.$m11, this.$m23), this.$m32)) - MultiplyDouble(MultiplyDouble(this.$m12, this.$m21), this.$m33));
  }
  Inverse() {
    if (this.IsIdentity)
    {
      return Matrix3.Identity;
    }
    let det = this.Determinant();
    if (MathHelper.$IsZero0(det))
    {
      throw new Errors.ArithmeticException("The matrix is not invertible.");
    }
    det = (1 / det);
    return Matrix3.$create0(MultiplyDouble(det, ((MultiplyDouble(this.$m22, this.$m33) - MultiplyDouble(this.$m23, this.$m32)))), MultiplyDouble(det, ((MultiplyDouble(this.$m13, this.$m32) - MultiplyDouble(this.$m12, this.$m33)))), MultiplyDouble(det, ((MultiplyDouble(this.$m12, this.$m23) - MultiplyDouble(this.$m13, this.$m22)))), MultiplyDouble(det, ((MultiplyDouble(this.$m23, this.$m31) - MultiplyDouble(this.$m21, this.$m33)))), MultiplyDouble(det, ((MultiplyDouble(this.$m11, this.$m33) - MultiplyDouble(this.$m13, this.$m31)))), MultiplyDouble(det, ((MultiplyDouble(this.$m13, this.$m21) - MultiplyDouble(this.$m11, this.$m23)))), MultiplyDouble(det, ((MultiplyDouble(this.$m21, this.$m32) - MultiplyDouble(this.$m22, this.$m31)))), MultiplyDouble(det, ((MultiplyDouble(this.$m12, this.$m31) - MultiplyDouble(this.$m11, this.$m32)))), MultiplyDouble(det, ((MultiplyDouble(this.$m11, this.$m22) - MultiplyDouble(this.$m12, this.$m21)))));
  }
  Transpose() {
    return Copy((this.IsIdentity ? Matrix3.Identity : Matrix3.$create0(this.$m11, this.$m21, this.$m31, this.$m12, this.$m22, this.$m32, this.$m13, this.$m23, this.$m33)));
  }
  static RotationX(angle) {
    let cos = DotNetMath.Cos(angle);
    let sin = DotNetMath.Sin(angle);
    return Matrix3.$create0(1, 0, 0, 0, cos, (-sin), 0, sin, cos);
  }
  static RotationY(angle) {
    let cos = DotNetMath.Cos(angle);
    let sin = DotNetMath.Sin(angle);
    return Matrix3.$create0(cos, 0, sin, 0, 1, 0, (-sin), 0, cos);
  }
  static RotationZ(angle) {
    let cos = DotNetMath.Cos(angle);
    let sin = DotNetMath.Sin(angle);
    return Matrix3.$create0(cos, (-sin), 0, sin, cos, 0, 0, 0, 1);
  }
  static $Scale0(value) {
    return Matrix3.$Scale2(value, value, value);
  }
  static $Scale1(value) {
    return Matrix3.$Scale2(value.X, value.Y, value.Z);
  }
  static $Scale2(x, y, z) {
    return Matrix3.$create0(x, 0, 0, 0, y, 0, 0, 0, z);
  }
  static Reflection(normal) {
    let n = Vector3.Normalize(normal);
    let a = n.X;
    let b = n.Y;
    let c = n.Z;
    return Matrix3.$create0((1 - MultiplyDouble((2 * a), a)), MultiplyDouble(((-2) * a), b), MultiplyDouble(((-2) * a), c), MultiplyDouble(((-2) * a), b), (1 - MultiplyDouble((2 * b), b)), MultiplyDouble(((-2) * b), c), MultiplyDouble(((-2) * a), c), MultiplyDouble(((-2) * b), c), (1 - MultiplyDouble((2 * c), c)));
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
  $Equals1(obj, threshold) {
    return ((((((((MathHelper.$IsEqual1(obj.M11, this.M11, threshold) && MathHelper.$IsEqual1(obj.M12, this.M12, threshold)) && MathHelper.$IsEqual1(obj.M13, this.M13, threshold)) && MathHelper.$IsEqual1(obj.M21, this.M21, threshold)) && MathHelper.$IsEqual1(obj.M22, this.M22, threshold)) && MathHelper.$IsEqual1(obj.M23, this.M23, threshold)) && MathHelper.$IsEqual1(obj.M31, this.M31, threshold)) && MathHelper.$IsEqual1(obj.M32, this.M32, threshold)) && MathHelper.$IsEqual1(obj.M33, this.M33, threshold));
  }
  $Equals2(obj) {
    let matrix;
    if ((obj instanceof Matrix3 && ((matrix = obj), true)))
    {
      return this.$Equals0(matrix);
    }
    return false;
  }
  GetHashCode() {
    return ((((((((DoubleHash(this.M11) ^ DoubleHash(this.M12)) ^ DoubleHash(this.M13)) ^ DoubleHash(this.M21)) ^ DoubleHash(this.M22)) ^ DoubleHash(this.M23)) ^ DoubleHash(this.M31)) ^ DoubleHash(this.M32)) ^ DoubleHash(this.M33));
  }
  $ToString0() {
    let separator = Culture.ListSeparator;
    let s = new StringBuilder();
    s.Append(Format(("|{0}{3} {1}{3} {2}|" + Culture.NewLine), this.$m11, this.$m12, this.$m13, separator));
    s.Append(Format(("|{0}{3} {1}{3} {2}|" + Culture.NewLine), this.$m21, this.$m22, this.$m23, separator));
    s.Append(Format("|{0}{3} {1}{3} {2}|", this.$m31, this.$m32, this.$m33, separator));
    return s.ToString();
  }
  $ToString1(provider) {
    let separator = Culture.ListSeparator;
    let s = new StringBuilder();
    s.Append(Format(("|{0}{3} {1}{3} {2}|" + Culture.NewLine), NumberText(this.$m11, provider), NumberText(this.$m12, provider), NumberText(this.$m13, provider), separator));
    s.Append(Format(("|{0}{3} {1}{3} {2}|" + Culture.NewLine), NumberText(this.$m21, provider), NumberText(this.$m22, provider), NumberText(this.$m23, provider), separator));
    s.Append(Format("|{0}{3} {1}{3} {2}|", NumberText(this.$m31, provider), NumberText(this.$m32, provider), NumberText(this.$m33, provider), separator));
    return s.ToString();
  }
  static op_Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Matrix3) && (args[1] instanceof Matrix3)) return Matrix3.$op_Multiply0(...args);
    if (args.length === 2 && (args[0] instanceof Matrix3) && (args[1] instanceof Vector3)) return Matrix3.$op_Multiply1(...args);
    if (args.length === 2 && (args[0] instanceof Matrix3) && (typeof args[1] === 'number')) return Matrix3.$op_Multiply2(...args);
    throw new ArgumentException("No matching Matrix3.op_Multiply overload. Consult native-port-manifest.json.");
  }
  static Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Matrix3) && (args[1] instanceof Matrix3)) return Matrix3.$Multiply0(...args);
    if (args.length === 2 && (args[0] instanceof Matrix3) && (args[1] instanceof Vector3)) return Matrix3.$Multiply1(...args);
    if (args.length === 2 && (args[0] instanceof Matrix3) && (typeof args[1] === 'number')) return Matrix3.$Multiply2(...args);
    throw new ArgumentException("No matching Matrix3.Multiply overload. Consult native-port-manifest.json.");
  }
  static Scale(...args) {
    if (args.length === 1 && (args[0] instanceof Vector3)) return Matrix3.$Scale1(...args);
    if (args.length === 3 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number')) return Matrix3.$Scale2(...args);
    if (args.length === 1 && (typeof args[0] === 'number')) return Matrix3.$Scale0(...args);
    throw new ArgumentException("No matching Matrix3.Scale overload. Consult native-port-manifest.json.");
  }
  static Equals(...args) {
    if (args.length === 3 && (args[0] instanceof Matrix3) && (args[1] instanceof Matrix3) && (typeof args[2] === 'number')) return Matrix3.$Equals1(...args);
    if (args.length === 2 && (args[0] instanceof Matrix3) && (args[1] instanceof Matrix3)) return Matrix3.$Equals0(...args);
    throw new ArgumentException("No matching Matrix3.Equals overload. Consult native-port-manifest.json.");
  }
  Equals(...args) {
    if (args.length === 2 && (args[0] instanceof Matrix3) && (typeof args[1] === 'number')) return this.$Equals1(...args);
    if (args.length === 1 && (args[0] instanceof Matrix3)) return this.$Equals0(...args);
    if (args.length === 1 && (true)) return this.$Equals2(...args);
    throw new ArgumentException("No matching Matrix3.Equals overload. Consult native-port-manifest.json.");
  }
  ToString(...args) {
    if (args.length === 1 && (args[0] === null || typeof args[0] === 'object' || typeof args[0] === 'string')) return this.$ToString1(...args);
    if (args.length === 0) return this.$ToString0(...args);
    throw new ArgumentException("No matching Matrix3.ToString overload. Consult native-port-manifest.json.");
  }
}
