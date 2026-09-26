// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { RootsPolynomial } from './RootsPolynominal.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class Integration {
  // C# backing state is prefixed with $; public members retain their original names.
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching Integration constructor. Use Integration.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static TrapezoidRule(numSamples, a, b, integrand) {
    let h = (((b - a)) / ((numSamples - 1)));
    let result = (0.5 * ((GteInvoke(integrand, a) + GteInvoke(integrand, b))));
    for (let i = 1; (i <= (numSamples - 2)); (++i))
    {
      result += GteInvoke(integrand, (a + MultiplyDouble(i, h)));
    }
    result *= h;
    return result;
  }
  static Romberg(order, a, b, integrand) {
    let half = 0.5;
    let rom = GteArray(order, () => null);
    for (let i = 0; (i < order); (i++))
    {
      SetElement(rom, i, GteArray(2, () => 0));
    }
    let h = (b - a);
    SetElement(GetElement(rom, 0), 0, MultiplyDouble(MultiplyDouble(half, h), ((GteInvoke(integrand, a) + GteInvoke(integrand, b)))));
    for (let i0 = 2, p0 = 1; (i0 <= order); (i0++), p0 *= 2, h *= half)
    {
      let sum = 0;
      let i1 = 0;
      for (i1 = 1; (i1 <= p0); (++i1))
      {
        sum += GteInvoke(integrand, (a + MultiplyDouble(h, ((i1 - half)))));
      }
      SetElement(GetElement(rom, 0), 1, MultiplyDouble(half, ((GetElement(GetElement(rom, 0), 0) + MultiplyDouble(h, sum)))));
      for (let i2 = 1, i2m1 = 0, p2 = 4; (i2 < i0); (i2++), (i2m1++), p2 *= 4)
      {
        SetElement(GetElement(rom, i2), 1, (((MultiplyDouble(p2, GetElement(GetElement(rom, i2m1), 1)) - GetElement(GetElement(rom, i2m1), 0))) / ((p2 - 1))));
      }
      for (i1 = 0; (i1 < i0); (++i1))
      {
        SetElement(GetElement(rom, i1), 0, GetElement(GetElement(rom, i1), 1));
      }
    }
    return GetElement(GetElement(rom, (order - 1)), 0);
  }
  static ComputeQuadratureInfo(degree, roots, coefficients) {
    let zero = 0;
    let one = 1;
    let half = 0.5;
    let poly = GteArray((degree + 1), () => null);
    SetElement(poly, 0, [zero]);
    SetElement(poly, 1, [zero, one]);
    for (let nm = 2, nm1 = 1, nm2 = 0, np1 = 3; (nm <= degree); (nm++), (nm1++), (nm2++), (np1++))
    {
      let mult0 = (nm1 / nm);
      let mult1 = ((((2 * nm) - 1)) / nm);
      SetElement(poly, nm, GteArray(np1, () => 0));
      SetElement(GetElement(poly, nm), 0, MultiplyDouble((-mult0), GetElement(GetElement(poly, nm2), 0)));
      for (let i = 1, im1 = 0; (i <= nm2); (i++), (im1++))
      {
        SetElement(GetElement(poly, nm), i, (MultiplyDouble(mult1, GetElement(GetElement(poly, nm1), im1)) - MultiplyDouble(mult0, GetElement(GetElement(poly, nm2), i))));
      }
      SetElement(GetElement(poly, nm), nm1, MultiplyDouble(mult1, GetElement(GetElement(poly, nm1), nm2)));
      SetElement(GetElement(poly, nm), nm, MultiplyDouble(mult1, GetElement(GetElement(poly, nm1), nm1)));
    }
    RootsPolynomial.$Find0(degree, GetElement(poly, degree), 2048, roots);
    coefficients.value = GteArray(GteReference(roots.value).length, () => 0);
    let n = (GteReference(roots.value).length - 1);
    let subroots = GteArray(n, () => 0);
    for (let i = 0; (i < GteReference(roots.value).length); (i++))
    {
      let denominator = 1;
      for (let j = 0, k = 0; (j < GteReference(roots.value).length); (j++))
      {
        if ((j !== i))
        {
          SetElement(subroots, (k++), GetElement(roots.value, j));
          denominator *= (GetElement(roots.value, i) - GetElement(roots.value, j));
        }
      }
      let delta = [((-one) - GteLast(subroots)), (one - GteLast(subroots))];
      let weights = GteArray(n, () => null);
      SetElement(weights, 0, [MultiplyDouble((half * GetElement(delta, 0)), GetElement(delta, 0)), MultiplyDouble((half * GetElement(delta, 1)), GetElement(delta, 1))]);
      for (let k = 1; (k < n); (k++))
      {
        let dk = k;
        let mult = ((-dk) / ((dk + 2)));
        SetElement(GetElement(weights, k), 0, MultiplyDouble(MultiplyDouble(mult, GetElement(delta, 0)), GetElement(GetElement(weights, (k - 1)), 0)));
        SetElement(GetElement(weights, k), 1, MultiplyDouble(MultiplyDouble(mult, GetElement(delta, 1)), GetElement(GetElement(weights, (k - 1)), 1)));
      }
      let numElements = (1 << ((n - 1)));
      let info = GteArray(numElements, () => new Info());
      SetElement(info, 0, Info.$create0(0, [one, one]));
      for (let ipow = 1, r = 0; (ipow < numElements); ipow <<= 1, (r++))
      {
        SetElement(info, ipow, Info.$create0(1, [((-one) - GetElement(subroots, r)), ((+one) - GetElement(subroots, r))]));
        for (let m = 1, j = (ipow + 1); (m < ipow); (m++), (j++))
        {
          SetElement(info, j, Info.$create0((GetElement(info, m).NumBits + 1), [MultiplyDouble(GetElement(GetElement(info, ipow).Product, 0), GetElement(GetElement(info, m).Product, 0)), MultiplyDouble(GetElement(GetElement(info, ipow).Product, 1), GetElement(GetElement(info, m).Product, 1))]));
        }
      }
      let sum = GteArray(n, () => null);
      for (let ni = 0; (ni < n); (ni++))
      {
        SetElement(sum, ni, [zero, zero]);
      }
      for (let k = 0; (k < GteReference(info).length); (++k))
      {
        SetElement(GetElement(sum, GetElement(info, k).NumBits), 0, (GetElement(GetElement(sum, GetElement(info, k).NumBits), 0) + GetElement(GetElement(info, k).Product, 0)));
        SetElement(GetElement(sum, GetElement(info, k).NumBits), 1, (GetElement(GetElement(sum, GetElement(info, k).NumBits), 1) + GetElement(GetElement(info, k).Product, 1)));
      }
      let total = [zero, zero];
      for (let k = 0; (k < n); (++k))
      {
        SetElement(total, 0, (GetElement(total, 0) + MultiplyDouble(GetElement(GetElement(weights, ((n - 1) - k)), 0), GetElement(GetElement(sum, k), 0))));
        SetElement(total, 1, (GetElement(total, 1) + MultiplyDouble(GetElement(GetElement(weights, ((n - 1) - k)), 1), GetElement(GetElement(sum, k), 1))));
      }
      SetElement(coefficients.value, i, (((GetElement(total, 1) - GetElement(total, 0))) / denominator));
    }
  }
  static GaussianQuadrature(roots, coefficients, a, b, integrand) {
    let half = 0.5;
    let radius = (half * ((b - a)));
    let center = (half * ((b + a)));
    let result = 0;
    for (let i = 0; (i < GteReference(roots).length); (i++))
    {
      result += MultiplyDouble(GetElement(coefficients, i), GteInvoke(integrand, (MultiplyDouble(radius, GetElement(roots, i)) + center)));
    }
    result *= radius;
    return result;
  }
}

export class Info {
  // C# backing state is prefixed with $; public members retain their original names.
  NumBits = 0;
  Product = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) return;
    if (args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching Info constructor. Use Info.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(numBits, product) {
    this.NumBits = numBits;
    this.Product = product;
  }
  static CreateOverload(signature, ...args) {
    if (signature === '' && args.length === 0) return new Info();
    if (signature === "int,double[]") {
      if (!(args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Info.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new Info(ConstructorTag, 0, args); }
  [CopyValue]() {
    const value = new Info();
    value.NumBits = this.NumBits;
    value.Product = this.Product;
    return value;
  }
  $assign(value) {
    this.NumBits = value.NumBits;
    this.Product = value.Product;
    return this;
  }
}
