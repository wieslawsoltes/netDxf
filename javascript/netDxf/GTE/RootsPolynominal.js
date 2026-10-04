// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class RootsPolynomial {
  // C# backing state is prefixed with $; public members retain their original names.
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching RootsPolynomial constructor. Use RootsPolynomial.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static SolveQuadratic(p0, p1, p2, rmMap) {
    let rat2 = 2;
    let q0 = (p0 / p2);
    let q1 = (p1 / p2);
    let q1half = (q1 / rat2);
    let c0 = (q0 - MultiplyDouble(q1half, q1half));
    let rmLocalMap = new GteSortedDictionary();
    RootsPolynomial.SolveDepressedQuadratic(c0, GteRef(() => rmLocalMap, v => { rmLocalMap = v; }));
    rmMap.value = new GteSortedDictionary();
    for (const $item0 of rmLocalMap) {
      let rm = $item0;
      let root = (rm.Key - q1half);
      GteReference(rmMap.value).Add(root, rm.Value);
    }
  }
  static SolveCubic(p0, p1, p2, p3, rmMap) {
    let rat2 = 2, rat3 = 3;
    let q0 = (p0 / p3);
    let q1 = (p1 / p3);
    let q2 = (p2 / p3);
    let q2third = (q2 / rat3);
    let c0 = (q0 - MultiplyDouble(q2third, ((q1 - MultiplyDouble((rat2 * q2third), q2third)))));
    let c1 = (q1 - MultiplyDouble(q2, q2third));
    let rmLocalMap = new GteSortedDictionary();
    RootsPolynomial.SolveDepressedCubic(c0, c1, GteRef(() => rmLocalMap, v => { rmLocalMap = v; }));
    rmMap.value = new GteSortedDictionary();
    for (const $item1 of rmLocalMap) {
      let rm = $item1;
      let root = (rm.Key - q2third);
      GteReference(rmMap.value).Add(root, rm.Value);
    }
  }
  static SolveQuartic(p0, p1, p2, p3, p4, rmMap) {
    let rat2 = 2, rat3 = 3, rat4 = 4, rat6 = 6;
    let q0 = (p0 / p4);
    let q1 = (p1 / p4);
    let q2 = (p2 / p4);
    let q3 = (p3 / p4);
    let q3fourth = (q3 / rat4);
    let q3fourthSqr = MultiplyDouble(q3fourth, q3fourth);
    let c0 = (q0 - MultiplyDouble(q3fourth, ((q1 - MultiplyDouble(q3fourth, ((q2 - (q3fourthSqr * rat3))))))));
    let c1 = (q1 - MultiplyDouble((rat2 * q3fourth), ((q2 - (rat4 * q3fourthSqr)))));
    let c2 = (q2 - (rat6 * q3fourthSqr));
    let rmLocalMap = new GteSortedDictionary();
    RootsPolynomial.SolveDepressedQuartic(c0, c1, c2, GteRef(() => rmLocalMap, v => { rmLocalMap = v; }));
    rmMap.value = new GteSortedDictionary();
    for (const $item2 of rmLocalMap) {
      let rm = $item2;
      let root = (rm.Key - q3fourth);
      GteReference(rmMap.value).Add(root, rm.Value);
    }
  }
  static GetRootInfoQuadratic(p0, p1, p2, info) {
    let rat2 = 2;
    let q0 = (p0 / p2);
    let q1 = (p1 / p2);
    let q1half = (q1 / rat2);
    let c0 = (q0 - MultiplyDouble(q1half, q1half));
    info.value = new List(2);
    RootsPolynomial.GetRootInfoDepressedQuadratic(c0, info);
  }
  static GetRootInfoCubic(p0, p1, p2, p3, info) {
    let rat2 = 2, rat3 = 3;
    let q0 = (p0 / p3);
    let q1 = (p1 / p3);
    let q2 = (p2 / p3);
    let q2third = (q2 / rat3);
    let c0 = (q0 - MultiplyDouble(q2third, ((q1 - MultiplyDouble((rat2 * q2third), q2third)))));
    let c1 = (q1 - MultiplyDouble(q2, q2third));
    info.value = new List(3);
    RootsPolynomial.GetRootInfoDepressedCubic(c0, c1, info);
  }
  static GetRootInfoQuartic(p0, p1, p2, p3, p4, info) {
    let rat2 = 2, rat3 = 3, rat4 = 4, rat6 = 6;
    let q0 = (p0 / p4);
    let q1 = (p1 / p4);
    let q2 = (p2 / p4);
    let q3 = (p3 / p4);
    let q3fourth = (q3 / rat4);
    let q3fourthSqr = MultiplyDouble(q3fourth, q3fourth);
    let c0 = (q0 - MultiplyDouble(q3fourth, ((q1 - MultiplyDouble(q3fourth, ((q2 - (q3fourthSqr * rat3))))))));
    let c1 = (q1 - MultiplyDouble((rat2 * q3fourth), ((q2 - (rat4 * q3fourthSqr)))));
    let c2 = (q2 - (rat6 * q3fourthSqr));
    info.value = new List(4);
    RootsPolynomial.GetRootInfoDepressedQuartic(c0, c1, c2, info);
  }
  static $Find0(degree, c, maxIterations, roots) {
    roots.value = GteArray(degree, () => 0);
    if (((degree >= 0) && (c !== null)))
    {
      let zero = 0;
      while (((degree >= 0) && (DotNetMath.Abs((GetElement(c, degree) - zero)) < 5E-324)))
      {
        (--degree);
      }
      if ((degree > 0))
      {
        let one = 1;
        let invLeading = (one / GetElement(c, degree));
        let maxValue = zero;
        for (let i = 0; (i < degree); (++i))
        {
          let value = DotNetMath.Abs(MultiplyDouble(GetElement(c, i), invLeading));
          if ((value > maxValue))
          {
            maxValue = value;
          }
        }
        let bound = (one + maxValue);
        return RootsPolynomial.FindRecursive(degree, c, (-bound), bound, maxIterations, roots);
      }
      if ((degree === 0))
      {
        return 0;
      }
      SetElement(roots.value, 0, zero);
      return 1;
    }
    return 0;
  }
  static $Find1(degree, c, tmin, tmax, maxIterations, root) {
    let zero = 0;
    let pmin = RootsPolynomial.Evaluate(degree, c, tmin);
    root.value = zero;
    if ((DotNetMath.Abs((pmin - zero)) < 5E-324))
    {
      root.value = tmin;
      return true;
    }
    let pmax = RootsPolynomial.Evaluate(degree, c, tmax);
    if ((DotNetMath.Abs((pmax - zero)) < 5E-324))
    {
      root.value = tmax;
      return true;
    }
    if ((MultiplyDouble(pmin, pmax) > zero))
    {
      return false;
    }
    if ((tmin >= tmax))
    {
      return false;
    }
    for (let i = 1; (i <= maxIterations); (i++))
    {
      root.value = (0.5 * ((tmin + tmax)));
      if (((DotNetMath.Abs((root.value - tmin)) < 5E-324) || (DotNetMath.Abs((root.value - tmax)) < 5E-324)))
      {
        break;
      }
      let p = RootsPolynomial.Evaluate(degree, c, root.value);
      let product = MultiplyDouble(p, pmin);
      if ((product < zero))
      {
        tmax = root.value;
        pmax = p;
      }
      else
      if ((product > zero))
      {
        tmin = root.value;
        pmin = p;
      }
      else
      {
        break;
      }
    }
    return true;
  }
  static SolveDepressedQuadratic(c0, rmMap) {
    let zero = 0;
    if ((c0 < zero))
    {
      let root1 = DotNetMath.Sqrt((-c0));
      let root0 = (-root1);
      GteReference(rmMap.value).Add(root0, 1);
      GteReference(rmMap.value).Add(root1, 1);
    }
    else
    if ((DotNetMath.Abs((c0 - zero)) < 5E-324))
    {
      GteReference(rmMap.value).Add(zero, 2);
    }
    else
    {
    }
  }
  static SolveDepressedCubic(c0, c1, rmMap) {
    let zero = 0;
    if ((DotNetMath.Abs((c0 - zero)) < 5E-324))
    {
      RootsPolynomial.SolveDepressedQuadratic(c1, rmMap);
      if (GteReference(rmMap.value).ContainsKey(zero))
      {
        rmMap.value.set_Item(zero, (rmMap.value.get_Item(zero) + 1));
      }
      else
      {
        GteReference(rmMap.value).Add(zero, 1);
      }
      return;
    }
    let oneThird = (1 / 3);
    if ((DotNetMath.Abs((c1 - zero)) < 5E-324))
    {
      let root0 = 0;
      if ((c0 > zero))
      {
        root0 = (-DotNetMath.Pow(c0, oneThird));
      }
      else
      {
        root0 = DotNetMath.Pow((-c0), oneThird);
      }
      GteReference(rmMap.value).Add(root0, 1);
      return;
    }
    let rat2 = 2, rat3 = 3, rat4 = 4, rat27 = 27, rat108 = 108;
    let delta = (-((MultiplyDouble(MultiplyDouble((rat4 * c1), c1), c1) + MultiplyDouble((rat27 * c0), c0))));
    if ((delta > zero))
    {
      let deltaDiv108 = (delta / rat108);
      let betaRe = ((-c0) / rat2);
      let betaIm = DotNetMath.Sqrt(deltaDiv108);
      let theta = DotNetMath.Atan2(betaIm, betaRe);
      let thetaDiv3 = (theta / rat3);
      let angle = thetaDiv3;
      let cs = DotNetMath.Cos(angle);
      let sn = DotNetMath.Sin(angle);
      let rhoSqr = (MultiplyDouble(betaRe, betaRe) + MultiplyDouble(betaIm, betaIm));
      let rhoPowThird = DotNetMath.Pow(rhoSqr, (1 / 6));
      let temp0 = MultiplyDouble(rhoPowThird, cs);
      let temp1 = MultiplyDouble(MultiplyDouble(rhoPowThird, sn), DotNetMath.Sqrt(3));
      let root0 = (rat2 * temp0);
      let root1 = ((-temp0) - temp1);
      let root2 = ((-temp0) + temp1);
      GteReference(rmMap.value).Add(root0, 1);
      GteReference(rmMap.value).Add(root1, 1);
      GteReference(rmMap.value).Add(root2, 1);
    }
    else
    if ((delta < zero))
    {
      let deltaDiv108 = (delta / rat108);
      let temp0 = ((-c0) / rat2);
      let temp1 = DotNetMath.Sqrt((-deltaDiv108));
      let temp2 = (temp0 - temp1);
      let temp3 = (temp0 + temp1);
      if ((temp2 >= zero))
      {
        temp2 = DotNetMath.Pow(temp2, oneThird);
      }
      else
      {
        temp2 = (-DotNetMath.Pow((-temp2), oneThird));
      }
      if ((temp3 >= zero))
      {
        temp3 = DotNetMath.Pow(temp3, oneThird);
      }
      else
      {
        temp3 = (-DotNetMath.Pow((-temp3), oneThird));
      }
      let root0 = (temp2 + temp3);
      GteReference(rmMap.value).Add(root0, 1);
    }
    else
    {
      let root0 = (((-rat3) * c0) / ((rat2 * c1)));
      let root1 = ((-rat2) * root0);
      GteReference(rmMap.value).Add(root0, 2);
      GteReference(rmMap.value).Add(root1, 1);
    }
  }
  static SolveDepressedQuartic(c0, c1, c2, rmMap) {
    let rmCubicMap;
    let zero = 0;
    if ((DotNetMath.Abs((c0 - zero)) < 5E-324))
    {
      RootsPolynomial.SolveDepressedCubic(c1, c2, rmMap);
      if (GteReference(rmMap.value).ContainsKey(zero))
      {
        rmMap.value.set_Item(zero, (rmMap.value.get_Item(zero) + 1));
      }
      else
      {
        GteReference(rmMap.value).Add(zero, 1);
      }
      return;
    }
    if ((DotNetMath.Abs((c1 - zero)) < 5E-324))
    {
      RootsPolynomial.SolveBiquadratic(c0, c2, rmMap);
      return;
    }
    let rat2 = 2, rat4 = 4, rat8 = 8, rat12 = 12, rat16 = 16;
    let rat27 = 27, rat36 = 36;
    let c0sqr = MultiplyDouble(c0, c0), c1sqr = MultiplyDouble(c1, c1), c2sqr = MultiplyDouble(c2, c2);
    let delta = (MultiplyDouble(c1sqr, ((((-rat27) * c1sqr) + MultiplyDouble((rat4 * c2), (((rat36 * c0) - c2sqr)))))) + MultiplyDouble((rat16 * c0), ((MultiplyDouble(c2sqr, ((c2sqr - (rat8 * c0)))) + (rat16 * c0sqr)))));
    let a0 = ((rat12 * c0) + c2sqr);
    let a1 = ((rat4 * c0) - c2sqr);
    if ((delta > zero))
    {
      if (((c2 < zero) && (a1 < zero)))
      {
        RootsPolynomial.SolveCubic((c1sqr - MultiplyDouble((rat4 * c0), c2)), (rat8 * c0), (rat4 * c2), (-rat8), GteRef(() => rmCubicMap, v => { rmCubicMap = v; }));
        let t = GteFirst(rmCubicMap).Key;
        let alphaSqr = ((rat2 * t) - c2);
        let alpha = DotNetMath.Sqrt(alphaSqr);
        let sgnC1 = 0;
        if ((c1 > zero))
        {
          sgnC1 = 1;
        }
        else
        {
          sgnC1 = (-1);
        }
        let arg = (MultiplyDouble(t, t) - c0);
        let beta = MultiplyDouble(sgnC1, DotNetMath.Sqrt(DotNetMath.Max(arg, 0)));
        let D0 = (alphaSqr - (rat4 * ((t + beta))));
        let sqrtD0 = DotNetMath.Sqrt(DotNetMath.Max(D0, 0));
        let D1 = (alphaSqr - (rat4 * ((t - beta))));
        let sqrtD1 = DotNetMath.Sqrt(DotNetMath.Max(D1, 0));
        let root0 = (((alpha - sqrtD0)) / rat2);
        let root1 = (((alpha + sqrtD0)) / rat2);
        let root2 = ((((-alpha) - sqrtD1)) / rat2);
        let root3 = ((((-alpha) + sqrtD1)) / rat2);
        GteReference(rmMap.value).Add(root0, 1);
        GteReference(rmMap.value).Add(root1, 1);
        GteReference(rmMap.value).Add(root2, 1);
        GteReference(rmMap.value).Add(root3, 1);
      }
      else
      {
      }
    }
    else
    if ((delta < zero))
    {
      RootsPolynomial.SolveCubic((c1sqr - MultiplyDouble((rat4 * c0), c2)), (rat8 * c0), (rat4 * c2), (-rat8), GteRef(() => rmCubicMap, v => { rmCubicMap = v; }));
      let t = GteFirst(rmCubicMap).Key;
      let alphaSqr = ((rat2 * t) - c2);
      let alpha = DotNetMath.Sqrt(DotNetMath.Max(alphaSqr, 0));
      let sgnC1 = 0;
      if ((c1 > zero))
      {
        sgnC1 = 1;
      }
      else
      {
        sgnC1 = (-1);
      }
      let arg = (MultiplyDouble(t, t) - c0);
      let beta = (MultiplyDouble(sgnC1, DotNetMath.Sqrt(DotNetMath.Max(arg, 0))));
      let root0 = 0, root1 = 0;
      if ((sgnC1 > 0))
      {
        let D1 = (alphaSqr - (rat4 * ((t - beta))));
        let sqrtD1 = DotNetMath.Sqrt(DotNetMath.Max(D1, 0));
        root0 = ((((-alpha) - sqrtD1)) / rat2);
        root1 = ((((-alpha) + sqrtD1)) / rat2);
      }
      else
      {
        let D0 = (alphaSqr - (rat4 * ((t + beta))));
        let sqrtD0 = DotNetMath.Sqrt(DotNetMath.Max(D0, 0));
        root0 = (((alpha - sqrtD0)) / rat2);
        root1 = (((alpha + sqrtD0)) / rat2);
      }
      GteReference(rmMap.value).Add(root0, 1);
      GteReference(rmMap.value).Add(root1, 1);
    }
    else
    {
      if (((a1 > zero) || (((c2 > zero) && (((DotNetMath.Abs((a1 - zero)) > 5E-324) || (DotNetMath.Abs((c1 - zero)) > 5E-324)))))))
      {
        let rat9 = 9;
        let root0 = (MultiplyDouble((-c1), a0) / (((rat9 * c1sqr) - MultiplyDouble((rat2 * c2), a1))));
        GteReference(rmMap.value).Add(root0, 2);
      }
      else
      {
        let rat3 = 3;
        if ((DotNetMath.Abs((a0 - zero)) > 5E-324))
        {
          let rat9 = 9;
          let root0 = (MultiplyDouble((-c1), a0) / (((rat9 * c1sqr) - MultiplyDouble((rat2 * c2), a1))));
          let alpha = (rat2 * root0);
          let beta = (c2 + MultiplyDouble((rat3 * root0), root0));
          let discr = (MultiplyDouble(alpha, alpha) - (rat4 * beta));
          let temp1 = DotNetMath.Sqrt(discr);
          let root1 = ((((-alpha) - temp1)) / rat2);
          let root2 = ((((-alpha) + temp1)) / rat2);
          GteReference(rmMap.value).Add(root0, 2);
          GteReference(rmMap.value).Add(root1, 1);
          GteReference(rmMap.value).Add(root2, 1);
        }
        else
        {
          let root0 = (((-rat3) * c1) / ((rat4 * c2)));
          let root1 = ((-rat3) * root0);
          GteReference(rmMap.value).Add(root0, 3);
          GteReference(rmMap.value).Add(root1, 1);
        }
      }
    }
  }
  static SolveBiquadratic(c0, c2, rmMap) {
    let zero = 0, rat2 = 2, rat256 = 256;
    let c2Half = (c2 / rat2);
    let a1 = (c0 - MultiplyDouble(c2Half, c2Half));
    let delta = MultiplyDouble(MultiplyDouble((rat256 * c0), a1), a1);
    if ((delta > zero))
    {
      if ((c2 < zero))
      {
        if ((a1 < zero))
        {
          let temp0 = DotNetMath.Sqrt((-a1));
          let temp1 = ((-c2Half) - temp0);
          let temp2 = ((-c2Half) + temp0);
          let root1 = DotNetMath.Sqrt(temp1);
          let root0 = (-root1);
          let root2 = DotNetMath.Sqrt(temp2);
          let root3 = (-root2);
          GteReference(rmMap.value).Add(root0, 1);
          GteReference(rmMap.value).Add(root1, 1);
          GteReference(rmMap.value).Add(root2, 1);
          GteReference(rmMap.value).Add(root3, 1);
        }
        else
        {
        }
      }
      else
      {
      }
    }
    else
    if ((delta < zero))
    {
      let temp0 = DotNetMath.Sqrt((-a1));
      let temp1 = ((-c2Half) + temp0);
      let root1 = DotNetMath.Sqrt(temp1);
      let root0 = (-root1);
      GteReference(rmMap.value).Add(root0, 1);
      GteReference(rmMap.value).Add(root1, 1);
    }
    else
    {
      if ((c2 < zero))
      {
        let root1 = DotNetMath.Sqrt((-c2Half));
        let root0 = (-root1);
        GteReference(rmMap.value).Add(root0, 2);
        GteReference(rmMap.value).Add(root1, 2);
      }
      else
      {
      }
    }
  }
  static GetRootInfoDepressedQuadratic(c0, info) {
    let zero = 0;
    if ((c0 < zero))
    {
      info.value.Add(1);
      info.value.Add(1);
    }
    else
    if ((DotNetMath.Abs((c0 - zero)) < 5E-324))
    {
      info.value.Add(2);
    }
    else
    {
    }
  }
  static GetRootInfoDepressedCubic(c0, c1, info) {
    let zero = 0;
    if ((DotNetMath.Abs((c0 - zero)) < 5E-324))
    {
      if ((DotNetMath.Abs((c1 - zero)) < 5E-324))
      {
        info.value.Add(3);
      }
      else
      {
        info.value.Add(1);
        RootsPolynomial.GetRootInfoDepressedQuadratic(c1, info);
      }
      return;
    }
    let rat4 = 4, rat27 = 27;
    let delta = (-((MultiplyDouble(MultiplyDouble((rat4 * c1), c1), c1) + MultiplyDouble((rat27 * c0), c0))));
    if ((delta > zero))
    {
      info.value.Add(1);
      info.value.Add(1);
      info.value.Add(1);
    }
    else
    if ((delta < zero))
    {
      info.value.Add(1);
    }
    else
    {
      info.value.Add(1);
      info.value.Add(2);
    }
  }
  static GetRootInfoDepressedQuartic(c0, c1, c2, info) {
    let zero = 0;
    if ((DotNetMath.Abs((c0 - zero)) < 5E-324))
    {
      if ((DotNetMath.Abs((c1 - zero)) < 5E-324))
      {
        if ((DotNetMath.Abs((c2 - zero)) < 5E-324))
        {
          info.value.Add(4);
        }
        else
        {
          info.value.Add(2);
          RootsPolynomial.GetRootInfoDepressedQuadratic(c2, info);
        }
      }
      else
      {
        info.value.Add(1);
        RootsPolynomial.GetRootInfoDepressedCubic(c1, c2, info);
      }
      return;
    }
    if ((DotNetMath.Abs((c1 - zero)) < 5E-324))
    {
      RootsPolynomial.GetRootInfoBiquadratic(c0, c2, info);
      return;
    }
    let rat4 = 4, rat8 = 8, rat12 = 12, rat16 = 16;
    let rat27 = 27, rat36 = 36;
    let c0sqr = MultiplyDouble(c0, c0), c1sqr = MultiplyDouble(c1, c1), c2sqr = MultiplyDouble(c2, c2);
    let delta = (MultiplyDouble(c1sqr, ((((-rat27) * c1sqr) + MultiplyDouble((rat4 * c2), (((rat36 * c0) - c2sqr)))))) + MultiplyDouble((rat16 * c0), ((MultiplyDouble(c2sqr, ((c2sqr - (rat8 * c0)))) + (rat16 * c0sqr)))));
    let a0 = ((rat12 * c0) + c2sqr);
    let a1 = ((rat4 * c0) - c2sqr);
    if ((delta > zero))
    {
      if (((c2 < zero) && (a1 < zero)))
      {
        info.value.Add(1);
        info.value.Add(1);
        info.value.Add(1);
        info.value.Add(1);
      }
      else
      {
      }
    }
    else
    if ((delta < zero))
    {
      info.value.Add(1);
      info.value.Add(1);
    }
    else
    {
      if (((a1 > zero) || (((c2 > zero) && (((DotNetMath.Abs((a1 - zero)) > 5E-324) || (DotNetMath.Abs((c1 - zero)) > 5E-324)))))))
      {
        info.value.Add(2);
      }
      else
      {
        if ((DotNetMath.Abs((a0 - zero)) > 5E-324))
        {
          info.value.Add(2);
          info.value.Add(1);
          info.value.Add(1);
        }
        else
        {
          info.value.Add(3);
          info.value.Add(1);
        }
      }
    }
  }
  static GetRootInfoBiquadratic(c0, c2, info) {
    let zero = 0, rat2 = 2, rat256 = 256;
    let c2Half = (c2 / rat2);
    let a1 = (c0 - MultiplyDouble(c2Half, c2Half));
    let delta = MultiplyDouble(MultiplyDouble((rat256 * c0), a1), a1);
    if ((delta > zero))
    {
      if ((c2 < zero))
      {
        if ((a1 < zero))
        {
          info.value.Add(1);
          info.value.Add(1);
          info.value.Add(1);
          info.value.Add(1);
        }
        else
        {
        }
      }
      else
      {
      }
    }
    else
    if ((delta < zero))
    {
      info.value.Add(1);
      info.value.Add(1);
    }
    else
    {
      if ((c2 < zero))
      {
        info.value.Add(2);
        info.value.Add(2);
      }
      else
      {
      }
    }
  }
  static FindRecursive(degree, c, tmin, tmax, maxIterations, roots) {
    let zero = 0;
    let root = zero;
    let numRoots = 0;
    if ((degree === 1))
    {
      if ((DotNetMath.Abs((GetElement(c, 1) - zero)) > 5E-324))
      {
        root = ((-GetElement(c, 0)) / GetElement(c, 1));
        numRoots = 1;
      }
      else
      if ((DotNetMath.Abs((GetElement(c, 0) - zero)) < 5E-324))
      {
        root = zero;
        numRoots = 1;
      }
      else
      {
        numRoots = 0;
      }
      if ((((numRoots > 0) && (tmin <= root)) && (root <= tmax)))
      {
        SetElement(roots.value, 0, root);
        return 1;
      }
      return 0;
    }
    let derivDegree = (degree - 1);
    let derivCoeff = GteArray((derivDegree + 1), () => 0);
    let derivRoots = GteArray(derivDegree, () => 0);
    for (let i = 0, ip1 = 1; (i <= derivDegree); (++i), (++ip1))
    {
      SetElement(derivCoeff, i, (MultiplyDouble(GetElement(c, ip1), ip1) / degree));
    }
    let numDerivRoots = RootsPolynomial.FindRecursive((degree - 1), derivCoeff, tmin, tmax, maxIterations, GteRef(() => derivRoots, v => { derivRoots = v; }));
    numRoots = 0;
    if ((numDerivRoots > 0))
    {
      if (RootsPolynomial.$Find1(degree, c, tmin, GetElement(derivRoots, 0), maxIterations, GteRef(() => root, v => { root = v; })))
      {
        SetElement(roots.value, (numRoots++), root);
      }
      for (let i = 0, ip1 = 1; (i <= (numDerivRoots - 2)); (++i), (++ip1))
      {
        if (RootsPolynomial.$Find1(degree, c, GetElement(derivRoots, i), GetElement(derivRoots, ip1), maxIterations, GteRef(() => root, v => { root = v; })))
        {
          SetElement(roots.value, (numRoots++), root);
        }
      }
      if (RootsPolynomial.$Find1(degree, c, GetElement(derivRoots, (numDerivRoots - 1)), tmax, maxIterations, GteRef(() => root, v => { root = v; })))
      {
        SetElement(roots.value, (numRoots++), root);
      }
    }
    else
    {
      if (RootsPolynomial.$Find1(degree, c, tmin, tmax, maxIterations, GteRef(() => root, v => { root = v; })))
      {
        SetElement(roots.value, (numRoots++), root);
      }
    }
    return numRoots;
  }
  static Evaluate(degree, c, t) {
    let i = degree;
    let result = GetElement(c, i);
    while (((--i) >= 0))
    {
      result = (MultiplyDouble(t, result) + GetElement(c, i));
    }
    return result;
  }
  static Find(...args) {
    if (args.length === 6 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (Number.isInteger(args[4]) && args[4] >= -2147483648 && args[4] <= 2147483647) && (args[5] != null && typeof args[5] === 'object' && 'value' in args[5])) return RootsPolynomial.$Find1(...args);
    if (args.length === 4 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647) && (args[3] != null && typeof args[3] === 'object' && 'value' in args[3])) return RootsPolynomial.$Find0(...args);
    throw new ArgumentException("No matching RootsPolynomial.Find overload. Consult native-port-manifest.json.");
  }
}
