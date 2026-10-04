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


export class BSplineSurfaceFit {
  // C# backing state is prefixed with $; public members retain their original names.
  $numSamples = null;
  $sampleData = null;
  $degree = null;
  $numControls = null;
  $controlData = null;
  $basisFunctions = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 7 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647) && (Number.isInteger(args[3]) && args[3] >= -2147483648 && args[3] <= 2147483647) && (Number.isInteger(args[4]) && args[4] >= -2147483648 && args[4] <= 2147483647) && (Number.isInteger(args[5]) && args[5] >= -2147483648 && args[5] <= 2147483647) && (args[6] === null || Array.isArray(args[6]) || args[6] instanceof Float64Array)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching BSplineSurfaceFit constructor. Use BSplineSurfaceFit.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(degree0, numControls0, numSamples0, degree1, numControls1, numSamples1, sampleData) {
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    this.$sampleData = sampleData;
    this.$controlData = GteArray((numControls0 * numControls1), () => new Vector3());
    this.$degree = [degree0, degree1];
    this.$numSamples = [numSamples0, numSamples1];
    this.$numControls = [numControls0, numControls1];
    this.$basisFunctions = GteArray(2, () => null);
    let input = new BasisFunctionInput();
    let tMultiplier = GteArray(2, () => 0);
    let dim = 0;
    for (dim = 0; (dim < 2); (dim++))
    {
      input.NumControls = GetElement(this.$numControls, dim);
      input.Degree = GetElement(this.$degree, dim);
      input.Uniform = true;
      input.Periodic = false;
      input.NumUniqueKnots = ((GetElement(this.$numControls, dim) - GetElement(this.$degree, dim)) + 1);
      input.UniqueKnots = GteArray(input.NumUniqueKnots, () => new UniqueKnot());
      GetElement(input.UniqueKnots, 0).T = 0;
      GetElement(input.UniqueKnots, 0).Multiplicity = (GetElement(this.$degree, dim) + 1);
      let last = (input.NumUniqueKnots - 1);
      let factor = (1 / last);
      for (let i = 1; (i < last); (i++))
      {
        GetElement(input.UniqueKnots, i).T = MultiplyDouble(factor, i);
        GetElement(input.UniqueKnots, i).Multiplicity = 1;
      }
      GetElement(input.UniqueKnots, last).T = 1;
      GetElement(input.UniqueKnots, last).Multiplicity = (GetElement(this.$degree, dim) + 1);
      SetElement(tMultiplier, dim, (1 / ((GetElement(this.$numSamples, dim) - 1))));
      SetElement(this.$basisFunctions, dim, new BasisFunction(input));
    }
    let t = 0;
    let i0 = 0, i1 = 0, i2 = 0, imin = 0, imax = 0;
    let ATAMat = [BandedMatrix.$create0(GetElement(this.$numControls, 0), (GetElement(this.$degree, 0) + 1), (GetElement(this.$degree, 0) + 1)), BandedMatrix.$create0(GetElement(this.$numControls, 1), (GetElement(this.$degree, 1) + 1), (GetElement(this.$degree, 1) + 1))];
    for (dim = 0; (dim < 2); (dim++))
    {
      for (i0 = 0; (i0 < GetElement(this.$numControls, dim)); (i0++))
      {
        for (i1 = 0; (i1 < i0); (i1++))
        {
          GetElement(ATAMat, dim).set_Item(i0, i1, GetElement(ATAMat, dim).get_Item(i1, i0));
        }
        let i1Max = (i0 + GetElement(this.$degree, dim));
        if ((i1Max >= GetElement(this.$numControls, dim)))
        {
          i1Max = (GetElement(this.$numControls, dim) - 1);
        }
        for (i1 = i0; (i1 <= i1Max); (i1++))
        {
          let value = 0;
          for (i2 = 0; (i2 < GetElement(this.$numSamples, dim)); (i2++))
          {
            t = MultiplyDouble(GetElement(tMultiplier, dim), i2);
            GteReference(GetElement(this.$basisFunctions, dim)).Evaluate(t, 0, GteRef(() => imin, v => { imin = v; }), GteRef(() => imax, v => { imax = v; }));
            if (((((imin <= i0) && (i0 <= imax)) && (imin <= i1)) && (i1 <= imax)))
            {
              let b0 = GteReference(GetElement(this.$basisFunctions, dim)).GetValue(0, i0);
              let b1 = GteReference(GetElement(this.$basisFunctions, dim)).GetValue(0, i1);
              value += MultiplyDouble(b0, b1);
            }
          }
          GetElement(ATAMat, dim).set_Item(i0, i1, value);
        }
      }
    }
    let ATMat = GteArray(2, () => null);
    for (dim = 0; (dim < 2); (dim++))
    {
      SetElement(ATMat, dim, GteArray((GetElement(this.$numControls, dim) * GetElement(this.$numSamples, dim)), () => 0));
      for (i0 = 0; (i0 < GetElement(this.$numControls, dim)); (i0++))
      {
        for (i1 = 0; (i1 < GetElement(this.$numSamples, dim)); (i1++))
        {
          t = MultiplyDouble(GetElement(tMultiplier, dim), i1);
          GteReference(GetElement(this.$basisFunctions, dim)).Evaluate(t, 0, GteRef(() => imin, v => { imin = v; }), GteRef(() => imax, v => { imax = v; }));
          if (((imin <= i0) && (i0 <= imax)))
          {
            SetElement(GetElement(ATMat, dim), ((i0 * GetElement(this.$numSamples, dim)) + i1), GteReference(GetElement(this.$basisFunctions, dim)).GetValue(0, i0));
          }
        }
      }
    }
    for (dim = 0; (dim < 2); (dim++))
    {
      let solved = GteReference(GetElement(ATAMat, dim)).$SolveSystem1(GteElementRef(ATMat, [dim], false), GetElement(this.$numSamples, dim));
      // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    }
    for (i1 = 0; (i1 < GetElement(this.$numControls, 1)); (i1++))
    {
      for (i0 = 0; (i0 < GetElement(this.$numControls, 0)); (i0++))
      {
        let sum = Vector3.Zero;
        for (let j1 = 0; (j1 < GetElement(this.$numSamples, 1)); (j1++))
        {
          let x1Value = GetElement(GetElement(ATMat, 1), ((i1 * GetElement(this.$numSamples, 1)) + j1));
          for (let j0 = 0; (j0 < GetElement(this.$numSamples, 0)); (j0++))
          {
            let x0Value = GetElement(GetElement(ATMat, 0), ((i0 * GetElement(this.$numSamples, 0)) + j0));
            let sample = Copy(GetElement(this.$sampleData, (j0 + (GetElement(this.$numSamples, 0) * j1))));
            sum = Vector3.op_Addition(sum, Vector3.$op_Multiply1(MultiplyDouble(x0Value, x1Value), sample));
          }
        }
        SetElement(this.$controlData, (i0 + (GetElement(this.$numControls, 0) * i1)), Copy(sum));
      }
    }
  }
  static CreateOverload(signature, ...args) {
    if (signature === "int,int,int,int,int,int,netDxf.Vector3[]") {
      if (!(args.length === 7 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647) && (Number.isInteger(args[3]) && args[3] >= -2147483648 && args[3] <= 2147483647) && (Number.isInteger(args[4]) && args[4] >= -2147483648 && args[4] <= 2147483647) && (Number.isInteger(args[5]) && args[5] >= -2147483648 && args[5] <= 2147483647) && (args[6] === null || Array.isArray(args[6]) || args[6] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BSplineSurfaceFit.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new BSplineSurfaceFit(ConstructorTag, 0, args); }
  NumSamples(dimension) {
    return GetElement(this.$numSamples, dimension);
  }
  get SampleData() {
    return this.$sampleData;
  }
  Degree(dimension) {
    return GetElement(this.$degree, dimension);
  }
  NumControls(dimension) {
    return GetElement(this.$numControls, dimension);
  }
  get ControlData() {
    return this.$controlData;
  }
  BasisFunction(dimension) {
    return GetElement(this.$basisFunctions, dimension);
  }
  GetPosition(u, v) {
    let iumin, iumax, ivmin, ivmax;
    GteReference(GetElement(this.$basisFunctions, 0)).Evaluate(u, 0, GteRef(() => iumin, v => { iumin = v; }), GteRef(() => iumax, v => { iumax = v; }));
    GteReference(GetElement(this.$basisFunctions, 1)).Evaluate(v, 0, GteRef(() => ivmin, v => { ivmin = v; }), GteRef(() => ivmax, v => { ivmax = v; }));
    let position = Vector3.Zero;
    for (let iv = ivmin; (iv <= ivmax); (iv++))
    {
      let value1 = GteReference(GetElement(this.$basisFunctions, 1)).GetValue(0, iv);
      for (let iu = iumin; (iu <= iumax); (iu++))
      {
        let value0 = GteReference(GetElement(this.$basisFunctions, 0)).GetValue(0, iu);
        let control = Copy(GetElement(this.$controlData, (iu + (GetElement(this.$numControls, 0) * iv))));
        position = Vector3.op_Addition(position, Vector3.$op_Multiply1(MultiplyDouble(value0, value1), control));
      }
    }
    return Copy(position);
  }
}
