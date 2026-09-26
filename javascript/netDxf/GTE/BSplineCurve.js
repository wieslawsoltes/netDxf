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


export class BSplineCurve extends ParametricCurve {
  // C# backing state is prefixed with $; public members retain their original names.
  $basisFunction = null;
  $controls = null;
  constructor(...args) {
    if (args.length === 2 && (args[0] instanceof BasisFunctionInput) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array)) {
      let input = Copy(args[0]);
      let controls = args[1];
      super(0, 1);
      this.$basisFunction = new BasisFunction(input);
      GteReference(this).SetTimeInterval(GteReference(this.$basisFunction).MinDomain, GteReference(this.$basisFunction).MaxDomain);
      this.$controls = GteArray(input.NumControls, () => new Vector3());
      if ((controls !== null))
      {
        GteCopyTo(controls, this.$controls, 0);
      }
      this.$isConstructed = true;
      return;
    }
    throw new ArgumentException("No matching BSplineCurve constructor. Use BSplineCurve.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    if (signature === "netDxf.GTE.BasisFunctionInput,netDxf.Vector3[]") return new BSplineCurve(...args);
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
  Evaluate(t, order, jet) {
    let imin, imax;
    let supOrder = 4;
    jet.value = GteArray(supOrder, () => new Vector3());
    if (((!this.$isConstructed) || (order >= supOrder)))
    {
      return;
    }
    GteReference(this.$basisFunction).Evaluate(t, order, GteRef(() => imin, v => { imin = v; }), GteRef(() => imax, v => { imax = v; }));
    SetElement(jet.value, 0, GteReference(this).Compute(0, imin, imax));
    if ((order >= 1))
    {
      SetElement(jet.value, 1, GteReference(this).Compute(1, imin, imax));
      if ((order >= 2))
      {
        SetElement(jet.value, 2, GteReference(this).Compute(2, imin, imax));
        if ((order === 3))
        {
          SetElement(jet.value, 3, GteReference(this).Compute(3, imin, imax));
        }
      }
    }
  }
  Compute(order, imin, imax) {
    let numControls = GteReference(this).NumControls;
    let result = Vector3.Zero;
    for (let i = imin; (i <= imax); (i++))
    {
      let tmp = GteReference(this.$basisFunction).GetValue(order, i);
      let j = ((i >= numControls) ? (i - numControls) : i);
      result = Vector3.op_Addition(result, Vector3.$op_Multiply1(tmp, GetElement(this.$controls, j)));
    }
    return Copy(result);
  }
}
