// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { BasisFunction } from './BasisFunction.js';
import { BasisFunctionInput } from './BasisFunction.js';
import { ParametricCurve } from './ParametricCurve.js';
import { Vector3 } from './../Vector3.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class NURBSCurve extends ParametricCurve {
  // C# backing state is prefixed with $; public members retain their original names.
  $basisFunction = null;
  $controls = null;
  $weights = null;
  constructor(...args) {
    if (args.length === 3 && (args[0] instanceof BasisFunctionInput) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array) && (args[2] === null || Array.isArray(args[2]) || args[2] instanceof Float64Array)) {
      let input = Copy(args[0]);
      let controls = args[1];
      let weights = args[2];
      super(0, 1);
      this.$basisFunction = new BasisFunction(input);
      GteReference(this).SetTimeInterval(GteReference(this.$basisFunction).MinDomain, GteReference(this.$basisFunction).MaxDomain);
      this.$controls = GteArray(input.NumControls, () => new Vector3());
      if ((controls !== null))
      {
        GteCopyTo(controls, this.$controls, 0);
      }
      this.$weights = GteArray(input.NumControls, () => 0);
      if ((weights !== null))
      {
        GteCopyTo(weights, this.$weights, 0);
      }
      this.$isConstructed = true;
      return;
    }
    throw new ArgumentException("No matching NURBSCurve constructor. Use NURBSCurve.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    if (signature === "netDxf.GTE.BasisFunctionInput,netDxf.Vector3[],double[]") return new NURBSCurve(...args);
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  get BasisFunction() {
    return this.$basisFunction;
  }
  get NumControls() {
    return GteReference(this.$controls).length;
  }
  get Controls() {
    return this.$controls;
  }
  get Weights() {
    return this.$weights;
  }
  SetControl(i, control) {
    if (((0 <= i) && (i < GteReference(this).NumControls)))
    {
      SetElement(this.$controls, i, Copy(control));
    }
  }
  GetControl(i) {
    if (((0 <= i) && (i < GteReference(this).NumControls)))
    {
      return Copy(GetElement(this.$controls, i));
    }
    return Copy(GetElement(this.$controls, 0));
  }
  SetWeight(i, weight) {
    if (((0 <= i) && (i < GteReference(this).NumControls)))
    {
      SetElement(this.$weights, i, weight);
    }
  }
  GetWeight(i) {
    if (((0 <= i) && (i < GteReference(this).NumControls)))
    {
      return GetElement(this.$weights, i);
    }
    return GetElement(this.$weights, 0);
  }
  Evaluate(t, order, jet) {
    let imin, imax, X, w, xDer1, wDer1, xDer2, wDer2, xDer3, wDer3;
    let supOrder = 4;
    jet.value = GteArray(supOrder, () => new Vector3());
    if (((!this.$isConstructed) || (order >= supOrder)))
    {
      return;
    }
    GteReference(this.$basisFunction).Evaluate(t, order, GteRef(() => imin, v => { imin = v; }), GteRef(() => imax, v => { imax = v; }));
    GteReference(this).Compute(0, imin, imax, GteRef(() => X, v => { X = v; }), GteRef(() => w, v => { w = v; }));
    let invW = (1 / w);
    SetElement(jet.value, 0, Vector3.$op_Multiply1(invW, X));
    if ((order >= 1))
    {
      GteReference(this).Compute(1, imin, imax, GteRef(() => xDer1, v => { xDer1 = v; }), GteRef(() => wDer1, v => { wDer1 = v; }));
      SetElement(jet.value, 1, Vector3.$op_Multiply1(invW, (Vector3.op_Subtraction(xDer1, Vector3.$op_Multiply1(wDer1, GetElement(jet.value, 0))))));
      if ((order >= 2))
      {
        GteReference(this).Compute(2, imin, imax, GteRef(() => xDer2, v => { xDer2 = v; }), GteRef(() => wDer2, v => { wDer2 = v; }));
        SetElement(jet.value, 2, Vector3.$op_Multiply1(invW, (Vector3.op_Subtraction(Vector3.op_Subtraction(xDer2, Vector3.$op_Multiply1((2 * wDer1), GetElement(jet.value, 1))), Vector3.$op_Multiply1(wDer2, GetElement(jet.value, 0))))));
        if ((order === 3))
        {
          GteReference(this).Compute(3, imin, imax, GteRef(() => xDer3, v => { xDer3 = v; }), GteRef(() => wDer3, v => { wDer3 = v; }));
          SetElement(jet.value, 3, Vector3.$op_Multiply1(invW, (Vector3.op_Subtraction(Vector3.op_Subtraction(Vector3.op_Subtraction(xDer3, Vector3.$op_Multiply1((3 * wDer1), GetElement(jet.value, 2))), Vector3.$op_Multiply1((3 * wDer2), GetElement(jet.value, 1))), Vector3.$op_Multiply1(wDer3, GetElement(jet.value, 0))))));
        }
      }
    }
  }
  Compute(order, imin, imax, x, w) {
    let numControls = GteReference(this).NumControls;
    x.value = Vector3.Zero;
    w.value = 0;
    for (let i = imin; (i <= imax); (++i))
    {
      let j = (((i >= numControls) ? (i - numControls) : i));
      let tmp = MultiplyDouble(GteReference(this.$basisFunction).GetValue(order, i), GetElement(this.$weights, j));
      x.value = Vector3.op_Addition(x.value, Vector3.$op_Multiply1(tmp, GetElement(this.$controls, j)));
      w.value += tmp;
    }
  }
}
