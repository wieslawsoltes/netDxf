// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Integration } from './Integration.js';
import { RootsBisection } from './RootBisection.js';
import { Vector3 } from './../Vector3.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class ParametricCurve {
  // C# backing state is prefixed with $; public members retain their original names.
  static get $DEFAULT_ROMBERG_ORDER() { return 8; }
  static get $DEFAULT_MAX_BISECTIONS() { return 1024; }
  static get $SUP_ORDER() { return 4; }
  $times = null;
  $segmentLength = null;
  $acumulatedLength = null;
  $rombergOrder = 0;
  $maxBisections = 0;
  $isConstructed = false;
  constructor(...args) {
    if (new.target === ParametricCurve) throw new NotSupportedException('Cannot construct an abstract class.');
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (new.target === ParametricCurve) throw new NotSupportedException('Cannot construct an abstract class.');
    if (args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array)) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number')) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching ParametricCurve constructor. Use ParametricCurve.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(tmin, tmax) {
    this.$ctor1(1, [tmin, tmax]);
  }
  $ctor1(numSegments, times) {
    this.$times = GteArray((numSegments + 1), () => 0);
    GteCopyTo(times, this.$times, 0);
    this.$segmentLength = GteArray(numSegments, () => 0);
    this.$acumulatedLength = GteArray(numSegments, () => 0);
    this.$rombergOrder = 8;
    this.$maxBisections = 1024;
    this.$isConstructed = false;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "double,double") {
      if (!(args.length === 2 && (typeof args[0] === 'number') && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return ParametricCurve.$create0(...args);
    }
    if (signature === "int,double[]") {
      if (!(args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return ParametricCurve.$create1(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new ParametricCurve(ConstructorTag, 0, args); }
  static $create1(...args) { return new ParametricCurve(ConstructorTag, 1, args); }
  get IsConstructed() {
    return this.$isConstructed;
  }
  get TMin() {
    return GetElement(this.$times, 0);
  }
  get TMax() {
    return GetElement(this.$times, (GteReference(this.$times).length - 1));
  }
  get NumSegments() {
    return GteReference(this.$segmentLength).length;
  }
  get Times() {
    return this.$times;
  }
  get RombergOrder() {
    return this.$rombergOrder;
  }
  set RombergOrder(value) {
    this.$rombergOrder = DotNetMath.Max(value, 1);
  }
  get MaxBisections() {
    return this.$maxBisections;
  }
  set MaxBisections(value) {
    this.$maxBisections = DotNetMath.Max(value, 1);
  }
  SetTimeInterval(tmin, tmax) {
    if ((GteReference(this.$times).length === 2))
    {
      SetElement(this.$times, 0, tmin);
      SetElement(this.$times, 1, tmax);
    }
  }
  GetPosition(t) {
    let position;
    GteReference(this).Evaluate(t, 0, GteRef(() => position, v => { position = v; }));
    return Copy(GetElement(position, 0));
  }
  GetTangent(t) {
    let jet;
    GteReference(this).Evaluate(t, 1, GteRef(() => jet, v => { jet = v; }));
    return Vector3.Normalize(GetElement(jet, 1));
  }
  GetSpeed(t) {
    let jet;
    GteReference(this).Evaluate(t, 1, GteRef(() => jet, v => { jet = v; }));
    return GteReference(GetElement(jet, 1)).Modulus();
  }
  GetLength(t0, t1) {
    const LowerBound = (array, first, last, val) => {
      for (let i = first; (i < last); (i++))
      {
        if ((GetElement(array, i) >= val))
        {
          return GetElement(array, i);
        }
      }
      return DotNetNaN;
    };
    const speed = (t) => {
      return GteReference(this).GetSpeed(t);
    };
    if ((DotNetMath.Abs(GetElement(this.$segmentLength, 0)) < 5E-324))
    {
      let numSegments = GteReference(this.$segmentLength).length;
      let accumulated = 0;
      for (let i = 0, ip1 = 1; (i < numSegments); (++i), (++ip1))
      {
        SetElement(this.$segmentLength, i, Integration.Romberg(this.$rombergOrder, GetElement(this.$times, i), GetElement(this.$times, ip1), speed));
        accumulated += GetElement(this.$segmentLength, i);
        SetElement(this.$acumulatedLength, i, accumulated);
      }
    }
    t0 = DotNetMath.Max(t0, GteReference(this).TMin);
    t1 = DotNetMath.Min(t1, GteReference(this).TMax);
    let iter0 = LowerBound(this.$times, 0, GteReference(this.$times).length, t0);
    let index0 = Int32(((iter0 - GetElement(this.$times, 0))));
    let iter1 = LowerBound(this.$times, 0, GteReference(this.$times).length, t1);
    let index1 = Int32(((iter1 - GetElement(this.$times, 0))));
    let length = 0;
    if ((index0 < index1))
    {
      length = 0;
      if ((t0 < iter0))
      {
        length += Integration.Romberg(this.$rombergOrder, t0, GetElement(this.$times, index0), speed);
      }
      let isup = 0;
      if ((t1 < iter1))
      {
        length += Integration.Romberg(this.$rombergOrder, GetElement(this.$times, (index1 - 1)), t1, speed);
        isup = (index1 - 1);
      }
      else
      {
        isup = index1;
      }
      for (let i = index0; (i < isup); (++i))
      {
        length += GetElement(this.$segmentLength, i);
      }
    }
    else
    {
      length = Integration.Romberg(this.$rombergOrder, t0, t1, speed);
    }
    return length;
  }
  GetTotalLength() {
    let lastLength = GetElement(this.$acumulatedLength, (GteReference(this.$acumulatedLength).length - 1));
    if ((DotNetMath.Abs(lastLength) < 5E-324))
    {
      return GteReference(this).GetLength(GteReference(this).TMin, GteReference(this).TMax);
    }
    return lastLength;
  }
  GetTime(length) {
    if ((length > 0))
    {
      if ((length < GteReference(this).GetTotalLength()))
      {
        const F = (t) => {
          const speed = (z) => {
            return GteReference(this).GetSpeed(z);
          };
          return (Integration.Romberg(this.$rombergOrder, GetElement(this.$times, 0), t, speed) - length);
        };
        let ratio = (length / GteReference(this).GetTotalLength());
        let omratio = (1 - ratio);
        let tmid = (MultiplyDouble(omratio, GetElement(this.$times, 0)) + MultiplyDouble(ratio, GetElement(this.$times, (GteReference(this.$times).length - 1))));
        let fmid = F(tmid);
        if ((fmid > 0))
        {
          RootsBisection.$Find1(F, GetElement(this.$times, 0), tmid, (-1), 1, this.$maxBisections, GteRef(() => tmid, v => { tmid = v; }));
        }
        else
        if ((fmid < 0))
        {
          RootsBisection.$Find1(F, tmid, GetElement(this.$times, (GteReference(this.$times).length - 1)), (-1), 1, this.$maxBisections, GteRef(() => tmid, v => { tmid = v; }));
        }
        return tmid;
      }
      return GetElement(this.$times, (GteReference(this.$times).length - 1));
    }
    return GetElement(this.$times, 0);
  }
  SubdivideByTime(numPoints, ts) {
    let points = GteArray(numPoints, () => new Vector3());
    ts.value = GteArray(numPoints, () => 0);
    let delta = (((GetElement(this.$times, (GteReference(this.$times).length - 1)) - GetElement(this.$times, 0))) / ((numPoints - 1)));
    for (let i = 0; (i < numPoints); (++i))
    {
      let t = (GetElement(this.$times, 0) + MultiplyDouble(delta, i));
      SetElement(points, i, GteReference(this).GetPosition(t));
      SetElement(ts.value, i, t);
    }
    return points;
  }
  SubdivideByLength(numPoints, ts) {
    let points = GteArray(numPoints, () => new Vector3());
    ts.value = GteArray(numPoints, () => 0);
    let delta = (GteReference(this).GetTotalLength() / ((numPoints - 1)));
    for (let i = 0; (i < numPoints); (++i))
    {
      let length = MultiplyDouble(delta, i);
      let t = GteReference(this).GetTime(length);
      SetElement(points, i, GteReference(this).GetPosition(t));
      SetElement(ts.value, i, t);
    }
    return points;
  }
}
