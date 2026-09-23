// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { BandedMatrix } from './BandedMatrix.js';
import { BasisFunction } from './BasisFunction.js';
import { BasisFunctionInput } from './BasisFunction.js';
import { FIQueryIntervals } from './IntrIntervals.js';
import { GMatrix } from './GMatrix.js';
import { Integration } from './Integration.js';
import { Vector3 } from './../Vector3.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class BSplineReduction {
  // C# backing state is prefixed with $; public members retain their original names.
  $sampleData = null;
  $degree = 0;
  $controlData = null;
  $basisFunction = null;
  $quantity = null;
  $numKnots = null;
  $knot = null;
  $mBasis = null;
  $mIndex = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (typeof args[2] === 'number')) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching BSplineReduction constructor. Use BSplineReduction.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$degree = 0;
    this.$quantity = [0, 0];
    this.$numKnots = [0, 0];
    this.$mBasis = [0, 0];
    this.$knot = GteArray(2, () => null);
    this.$mIndex = [0, 0];
  }
  $ctor1(inControls, degree, fraction) {
    this.$ctor0();
    let numSamples = GteReference(inControls).length;
    this.$sampleData = inControls;
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    let numControls = Int32(DotNetMath.Round(MultiplyDouble(fraction, numSamples)));
    if ((numControls >= numSamples))
    {
      this.$controlData = inControls;
      return;
    }
    if ((numControls < (degree + 1)))
    {
      numControls = (degree + 1);
    }
    this.$degree = degree;
    SetElement(this.$quantity, 0, numControls);
    SetElement(this.$quantity, 1, numSamples);
    for (let j = 0; (j <= 1); (j++))
    {
      SetElement(this.$numKnots, j, ((GetElement(this.$quantity, j) + this.$degree) + 1));
      SetElement(this.$knot, j, GteArray(GetElement(this.$numKnots, j), () => 0));
      let i = 0;
      for (i = 0; (i <= this.$degree); (++i))
      {
        SetElement(GetElement(this.$knot, j), i, 0);
      }
      let denom = (GetElement(this.$quantity, j) - this.$degree);
      let factor = (1 / denom);
      for (; (i < GetElement(this.$quantity, j)); (++i))
      {
        SetElement(GetElement(this.$knot, j), i, MultiplyDouble(((i - this.$degree)), factor));
      }
      for (; (i < GetElement(this.$numKnots, j)); (++i))
      {
        SetElement(GetElement(this.$knot, j), i, 1);
      }
    }
    let value = 0, tmin = 0, tmax = 0;
    let i0 = 0, i1 = 0;
    SetElement(this.$mBasis, 0, 0);
    SetElement(this.$mBasis, 1, 0);
    const integrand = (t) => {
      let value0 = GteReference(this).F(GetElement(this.$mBasis, 0), GetElement(this.$mIndex, 0), this.$degree, t);
      let value1 = GteReference(this).F(GetElement(this.$mBasis, 1), GetElement(this.$mIndex, 1), this.$degree, t);
      let result = MultiplyDouble(value0, value1);
      return result;
    };
    let A = BandedMatrix.$create0(GetElement(this.$quantity, 0), this.$degree, this.$degree);
    for (i0 = 0; (i0 < GetElement(this.$quantity, 0)); (++i0))
    {
      SetElement(this.$mIndex, 0, i0);
      tmax = GteReference(this).MaxSupport(0, i0);
      for (i1 = i0; ((i1 <= (i0 + this.$degree)) && (i1 < GetElement(this.$quantity, 0))); (++i1))
      {
        SetElement(this.$mIndex, 1, i1);
        tmin = GteReference(this).MinSupport(0, i1);
        value = Integration.Romberg(8, tmin, tmax, integrand);
        A.set_Item(i0, i1, value);
        A.set_Item(i1, i0, value);
      }
    }
    let invA = GMatrix.$create0(GetElement(this.$quantity, 0), GetElement(this.$quantity, 0));
    let invertible = GteReference(A).ComputeInverse(GteReference(GteReference(invA).Elements).Vector);
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    SetElement(this.$mBasis, 1, 1);
    let B = GMatrix.$create0(GetElement(this.$quantity, 0), GetElement(this.$quantity, 1));
    for (i0 = 0; (i0 < GetElement(this.$quantity, 0)); (++i0))
    {
      SetElement(this.$mIndex, 0, i0);
      let tmin0 = GteReference(this).MinSupport(0, i0);
      let tmax0 = GteReference(this).MaxSupport(0, i0);
      for (i1 = 0; (i1 < GetElement(this.$quantity, 1)); (++i1))
      {
        SetElement(this.$mIndex, 1, i1);
        let tmin1 = GteReference(this).MinSupport(1, i1);
        let tmax1 = GteReference(this).MaxSupport(1, i1);
        let interval0 = [tmin0, tmax0];
        let interval1 = [tmin1, tmax1];
        let result = FIQueryIntervals.$create1(interval0, interval1);
        if ((GteReference(result).NumIntersections === 2))
        {
          value = Integration.Romberg(8, GetElement(GteReference(result).Overlap, 0), GetElement(GteReference(result).Overlap, 1), integrand);
          B.set_Item(i0, i1, value);
        }
        else
        {
          B.set_Item(i0, i1, 0);
        }
      }
    }
    let prod = GMatrix.$op_Multiply4(invA, B);
    this.$controlData = GteArray(numControls, () => new Vector3());
    for (i0 = 0; (i0 < GetElement(this.$quantity, 0)); (++i0))
    {
      for (i1 = 0; (i1 < GetElement(this.$quantity, 1)); (++i1))
      {
        SetElement(this.$controlData, i0, Vector3.op_Addition(GetElement(this.$controlData, i0), Vector3.$op_Multiply0(GetElement(inControls, i1), prod.get_Item(i0, i1))));
      }
    }
    SetElement(this.$controlData, 0, Copy(GetElement(inControls, 0)));
    SetElement(this.$controlData, (GteReference(this.$controlData).length - 1), Copy(GetElement(inControls, (GteReference(inControls).length - 1))));
    let input = new BasisFunctionInput(numControls, degree);
    this.$basisFunction = new BasisFunction(input);
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BSplineReduction.$create0(...args);
    }
    if (signature === "netDxf.Vector3[],int,double") {
      if (!(args.length === 3 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BSplineReduction.$create1(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new BSplineReduction(ConstructorTag, 0, args); }
  static $create1(...args) { return new BSplineReduction(ConstructorTag, 1, args); }
  get NumSamples() {
    return GetElement(this.$quantity, 1);
  }
  get SampleData() {
    return this.$sampleData;
  }
  get Degree() {
    return this.$degree;
  }
  get NumControls() {
    return GetElement(this.$quantity, 0);
  }
  get ControlData() {
    return this.$controlData;
  }
  get BasisFunction() {
    return this.$basisFunction;
  }
  MinSupport(basis, i) {
    return GetElement(GetElement(this.$knot, basis), i);
  }
  MaxSupport(basis, i) {
    return GetElement(GetElement(this.$knot, basis), ((i + 1) + this.$degree));
  }
  F(basis, i, j, t) {
    if ((j > 0))
    {
      let result = 0;
      let denom = (GetElement(GetElement(this.$knot, basis), (i + j)) - GetElement(GetElement(this.$knot, basis), i));
      if ((denom > 0))
      {
        result += (MultiplyDouble(((t - GetElement(GetElement(this.$knot, basis), i))), GteReference(this).F(basis, i, (j - 1), t)) / denom);
      }
      denom = (GetElement(GetElement(this.$knot, basis), ((i + j) + 1)) - GetElement(GetElement(this.$knot, basis), (i + 1)));
      if ((denom > 0))
      {
        result += (MultiplyDouble(((GetElement(GetElement(this.$knot, basis), ((i + j) + 1)) - t)), GteReference(this).F(basis, (i + 1), (j - 1), t)) / denom);
      }
      return result;
    }
    if (((GetElement(GetElement(this.$knot, basis), i) <= t) && (t < GetElement(GetElement(this.$knot, basis), (i + 1)))))
    {
      return 1;
    }
    return 0;
  }
}
