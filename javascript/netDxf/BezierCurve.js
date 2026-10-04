// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class BezierCurve {
  // C# backing state is prefixed with $; public members retain their original names.
  $controlPoints = null;
  $degree = 0;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (new.target === BezierCurve) throw new NotSupportedException('Cannot construct an abstract class.');
    if (args.length === 2 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function') && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching BezierCurve constructor. Use BezierCurve.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(controlPoints, degree) {
    if ((controlPoints === null))
    {
      throw new Errors.ArgumentNullException("controlPoints");
    }
    if ((degree < 1))
    {
      throw new Errors.ArgumentOutOfRangeException("degree", degree, "The bezier curve degree must be at least one.");
    }
    this.$controlPoints = Array.from(controlPoints, Copy);
    this.$degree = degree;
    if ((this.$degree !== (this.$controlPoints.length - 1)))
    {
      throw new Errors.ArgumentException("The bezier curve degree must be equal to the number of control points minus one.");
    }
  }
  static CreateOverload(signature, ...args) {
    if (signature === "System.Collections.Generic.IEnumerable\u003CnetDxf.Vector3\u003E,int") {
      if (!(args.length === 2 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function') && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BezierCurve.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new BezierCurve(ConstructorTag, 0, args); }
  get ControlPoints() {
    return this.$controlPoints;
  }
  get Degree() {
    return this.$degree;
  }
}
