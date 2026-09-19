// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { BezierCurve } from './BezierCurve.js';
import { Vector3 } from './Vector3.js';

export class BezierCurveQuadratic extends BezierCurve {
  // C# backing state is prefixed with $; public members retain their original names.
  constructor(...args) {
    if (args.length === 3 && (args[0] instanceof Vector3) && (args[1] instanceof Vector3) && (args[2] instanceof Vector3)) {
      let startPoint = Copy(args[0]);
      let controlPoint = Copy(args[1]);
      let endPoint = Copy(args[2]);
      super([Copy(startPoint), Copy(controlPoint), Copy(endPoint)], 2);
      return;
    }
    if (args.length === 1 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function')) {
      let controlPoints = args[0];
      super(controlPoints, 2);
      return;
    }
    throw new ArgumentException("No matching BezierCurveQuadratic constructor. Use BezierCurveQuadratic.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    if (signature === "System.Collections.Generic.IEnumerable\u003CnetDxf.Vector3\u003E") return new BezierCurveQuadratic(...args);
    if (signature === "netDxf.Vector3,netDxf.Vector3,netDxf.Vector3") return new BezierCurveQuadratic(...args);
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  get StartPoint() {
    return Copy(GetElement(this.$controlPoints, 0));
  }
  set StartPoint(value) {
    SetElement(this.$controlPoints, 0, Copy(value));
  }
  get ControlPoint() {
    return Copy(GetElement(this.$controlPoints, 1));
  }
  set ControlPoint(value) {
    SetElement(this.$controlPoints, 1, Copy(value));
  }
  get EndPoint() {
    return Copy(GetElement(this.$controlPoints, 2));
  }
  set EndPoint(value) {
    SetElement(this.$controlPoints, 2, Copy(value));
  }
  CalculatePoint(t) {
    if (((t < 0) || (t > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("t", t, "The parameter t must be between 0.0 and 1.0.");
    }
    let c = (1 - t);
    return Vector3.op_Addition(Vector3.op_Addition(Vector3.$op_Multiply1(MultiplyDouble(c, c), this.StartPoint), Vector3.$op_Multiply1(MultiplyDouble((2 * t), c), this.ControlPoint)), Vector3.$op_Multiply1(MultiplyDouble(t, t), this.EndPoint));
  }
  CalculateTangent(t) {
    if (((t < 0) || (t > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("t", t, "The parameter t must be between 0.0 and 1.0.");
    }
    let c = (1 - t);
    return Vector3.Normalize(Vector3.op_Addition(Vector3.op_Addition(Vector3.$op_Multiply1(c, this.StartPoint), Vector3.$op_Multiply1(((1 - (2 * t))), this.ControlPoint)), Vector3.$op_Multiply1(t, this.EndPoint)));
  }
  Split(t) {
    if (((t < 0) || (t > 1)))
    {
      throw new Errors.ArgumentOutOfRangeException("t", t, "The parameter t must be between 0.0 and 1.0.");
    }
    let p12 = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(this.ControlPoint, this.StartPoint)), t), this.StartPoint);
    let p23 = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(this.EndPoint, this.ControlPoint)), t), this.ControlPoint);
    let breakPoint = Vector3.op_Addition(Vector3.$op_Multiply0((Vector3.op_Subtraction(p23, p12)), t), p12);
    return [new BezierCurveQuadratic(this.StartPoint, p12, breakPoint), new BezierCurveQuadratic(breakPoint, p23, this.EndPoint)];
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
}
