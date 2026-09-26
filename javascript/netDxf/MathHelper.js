// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Matrix3 } from './Matrix3.js';
import { Vector2 } from './Vector2.js';
import { Vector3 } from './Vector3.js';

export class MathHelper {
  // C# backing state is prefixed with $; public members retain their original names.
  static get DegToRad() { return 0.017453292519943295; }
  static get RadToDeg() { return 57.29577951308232; }
  static get DegToGrad() { return 1.1111111111111112; }
  static get GradToDeg() { return 0.9; }
  static get HalfPI() { return 1.5707963267948966; }
  static get PI() { return 3.141592653589793; }
  static get ThreeHalfPI() { return 4.71238898038469; }
  static get TwoPI() { return 6.283185307179586; }
  static $epsilon = 1E-12;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching MathHelper constructor. Use MathHelper.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static get Epsilon() {
    return MathHelper.$epsilon;
  }
  static set Epsilon(value) {
    if ((value <= 0))
    {
      throw new Errors.ArgumentOutOfRangeException("value", value, "The epsilon value must be a positive number greater than zero.");
    }
    MathHelper.$epsilon = value;
  }
  static $Sign0(number) {
    return (MathHelper.$IsZero0(number) ? 0 : DotNetMath.Sign(number));
  }
  static $Sign1(number, threshold) {
    return (MathHelper.$IsZero1(number, threshold) ? 0 : DotNetMath.Sign(number));
  }
  static $IsOne0(number) {
    return MathHelper.$IsOne1(number, MathHelper.Epsilon);
  }
  static $IsOne1(number, threshold) {
    return MathHelper.$IsZero1((number - 1), threshold);
  }
  static $IsZero0(number) {
    return MathHelper.$IsZero1(number, MathHelper.Epsilon);
  }
  static $IsZero1(number, threshold) {
    return ((number >= (-threshold)) && (number <= threshold));
  }
  static $IsEqual0(a, b) {
    return MathHelper.$IsEqual1(a, b, MathHelper.Epsilon);
  }
  static $IsEqual1(a, b, threshold) {
    return MathHelper.$IsZero1((a - b), threshold);
  }
  static $Transform0(point, rotation, from, to) {
    if (MathHelper.$IsZero0(rotation))
    {
      return Copy(point);
    }
    let sin = DotNetMath.Sin(rotation);
    let cos = DotNetMath.Cos(rotation);
    { const $switch0 = from;
    switch (true) {
      case $switch0 === 0 && (to === 1):
        return Vector2.$create1((MultiplyDouble(point.X, cos) + MultiplyDouble(point.Y, sin)), (MultiplyDouble((-point.X), sin) + MultiplyDouble(point.Y, cos)));
      case $switch0 === 1 && (to === 0):
        return Vector2.$create1((MultiplyDouble(point.X, cos) - MultiplyDouble(point.Y, sin)), (MultiplyDouble(point.X, sin) + MultiplyDouble(point.Y, cos)));
      default:
        return Copy(point);
    } }
  }
  static $Transform1(points, rotation, from, to) {
    if ((points === null))
    {
      throw new Errors.ArgumentNullException("points");
    }
    if (MathHelper.$IsZero0(rotation))
    {
      return new List(points);
    }
    let sin = DotNetMath.Sin(rotation);
    let cos = DotNetMath.Cos(rotation);
    let transPoints = null;
    { const $switch1 = from;
    switch (true) {
      case $switch1 === 0 && (to === 1):
        {
          transPoints = new List();
          for (const $item2 of points) {
            let p = Copy($item2);
            transPoints.Add(Vector2.$create1((MultiplyDouble(p.X, cos) + MultiplyDouble(p.Y, sin)), (MultiplyDouble((-p.X), sin) + MultiplyDouble(p.Y, cos))));
          }
          return transPoints;
        }
      case $switch1 === 1 && (to === 0):
        {
          transPoints = new List();
          for (const $item3 of points) {
            let p = Copy($item3);
            transPoints.Add(Vector2.$create1((MultiplyDouble(p.X, cos) - MultiplyDouble(p.Y, sin)), (MultiplyDouble(p.X, sin) + MultiplyDouble(p.Y, cos))));
          }
          return transPoints;
        }
      default:
        return new List(points);
    } }
  }
  static $Transform2(point, zAxis, from, to) {
    let trans = MathHelper.ArbitraryAxis(zAxis);
    { const $switch4 = from;
    switch (true) {
      case $switch4 === 0 && (to === 1):
        trans = trans.Transpose();
        return Matrix3.$op_Multiply1(trans, point);
      case $switch4 === 1 && (to === 0):
        return Matrix3.$op_Multiply1(trans, point);
      default:
        return Copy(point);
    } }
  }
  static $Transform3(points, zAxis, from, to) {
    if ((points === null))
    {
      throw new Errors.ArgumentNullException("points");
    }
    let trans = MathHelper.ArbitraryAxis(zAxis);
    let transPoints = null;
    { const $switch5 = from;
    switch (true) {
      case $switch5 === 0 && (to === 1):
        {
          transPoints = new List();
          trans = trans.Transpose();
          for (const $item6 of points) {
            let p = Copy($item6);
            transPoints.Add(Matrix3.$op_Multiply1(trans, p));
          }
          return transPoints;
        }
      case $switch5 === 1 && (to === 0):
        {
          transPoints = new List();
          for (const $item7 of points) {
            let p = Copy($item7);
            transPoints.Add(Matrix3.$op_Multiply1(trans, p));
          }
          return transPoints;
        }
      default:
        return new List(points);
    } }
  }
  static $Transform4(point, zAxis, elevation) {
    let trans = MathHelper.ArbitraryAxis(zAxis);
    return Matrix3.$op_Multiply1(trans, Vector3.$create1(point.X, point.Y, elevation));
  }
  static $Transform5(points, zAxis, elevation) {
    if ((points === null))
    {
      throw new Errors.ArgumentNullException("points");
    }
    let transPoints = new List();
    let trans = MathHelper.ArbitraryAxis(zAxis);
    for (const $item8 of points) {
      let p = Copy($item8);
      transPoints.Add(Matrix3.$op_Multiply1(trans, Vector3.$create1(p.X, p.Y, elevation)));
    }
    return transPoints;
  }
  static $Transform6(point, zAxis, elevation) {
    let trans = MathHelper.ArbitraryAxis(zAxis).Transpose();
    let p = Matrix3.$op_Multiply1(trans, point);
    elevation.value = p.Z;
    return Vector2.$create1(p.X, p.Y);
  }
  static $Transform7(points, zAxis, elevation) {
    if ((points === null))
    {
      throw new Errors.ArgumentNullException("points");
    }
    let transPoints = new List();
    let trans = MathHelper.ArbitraryAxis(zAxis).Transpose();
    elevation.value = 0;
    for (const $item9 of points) {
      let point = Copy($item9);
      let p = Matrix3.$op_Multiply1(trans, point);
      elevation.value += p.Z;
      transPoints.Add(Vector2.$create1(p.X, p.Y));
    }
    elevation.value /= transPoints.length;
    return transPoints;
  }
  static ArbitraryAxis(zAxis) {
    zAxis = Copy(zAxis);
    zAxis.Normalize();
    if (zAxis.$Equals0(Vector3.UnitZ))
    {
      return Matrix3.Identity;
    }
    let wY = Vector3.UnitY;
    let wZ = Vector3.UnitZ;
    let aX = new Vector3();
    if ((((DotNetMath.Abs(zAxis.X) < (1 / 64))) && ((DotNetMath.Abs(zAxis.Y) < (1 / 64)))))
    {
      aX = Vector3.CrossProduct(wY, zAxis);
    }
    else
    {
      aX = Vector3.CrossProduct(wZ, zAxis);
    }
    aX.Normalize();
    let aY = Vector3.CrossProduct(zAxis, aX);
    aY.Normalize();
    return Matrix3.$create0(aX.X, aY.X, zAxis.X, aX.Y, aY.Y, zAxis.Y, aX.Z, aY.Z, zAxis.Z);
  }
  static $PointLineDistance0(p, origin, dir) {
    let t = Vector3.DotProduct(dir, Vector3.op_Subtraction(p, origin));
    let pPrime = Vector3.op_Addition(origin, Vector3.$op_Multiply1(t, dir));
    let vec = Vector3.op_Subtraction(p, pPrime);
    let distanceSquared = Vector3.DotProduct(vec, vec);
    return DotNetMath.Sqrt(distanceSquared);
  }
  static $PointLineDistance1(p, origin, dir) {
    let t = Vector2.DotProduct(dir, Vector2.op_Subtraction(p, origin));
    let pPrime = Vector2.op_Addition(origin, Vector2.$op_Multiply1(t, dir));
    let vec = Vector2.op_Subtraction(p, pPrime);
    let distanceSquared = Vector2.DotProduct(vec, vec);
    return DotNetMath.Sqrt(distanceSquared);
  }
  static $PointInSegment0(p, start, end) {
    let dir = Vector3.op_Subtraction(end, start);
    let pPrime = Vector3.op_Subtraction(p, start);
    let t = Vector3.DotProduct(dir, pPrime);
    if ((t < 0))
    {
      return (-1);
    }
    let dot = Vector3.DotProduct(dir, dir);
    if ((t > dot))
    {
      return 1;
    }
    return 0;
  }
  static $PointInSegment1(p, start, end) {
    let dir = Vector2.op_Subtraction(end, start);
    let pPrime = Vector2.op_Subtraction(p, start);
    let t = Vector2.DotProduct(dir, pPrime);
    if ((t < 0))
    {
      return (-1);
    }
    let dot = Vector2.DotProduct(dir, dir);
    if ((t > dot))
    {
      return 1;
    }
    return 0;
  }
  static $FindIntersection0(point0, dir0, point1, dir1) {
    return MathHelper.$FindIntersection1(point0, dir0, point1, dir1, MathHelper.Epsilon);
  }
  static $FindIntersection1(point0, dir0, point1, dir1, threshold) {
    if (Vector2.$AreParallel1(dir0, dir1, threshold))
    {
      return Vector2.NaN;
    }
    let v = Vector2.op_Subtraction(point1, point0);
    let cross = Vector2.CrossProduct(dir0, dir1);
    let s = (((MultiplyDouble(v.X, dir1.Y) - MultiplyDouble(v.Y, dir1.X))) / cross);
    return Vector2.op_Addition(point0, Vector2.$op_Multiply1(s, dir0));
  }
  static NormalizeAngle(angle) {
    let normalized = RemainderDouble(angle, 360);
    if ((MathHelper.$IsZero0(normalized) || MathHelper.$IsEqual0(DotNetMath.Abs(normalized), 360)))
    {
      return 0;
    }
    if ((normalized < 0))
    {
      return (360 + normalized);
    }
    return normalized;
  }
  static RoundToNearest(number, roundTo) {
    let multiplier = DotNetMath.Round((number / roundTo), 0);
    return MultiplyDouble(multiplier, roundTo);
  }
  static ArcFromBulge(startPoint, endPoint, bulge) {
    if (MathHelper.$IsZero0(bulge))
    {
      throw new Errors.ArgumentOutOfRangeException("bulge", bulge, "The bulge value must be different than zero to make an arc.");
    }
    let theta = (4 * DotNetMath.Atan(DotNetMath.Abs(bulge)));
    let dist = (0.5 * Vector2.Distance(startPoint, endPoint));
    let gamma = (0.5 * ((3.141592653589793 - theta)));
    let phi = (Vector2.$Angle1(startPoint, endPoint) + MultiplyDouble(DotNetMath.Sign(bulge), gamma));
    let radius = (dist / DotNetMath.Sin((0.5 * theta)));
    let center = Vector2.$create1((startPoint.X + MultiplyDouble(radius, DotNetMath.Cos(phi))), (startPoint.Y + MultiplyDouble(radius, DotNetMath.Sin(phi))));
    let startAngle = 0;
    let endAngle = 0;
    if ((bulge > 0))
    {
      startAngle = MathHelper.NormalizeAngle((Vector2.$Angle0(Vector2.op_Subtraction(startPoint, center)) * 57.29577951308232));
      endAngle = MathHelper.NormalizeAngle((startAngle + (theta * 57.29577951308232)));
    }
    else
    {
      endAngle = MathHelper.NormalizeAngle((Vector2.$Angle0(Vector2.op_Subtraction(startPoint, center)) * 57.29577951308232));
      startAngle = MathHelper.NormalizeAngle((endAngle - (theta * 57.29577951308232)));
    }
    return new Tuple(center, radius, startAngle, endAngle);
  }
  static ArcToBulge(center, radius, startAngle, endAngle) {
    if ((radius <= 0))
    {
      throw new Errors.ArgumentOutOfRangeException("radius", radius, "The arc radius larger than zero.");
    }
    let arcAngle = (((endAngle - startAngle)) * 0.017453292519943295);
    let bulge = DotNetMath.Tan((arcAngle / 4));
    let startPoint = Vector2.Polar(center, radius, (startAngle * 0.017453292519943295));
    let endPoint = Vector2.Polar(center, radius, (endAngle * 0.017453292519943295));
    return new Tuple(startPoint, endPoint, bulge);
  }
  static Sign(...args) {
    if (args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number')) return MathHelper.$Sign1(...args);
    if (args.length === 1 && (typeof args[0] === 'number')) return MathHelper.$Sign0(...args);
    throw new ArgumentException("No matching MathHelper.Sign overload. Consult native-port-manifest.json.");
  }
  static IsOne(...args) {
    if (args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number')) return MathHelper.$IsOne1(...args);
    if (args.length === 1 && (typeof args[0] === 'number')) return MathHelper.$IsOne0(...args);
    throw new ArgumentException("No matching MathHelper.IsOne overload. Consult native-port-manifest.json.");
  }
  static IsZero(...args) {
    if (args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number')) return MathHelper.$IsZero1(...args);
    if (args.length === 1 && (typeof args[0] === 'number')) return MathHelper.$IsZero0(...args);
    throw new ArgumentException("No matching MathHelper.IsZero overload. Consult native-port-manifest.json.");
  }
  static IsEqual(...args) {
    if (args.length === 3 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number')) return MathHelper.$IsEqual1(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number')) return MathHelper.$IsEqual0(...args);
    throw new ArgumentException("No matching MathHelper.IsEqual overload. Consult native-port-manifest.json.");
  }
  static Transform(...args) {
    if (args.length === 4 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (typeof args[2] === 'number') && (typeof args[3] === 'number')) return MathHelper.$Transform2(...args);
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector3) && (typeof args[2] === 'number')) return MathHelper.$Transform4(...args);
    if (args.length === 3 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (args[2] != null && typeof args[2] === 'object' && 'value' in args[2])) return MathHelper.$Transform6(...args);
    if (args.length === 4 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function') && (args[1] instanceof Vector3) && (typeof args[2] === 'number') && (typeof args[3] === 'number')) return MathHelper.$Transform3(...args);
    if (args.length === 4 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number')) return MathHelper.$Transform0(...args);
    if (args.length === 3 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function') && (args[1] instanceof Vector3) && (typeof args[2] === 'number')) return MathHelper.$Transform5(...args);
    if (args.length === 3 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function') && (args[1] instanceof Vector3) && (args[2] != null && typeof args[2] === 'object' && 'value' in args[2])) return MathHelper.$Transform7(...args);
    if (args.length === 4 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number')) return MathHelper.$Transform1(...args);
    throw new ArgumentException("No matching MathHelper.Transform overload. Consult native-port-manifest.json.");
  }
  static PointLineDistance(...args) {
    if (args.length === 3 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (args[2] instanceof Vector3)) return MathHelper.$PointLineDistance0(...args);
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2)) return MathHelper.$PointLineDistance1(...args);
    throw new ArgumentException("No matching MathHelper.PointLineDistance overload. Consult native-port-manifest.json.");
  }
  static PointInSegment(...args) {
    if (args.length === 3 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (args[2] instanceof Vector3)) return MathHelper.$PointInSegment0(...args);
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2)) return MathHelper.$PointInSegment1(...args);
    throw new ArgumentException("No matching MathHelper.PointInSegment overload. Consult native-port-manifest.json.");
  }
  static FindIntersection(...args) {
    if (args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (args[3] instanceof Vector2) && (typeof args[4] === 'number')) return MathHelper.$FindIntersection1(...args);
    if (args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (args[3] instanceof Vector2)) return MathHelper.$FindIntersection0(...args);
    throw new ArgumentException("No matching MathHelper.FindIntersection overload. Consult native-port-manifest.json.");
  }
}
