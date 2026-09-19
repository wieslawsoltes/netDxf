// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { BezierCurve } from './BezierCurve.js';
import { Vector3 } from './Vector3.js';

export class BezierCurveCubic extends BezierCurve {
  // C# backing state is prefixed with $; public members retain their original names.
  constructor(...args) {
    if (args.length === 4 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (args[2] instanceof Vector3) && (args[3] instanceof Vector3)) {
      let startPoint = Copy(args[0]);
      let firstControlPoint = Copy(args[1]);
      let secondControlPoint = Copy(args[2]);
      let endPoint = Copy(args[3]);
      super([Copy(startPoint), Copy(firstControlPoint), Copy(secondControlPoint), Copy(endPoint)], 3);
      return;
    }
    if (args.length === 1 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function')) {
      let controlPoints = args[0];
      super(controlPoints, 3);
      return;
    }
    throw new ArgumentException("No matching BezierCurveCubic constructor. Use BezierCurveCubic.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    if (signature === "System.Collections.Generic.IEnumerable\u003CnetDxf.Vector3\u003E") return new BezierCurveCubic(...args);
    if (signature === "netDxf.Vector3,netDxf.Vector3,netDxf.Vector3,netDxf.Vector3") return new BezierCurveCubic(...args);
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  get StartPoint() {
    return Copy(GetElement(this.$controlPoints, 0));
  }
  set StartPoint(value) {
    SetElement(this.$controlPoints, 0, Copy(value));
  }
  get FirstControlPoint() {
    return Copy(GetElement(this.$controlPoints, 1));
  }
  set FirstControlPoint(value) {
    SetElement(this.$controlPoints, 1, Copy(value));
  }
  get SecondControlPoint() {
    return Copy(GetElement(this.$controlPoints, 2));
  }
  set SecondControlPoint(value) {
    SetElement(this.$controlPoints, 2, Copy(value));
  }
  get EndPoint() {
    return Copy(GetElement(this.$controlPoints, 3));
  }
  set EndPoint(value) {
    SetElement(this.$controlPoints, 3, Copy(value));
  }
  CalculatePoint(t) {
    if (((t < 0) || (t > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("t", t, "The parameter t must be between 0.0 and 1.0.");
    }
    let c = (1 - t);
    return Vector3.op_Addition(Vector3.op_Addition(Vector3.op_Addition(Vector3.$op_Multiply1(MultiplyDouble(MultiplyDouble(c, c), c), this.StartPoint), Vector3.$op_Multiply1(MultiplyDouble(MultiplyDouble((3 * t), c), c), this.FirstControlPoint)), Vector3.$op_Multiply1(MultiplyDouble(MultiplyDouble((3 * c), t), t), this.SecondControlPoint)), Vector3.$op_Multiply1(MultiplyDouble(MultiplyDouble(t, t), t), this.EndPoint));
  }
  CalculateTangent(t) {
    if (((t < 0) || (t > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("t", t, "The parameter t must be between 0.0 and 1.0.");
    }
    let c = (1 - t);
    return Vector3.Normalize(Vector3.op_Addition(Vector3.op_Addition(Vector3.op_Addition(Vector3.$op_Multiply1(MultiplyDouble((-c), c), this.StartPoint), Vector3.$op_Multiply1(((MultiplyDouble(c, c) - MultiplyDouble((2 * c), t))), this.FirstControlPoint)), Vector3.$op_Multiply1(((MultiplyDouble((2 * c), t) - MultiplyDouble(t, t))), this.SecondControlPoint)), Vector3.$op_Multiply1(MultiplyDouble(t, t), this.EndPoint)));
  }
  Split(t) {
    if (((t < 0) || (t > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("t", t, "The parameter t must be between 0.0 and 1.0.");
    }
    let p12 = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(this.FirstControlPoint, this.StartPoint)), t), this.StartPoint);
    let p23 = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(this.SecondControlPoint, this.FirstControlPoint)), t), this.FirstControlPoint);
    let p123 = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(p23, p12)), t), p12);
    let p34 = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(this.EndPoint, this.SecondControlPoint)), t), this.SecondControlPoint);
    let p234 = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(p34, p23)), t), p23);
    let breakPoint = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(p234, p123)), t), p123);
    return [new BezierCurveCubic(this.StartPoint, p12, p123, breakPoint), new BezierCurveCubic(breakPoint, p234, p34, this.EndPoint)];
  }
  Reverse() {
    (this.$controlPoints).reverse();
  }
  PolygonalVertexes(precision) {
    if ((precision < 2))
    {
      throw new Errors.ArgumentOutOfRangeException("precision", precision, "The precision must be equal or greater than two.");
    }
    let vertexes = new List();
    let delta = (1 / ((precision - 1)));
    for (let i = 0; (i < precision); (i++))
    {
      let t = MultiplyDouble(delta, i);
      vertexes.Add(this.CalculatePoint(t));
    }
    return vertexes;
  }
  static CreateFromFitPoints(fitPoints) {
    if ((fitPoints === null))
    {
      throw new Errors.ArgumentNullException("fitPoints");
    }
    let points = Array.from(fitPoints, Copy);
    let numFitPoints = points.length;
    if ((numFitPoints < 2))
    {
      throw new Errors.ArgumentOutOfRangeException("fitPoints", numFitPoints, "At least two fit points required.");
    }
    let n = (numFitPoints - 1);
    let curves = new List();
    let firstControlPoint = new Vector3();
    let secondControlPoint = new Vector3();
    if ((n === 1))
    {
      firstControlPoint = Vector3.op_Addition(GetElement(points, 0), Vector3.$op_Division0((Vector3.op_Subtraction(GetElement(points, 1), GetElement(points, 0))), 3));
      secondControlPoint = Vector3.op_Addition(GetElement(points, 1), Vector3.$op_Division0((Vector3.op_Subtraction(GetElement(points, 0), GetElement(points, 1))), 3));
      curves.Add(new BezierCurveCubic(GetElement(points, 0), firstControlPoint, secondControlPoint, GetElement(points, 1)));
      return curves;
    }
    let rhs = Array.from({ length: n }, () => 0);
    for (let i = 1; (i < (n - 1)); (i++))
    {
      SetElement(rhs, i, ((4 * GetElement(points, i).X) + (2 * GetElement(points, (i + 1)).X)));
    }
    SetElement(rhs, 0, (GetElement(points, 0).X + (2 * GetElement(points, 1).X)));
    SetElement(rhs, (n - 1), ((((8 * GetElement(points, (n - 1)).X) + GetElement(points, n).X)) / 2));
    let x = BezierCurveCubic.GetFirstControlPoints(rhs);
    for (let i = 1; (i < (n - 1)); (i++))
    {
      SetElement(rhs, i, ((4 * GetElement(points, i).Y) + (2 * GetElement(points, (i + 1)).Y)));
    }
    SetElement(rhs, 0, (GetElement(points, 0).Y + (2 * GetElement(points, 1).Y)));
    SetElement(rhs, (n - 1), ((((8 * GetElement(points, (n - 1)).Y) + GetElement(points, n).Y)) / 2));
    let y = BezierCurveCubic.GetFirstControlPoints(rhs);
    for (let i = 1; (i < (n - 1)); (i++))
    {
      SetElement(rhs, i, ((4 * GetElement(points, i).Z) + (2 * GetElement(points, (i + 1)).Z)));
    }
    SetElement(rhs, 0, (GetElement(points, 0).Z + (2 * GetElement(points, 1).Z)));
    SetElement(rhs, (n - 1), ((((8 * GetElement(points, (n - 1)).Z) + GetElement(points, n).Z)) / 2));
    let z = BezierCurveCubic.GetFirstControlPoints(rhs);
    for (let i = 0; (i < n); (i++))
    {
      firstControlPoint = Vector3.$create1(GetElement(x, i), GetElement(y, i), GetElement(z, i));
      if ((i < (n - 1)))
      {
        secondControlPoint = Vector3.$create1(((2 * GetElement(points, (i + 1)).X) - GetElement(x, (i + 1))), ((2 * GetElement(points, (i + 1)).Y) - GetElement(y, (i + 1))), ((2 * GetElement(points, (i + 1)).Z) - GetElement(z, (i + 1))));
      }
      else
      {
        secondControlPoint = Vector3.$create1((((GetElement(points, n).X + GetElement(x, (n - 1)))) / 2), (((GetElement(points, n).Y + GetElement(y, (n - 1)))) / 2), (((GetElement(points, n).Z + GetElement(z, (n - 1)))) / 2));
      }
      curves.Add(new BezierCurveCubic(GetElement(points, i), firstControlPoint, secondControlPoint, GetElement(points, (i + 1))));
    }
    return curves;
  }
  static GetFirstControlPoints(rhs) {
    let n = rhs.length;
    let x = Array.from({ length: n }, () => 0);
    let tmp = Array.from({ length: n }, () => 0);
    let b = 2;
    SetElement(x, 0, (GetElement(rhs, 0) / b));
    for (let i = 1; (i < n); (i++))
    {
      SetElement(tmp, i, (1 / b));
      b = ((((i < (n - 1)) ? 4 : 3.5)) - GetElement(tmp, i));
      SetElement(x, i, (((GetElement(rhs, i) - GetElement(x, (i - 1)))) / b));
    }
    for (let i = 1; (i < n); (i++))
    {
      SetElement(x, ((n - i) - 1), (GetElement(x, ((n - i) - 1)) - MultiplyDouble(GetElement(tmp, (n - i)), GetElement(x, (n - i)))));
    }
    return x;
  }
}
