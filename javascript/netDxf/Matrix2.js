// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { MathHelper } from './MathHelper.js';
import { Vector2 } from './Vector2.js';

export class Matrix2 {
  // C# backing state is prefixed with $; public members retain their original names.
  $m11 = 0;
  $m12 = 0;
  $m21 = 0;
  $m22 = 0;
  $dirty = false;
  $isIdentity = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) return;
    if (args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number')) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching Matrix2 constructor. Use Matrix2.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(m11, m12, m21, m22) {
    this.$m11 = m11;
    this.$m12 = m12;
    this.$m21 = m21;
    this.$m22 = m22;
    this.$dirty = true;
    this.$isIdentity = false;
  }
  static CreateOverload(signature, ...args) {
    if (signature === '' && args.length === 0) return new Matrix2();
    if (signature === "double,double,double,double") {
      if (!(args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Matrix2.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new Matrix2(ConstructorTag, 0, args); }
  [CopyValue]() {
    const value = new Matrix2();
    value.$m11 = this.$m11;
    value.$m12 = this.$m12;
    value.$m21 = this.$m21;
    value.$m22 = this.$m22;
    value.$dirty = this.$dirty;
    value.$isIdentity = this.$isIdentity;
    return value;
  }
  $assign(value) {
    this.$m11 = value.$m11;
    this.$m12 = value.$m12;
    this.$m21 = value.$m21;
    this.$m22 = value.$m22;
    this.$dirty = value.$dirty;
    this.$isIdentity = value.$isIdentity;
    return this;
  }
  static get Zero() {
    return Init(Matrix2.$create0(0, 0, 0, 0), $new => { $new.$dirty = false; $new.$isIdentity = false; });
  }
  static get Identity() {
    return Init(Matrix2.$create0(1, 0, 0, 1), $new => { $new.$dirty = false; $new.$isIdentity = true; });
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
          default:
            throw new Errors.ArgumentOutOfRangeException("column");
        } }
      default:
        throw new Errors.ArgumentOutOfRangeException("row");
    } }
  }
  set_Item(row, column, value) {
    { const $switch3 = row;
    switch (true) {
      case $switch3 === 0:
        { const $switch4 = column;
        switch (true) {
          case $switch4 === 0:
            this.$m11 = value;
            break;
          case $switch4 === 1:
            this.$m12 = value;
            break;
          default:
            throw new Errors.ArgumentOutOfRangeException("column");
        } }
        break;
      case $switch3 === 1:
        { const $switch5 = column;
        switch (true) {
          case $switch5 === 0:
            this.$m21 = value;
            break;
          case $switch5 === 1:
            this.$m22 = value;
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
      this.$isIdentity = true;
      return this.$isIdentity;
    }
    return this.$isIdentity;
  }
  static op_Addition(a, b) {
    return Matrix2.$create0((a.M11 + b.M11), (a.M12 + b.M12), (a.M21 + b.M21), (a.M22 + b.M22));
  }
  static Add(a, b) {
    return Matrix2.$create0((a.M11 + b.M11), (a.M12 + b.M12), (a.M21 + b.M21), (a.M22 + b.M22));
  }
  static op_Subtraction(a, b) {
    return Matrix2.$create0((a.M11 - b.M11), (a.M12 - b.M12), (a.M21 - b.M21), (a.M22 - b.M22));
  }
  static Subtract(a, b) {
    return Matrix2.$create0((a.M11 - b.M11), (a.M12 - b.M12), (a.M21 - b.M21), (a.M22 - b.M22));
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
    return Matrix2.$create0((MultiplyDouble(a.M11, b.M11) + MultiplyDouble(a.M12, b.M21)), (MultiplyDouble(a.M11, b.M12) + MultiplyDouble(a.M12, b.M22)), (MultiplyDouble(a.M21, b.M11) + MultiplyDouble(a.M22, b.M21)), (MultiplyDouble(a.M21, b.M12) + MultiplyDouble(a.M22, b.M22)));
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
    return Matrix2.$create0((MultiplyDouble(a.M11, b.M11) + MultiplyDouble(a.M12, b.M21)), (MultiplyDouble(a.M11, b.M12) + MultiplyDouble(a.M12, b.M22)), (MultiplyDouble(a.M21, b.M11) + MultiplyDouble(a.M22, b.M21)), (MultiplyDouble(a.M21, b.M12) + MultiplyDouble(a.M22, b.M22)));
  }
  static $op_Multiply1(a, u) {
    a = Copy(a);
    return Copy((a.IsIdentity ? u : Vector2.$create1((MultiplyDouble(a.M11, u.X) + MultiplyDouble(a.M12, u.Y)), (MultiplyDouble(a.M21, u.X) + MultiplyDouble(a.M22, u.Y)))));
  }
  static $Multiply1(a, u) {
    a = Copy(a);
    return Copy((a.IsIdentity ? u : Vector2.$create1((MultiplyDouble(a.M11, u.X) + MultiplyDouble(a.M12, u.Y)), (MultiplyDouble(a.M21, u.X) + MultiplyDouble(a.M22, u.Y)))));
  }
  static $op_Multiply2(m, a) {
    return Matrix2.$create0(MultiplyDouble(m.M11, a), MultiplyDouble(m.M12, a), MultiplyDouble(m.M21, a), MultiplyDouble(m.M22, a));
  }
  static $Multiply2(m, a) {
    return Matrix2.$create0(MultiplyDouble(m.M11, a), MultiplyDouble(m.M12, a), MultiplyDouble(m.M21, a), MultiplyDouble(m.M22, a));
  }
  static op_Equality(u, v) {
    return Matrix2.$Equals0(u, v);
  }
  static op_Inequality(u, v) {
    return (!Matrix2.$Equals0(u, v));
  }
  Determinant() {
    return (this.IsIdentity ? 1 : (MultiplyDouble(this.$m11, this.$m22) - MultiplyDouble(this.$m12, this.$m21)));
  }
  Inverse() {
    if (this.IsIdentity)
    {
      return Matrix2.Identity;
    }
    let det = this.Determinant();
    if (MathHelper.$IsZero0(det))
    {
      throw new Errors.ArithmeticException("The matrix is not invertible.");
    }
    det = (1 / det);
    return Matrix2.$create0(MultiplyDouble(det, this.$m22), MultiplyDouble((-det), this.$m12), MultiplyDouble((-det), this.$m21), MultiplyDouble(det, this.$m11));
  }
  Transpose() {
    return Copy((this.IsIdentity ? Matrix2.Identity : Matrix2.$create0(this.$m11, this.$m21, this.$m12, this.$m22)));
  }
  static Rotation(angle) {
    let cos = DotNetMath.Cos(angle);
    let sin = DotNetMath.Sin(angle);
    return Matrix2.$create0(cos, (-sin), sin, cos);
  }
  static $Scale0(value) {
    return Matrix2.$Scale2(value, value);
  }
  static $Scale1(value) {
    return Matrix2.$Scale2(value.X, value.Y);
  }
  static $Scale2(x, y) {
    return Matrix2.$create0(x, 0, 0, y);
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
    return (((MathHelper.$IsEqual1(obj.M11, this.M11, threshold) && MathHelper.$IsEqual1(obj.M12, this.M12, threshold)) && MathHelper.$IsEqual1(obj.M21, this.M21, threshold)) && MathHelper.$IsEqual1(obj.M22, this.M22, threshold));
  }
  $Equals2(obj) {
    let matrix;
    if ((obj instanceof Matrix2 && ((matrix = obj), true)))
    {
      return this.$Equals0(matrix);
    }
    return false;
  }
  GetHashCode() {
    return (((DoubleHash(this.M11) ^ DoubleHash(this.M12)) ^ DoubleHash(this.M21)) ^ DoubleHash(this.M22));
  }
  $ToString0() {
    let separator = Culture.ListSeparator;
    let s = new StringBuilder();
    s.Append(Format(("|{0}{2} {1}|" + Culture.NewLine), this.$m11, this.$m12, separator));
    s.Append(Format("|{0}{2} {1}|", this.$m21, this.$m22, separator));
    return s.ToString();
  }
  $ToString1(provider) {
    let separator = Culture.ListSeparator;
    let s = new StringBuilder();
    s.Append(Format(("|{0}{2} {1}|" + Culture.NewLine), NumberText(this.$m11, provider), NumberText(this.$m12, provider), separator));
    s.Append(Format("|{0}{2} {1}|", NumberText(this.$m21, provider), NumberText(this.$m22, provider), separator));
    return s.ToString();
  }
  static op_Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Matrix2) && (args[1] instanceof Matrix2)) return Matrix2.$op_Multiply0(...args);
    if (args.length === 2 && (args[0] instanceof Matrix2) && (args[1] instanceof Vector2)) return Matrix2.$op_Multiply1(...args);
    if (args.length === 2 && (args[0] instanceof Matrix2) && (typeof args[1] === 'number')) return Matrix2.$op_Multiply2(...args);
    throw new ArgumentException("No matching Matrix2.op_Multiply overload. Consult native-port-manifest.json.");
  }
  static Multiply(...args) {
    if (args.length === 2 && (args[0] instanceof Matrix2) && (args[1] instanceof Matrix2)) return Matrix2.$Multiply0(...args);
    if (args.length === 2 && (args[0] instanceof Matrix2) && (args[1] instanceof Vector2)) return Matrix2.$Multiply1(...args);
    if (args.length === 2 && (args[0] instanceof Matrix2) && (typeof args[1] === 'number')) return Matrix2.$Multiply2(...args);
    throw new ArgumentException("No matching Matrix2.Multiply overload. Consult native-port-manifest.json.");
  }
  static Scale(...args) {
    if (args.length === 1 && (args[0] instanceof Vector2)) return Matrix2.$Scale1(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number')) return Matrix2.$Scale2(...args);
    if (args.length === 1 && (typeof args[0] === 'number')) return Matrix2.$Scale0(...args);
    throw new ArgumentException("No matching Matrix2.Scale overload. Consult native-port-manifest.json.");
  }
  static Equals(...args) {
    if (args.length === 3 && (args[0] instanceof Matrix2) && (args[1] instanceof Matrix2) && (typeof args[2] === 'number')) return Matrix2.$Equals1(...args);
    if (args.length === 2 && (args[0] instanceof Matrix2) && (args[1] instanceof Matrix2)) return Matrix2.$Equals0(...args);
    throw new ArgumentException("No matching Matrix2.Equals overload. Consult native-port-manifest.json.");
  }
  Equals(...args) {
    if (args.length === 2 && (args[0] instanceof Matrix2) && (typeof args[1] === 'number')) return this.$Equals1(...args);
    if (args.length === 1 && (args[0] instanceof Matrix2)) return this.$Equals0(...args);
    if (args.length === 1 && (true)) return this.$Equals2(...args);
    throw new ArgumentException("No matching Matrix2.Equals overload. Consult native-port-manifest.json.");
  }
  ToString(...args) {
    if (args.length === 1 && (args[0] === null || typeof args[0] === 'object' || typeof args[0] === 'string')) return this.$ToString1(...args);
    if (args.length === 0) return this.$ToString0(...args);
    throw new ArgumentException("No matching Matrix2.ToString overload. Consult native-port-manifest.json.");
  }
}
