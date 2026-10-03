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
import { UniqueKnot } from './BasisFunction.js';
import { Vector3 } from './../Vector3.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class BSplineCurveFit {
  // C# backing state is prefixed with $; public members retain their original names.
  $numSamples = 0;
  $sampleData = null;
  $degree = 0;
  $numControls = 0;
  $controlData = null;
  $basisFunction = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching BSplineCurveFit constructor. Use BSplineCurveFit.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(sampleData, degree, numControls) {
    this.$numSamples = GteReference(sampleData).length;
    this.$sampleData = sampleData;
    this.$degree = degree;
    this.$numControls = numControls;
    this.$controlData = GteArray(numControls, () => new Vector3());
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    let input = new BasisFunctionInput();
    input.NumControls = numControls;
    input.Degree = degree;
    input.Uniform = true;
    input.Periodic = false;
    input.NumUniqueKnots = ((numControls - degree) + 1);
    input.UniqueKnots = GteArray(input.NumUniqueKnots, () => new UniqueKnot());
    GetElement(input.UniqueKnots, 0).T = 0;
    GetElement(input.UniqueKnots, 0).Multiplicity = (degree + 1);
    let last = (input.NumUniqueKnots - 1);
    let factor = (1 / last);
    for (let i = 1; (i < last); (++i))
    {
      GetElement(input.UniqueKnots, i).T = MultiplyDouble(factor, i);
      GetElement(input.UniqueKnots, i).Multiplicity = 1;
    }
    GetElement(input.UniqueKnots, last).T = 1;
    GetElement(input.UniqueKnots, last).Multiplicity = (degree + 1);
    this.$basisFunction = new BasisFunction(input);
    let tMultiplier = (1 / ((this.$numSamples - 1)));
    let t = 0;
    let i0 = 0, i1 = 0, i2 = 0, imin = 0, imax = 0;
    let degp1 = (this.$degree + 1);
    let numBands = ((this.$numControls > degp1) ? degp1 : this.$degree);
    let ATAMat = BandedMatrix.$create0(this.$numControls, numBands, numBands);
    for (i0 = 0; (i0 < this.$numControls); (i0++))
    {
      for (i1 = 0; (i1 < i0); (i1++))
      {
        ATAMat.set_Item(i0, i1, ATAMat.get_Item(i1, i0));
      }
      let i1Max = (i0 + this.$degree);
      if ((i1Max >= this.$numControls))
      {
        i1Max = (this.$numControls - 1);
      }
      for (i1 = i0; (i1 <= i1Max); (i1++))
      {
        let value = 0;
        for (i2 = 0; (i2 < this.$numSamples); (i2++))
        {
          t = MultiplyDouble(tMultiplier, i2);
          GteReference(this.$basisFunction).Evaluate(t, 0, GteRef(() => imin, v => { imin = v; }), GteRef(() => imax, v => { imax = v; }));
          if (((((imin <= i0) && (i0 <= imax)) && (imin <= i1)) && (i1 <= imax)))
          {
            let b0 = GteReference(this.$basisFunction).GetValue(0, i0);
            let b1 = GteReference(this.$basisFunction).GetValue(0, i1);
            value += MultiplyDouble(b0, b1);
          }
        }
        ATAMat.set_Item(i0, i1, value);
      }
    }
    let ATMat = GteArray((this.$numControls * this.$numSamples), () => 0);
    for (i0 = 0; (i0 < this.$numControls); (i0++))
    {
      for (i1 = 0; (i1 < this.$numSamples); (i1++))
      {
        t = MultiplyDouble(tMultiplier, i1);
        GteReference(this.$basisFunction).Evaluate(t, 0, GteRef(() => imin, v => { imin = v; }), GteRef(() => imax, v => { imax = v; }));
        if (((imin <= i0) && (i0 <= imax)))
        {
          SetElement(ATMat, ((i0 * this.$numSamples) + i1), GteReference(this.$basisFunction).GetValue(0, i0));
        }
      }
    }
    let solved = GteReference(ATAMat).$SolveSystem1(GteRef(() => ATMat, v => { ATMat = v; }), this.$numSamples);
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    for (i0 = 0; (i0 < this.$numControls); (i0++))
    {
      let Q = Copy(GetElement(this.$controlData, i0));
      for (i1 = 0; (i1 < this.$numSamples); (i1++))
      {
        let P = Copy(GetElement(this.$sampleData, i1));
        let xValue = GetElement(ATMat, ((i0 * this.$numSamples) + i1));
        Q = Vector3.op_Addition(Q, Vector3.$op_Multiply1(xValue, P));
      }
      SetElement(this.$controlData, i0, Copy(Q));
    }
  }
  static CreateOverload(signature, ...args) {
    if (signature === "netDxf.Vector3[],int,int") {
      if (!(args.length === 3 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BSplineCurveFit.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new BSplineCurveFit(ConstructorTag, 0, args); }
  get NumSamples() {
    return this.$numSamples;
  }
  get SampleData() {
    return this.$sampleData;
  }
  get Degree() {
    return this.$degree;
  }
  get NumControls() {
    return this.$numControls;
  }
  get ControlData() {
    return this.$controlData;
  }
  get BasisFunction() {
    return this.$basisFunction;
  }
  Evaluate(t, order, value) {
    let imin, imax;
    GteReference(this.$basisFunction).Evaluate(t, order, GteRef(() => imin, v => { imin = v; }), GteRef(() => imax, v => { imax = v; }));
    let source = Copy(GetElement(this.$controlData, imin));
    let basisValue = GteReference(this.$basisFunction).GetValue(order, imin);
    value.value = Vector3.$op_Multiply1(basisValue, source);
    for (let i = (imin + 1); (i <= imax); (i++))
    {
      source = Copy(GetElement(this.$controlData, i));
      basisValue = GteReference(this.$basisFunction).GetValue(order, i);
      value.value = Vector3.op_Addition(value.value, Vector3.$op_Multiply1(basisValue, source));
    }
  }
  GetPosition(t) {
    let position;
    GteReference(this).Evaluate(t, 0, GteRef(() => position, v => { position = v; }));
    return Copy(position);
  }
}
