// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { ParametricCurve } from './ParametricCurve.js';
import { Vector3 } from './../Vector3.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class BezierCurve extends ParametricCurve {
  // C# backing state is prefixed with $; public members retain their original names.
  $degree = 0;
  $numControls = 0;
  $controls = null;
  $choose = null;
  constructor(...args) {
    if (args.length === 2 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) {
      let controls = args[0];
      let degree = args[1];
      super(0, 1);
      // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
      this.$degree = degree;
      this.$numControls = (degree + 1);
      this.$choose = GteArray(GteReference(this).NumControls, () => null);
      this.$controls = GteArray(4, () => null);
      SetElement(this.$controls, 0, GteArray(GteReference(this).NumControls, () => new Vector3()));
      GteCopyTo(controls, GetElement(this.$controls, 0), 0);
      SetElement(this.$controls, 1, GteArray((this.$numControls - 1), () => new Vector3()));
      for (let i = 0, ip1 = 1; (ip1 < this.$numControls); (i++), (ip1++))
      {
        SetElement(GetElement(this.$controls, 1), i, Vector3.op_Subtraction(GetElement(GetElement(this.$controls, 0), ip1), GetElement(GetElement(this.$controls, 0), i)));
      }
      SetElement(this.$controls, 2, GteArray((this.$numControls - 2), () => new Vector3()));
      for (let i = 0, ip1 = 1, ip2 = 2; (ip2 < this.$numControls); (i++), (ip1++), (ip2++))
      {
        SetElement(GetElement(this.$controls, 2), i, Vector3.op_Subtraction(GetElement(GetElement(this.$controls, 1), ip1), GetElement(GetElement(this.$controls, 1), i)));
      }
      if ((degree >= 3))
      {
        SetElement(this.$controls, 3, GteArray((this.$numControls - 3), () => new Vector3()));
        for (let i = 0, ip1 = 1, ip3 = 3; (ip3 < this.$numControls); (i++), (ip1++), (ip3++))
        {
          SetElement(GetElement(this.$controls, 3), i, Vector3.op_Subtraction(GetElement(GetElement(this.$controls, 2), ip1), GetElement(GetElement(this.$controls, 2), i)));
        }
      }
      SetElement(this.$choose, 0, [1]);
      SetElement(this.$choose, 1, [1, 1]);
      for (let i = 2; (i <= this.$degree); (i++))
      {
        SetElement(this.$choose, i, GteArray((i + 1), () => 0));
        SetElement(GetElement(this.$choose, i), 0, 1);
        SetElement(GetElement(this.$choose, i), i, 1);
        for (let j = 1; (j < i); (j++))
        {
          SetElement(GetElement(this.$choose, i), j, (GetElement(GetElement(this.$choose, (i - 1)), (j - 1)) + GetElement(GetElement(this.$choose, (i - 1)), j)));
        }
      }
      this.$isConstructed = true;
      return;
    }
    throw new ArgumentException("No matching BezierCurve constructor. Use BezierCurve.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    if (signature === "netDxf.Vector3[],int") return new BezierCurve(...args);
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  get Degree() {
    return this.$degree;
  }
  get NumControls() {
    return this.$numControls;
  }
  get Controls() {
    return GetElement(this.$controls, 0);
  }
  Evaluate(t, order, jet) {
    let supOrder = 4;
    jet.value = GteArray(supOrder, () => new Vector3());
    if (((!this.$isConstructed) || (order >= 4)))
    {
      return;
    }
    let omt = (1 - t);
    SetElement(jet.value, 0, GteReference(this).Compute(t, omt, 0));
    if ((order >= 1))
    {
      SetElement(jet.value, 1, GteReference(this).Compute(t, omt, 1));
      if ((order >= 2))
      {
        SetElement(jet.value, 2, GteReference(this).Compute(t, omt, 2));
        if ((order >= 3))
        {
          if ((this.$degree >= 3))
          {
            SetElement(jet.value, 3, GteReference(this).Compute(t, omt, 3));
          }
          else
          {
            SetElement(jet.value, 3, Vector3.Zero);
          }
        }
      }
    }
  }
  Compute(t, omt, order) {
    let result = Vector3.$op_Multiply1(omt, GetElement(GetElement(this.$controls, order), 0));
    let tpow = t;
    let isup = (this.$degree - order);
    for (let i = 1; (i < isup); (i++))
    {
      let c = MultiplyDouble(GetElement(GetElement(this.$choose, isup), i), tpow);
      result = Vector3.$op_Multiply0((Vector3.op_Addition(result, Vector3.$op_Multiply1(c, GetElement(GetElement(this.$controls, order), i)))), omt);
      tpow *= t;
    }
    result = Vector3.op_Addition(result, Vector3.$op_Multiply1(tpow, GetElement(GetElement(this.$controls, order), isup)));
    let multiplier = 1;
    for (let i = 0; (i < order); (i++))
    {
      multiplier *= (this.$degree - i);
    }
    result = Vector3.$op_Multiply0(result, multiplier);
    return Copy(result);
  }
}
