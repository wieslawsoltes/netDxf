// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';

export const FIQueryIntervalsType = Object.freeze({"IsEmpty":0,"IsPoint":1,"IsFinite":2,"IsPositiveInfinite":3,"IsNegativeInfinite":4,"IsDynamicQuery":5});

export class TIQueryIntervals {
  // C# backing state is prefixed with $; public members retain their original names.
  $intersect = false;
  $firstTime = 0;
  $lastTime = 0;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'boolean') && (typeof args[2] === 'number') && (typeof args[3] === 'boolean')) {
      this.$ctor3(...args);
      return;
    }
    if (args.length === 5 && (typeof args[0] === 'number') && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array) && (typeof args[2] === 'number') && (args[3] === null || Array.isArray(args[3]) || args[3] instanceof Float64Array) && (typeof args[4] === 'number')) {
      this.$ctor4(...args);
      return;
    }
    if (args.length === 3 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (typeof args[1] === 'number') && (typeof args[2] === 'boolean')) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 2 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array)) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching TIQueryIntervals constructor. Use TIQueryIntervals.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$intersect = false;
    this.$firstTime = 0;
    this.$lastTime = 0;
  }
  $ctor1(interval0, interval1) {
    this.$ctor0();
    this.$intersect = ((GetElement(interval0, 0) <= GetElement(interval1, 1)) && (GetElement(interval0, 1) >= GetElement(interval1, 0)));
  }
  $ctor2(finite, a, isPositiveInfinite) {
    this.$ctor0();
    if (isPositiveInfinite)
    {
      this.$intersect = (GetElement(finite, 1) >= a);
    }
    else
    {
      this.$intersect = (GetElement(finite, 0) <= a);
    }
  }
  $ctor3(a0, isPositiveInfinite0, a1, isPositiveInfinite1) {
    this.$ctor0();
    if (isPositiveInfinite0)
    {
      if (isPositiveInfinite1)
      {
        this.$intersect = true;
      }
      else
      {
        this.$intersect = (a0 <= a1);
      }
    }
    else
    {
      if (isPositiveInfinite1)
      {
        this.$intersect = (a0 >= a1);
      }
      else
      {
        this.$intersect = true;
      }
    }
  }
  $ctor4(maxTime, interval0, speed0, interval1, speed1) {
    this.$ctor0();
    let zero = 0;
    if ((GetElement(interval0, 1) < GetElement(interval1, 0)))
    {
      let diffSpeed = (speed0 - speed1);
      if ((diffSpeed > zero))
      {
        let diffPos = (GetElement(interval1, 0) - GetElement(interval0, 1));
        this.$intersect = ((diffPos <= MultiplyDouble(maxTime, diffSpeed)));
        this.$firstTime = (diffPos / diffSpeed);
        this.$lastTime = (((GetElement(interval1, 1) - GetElement(interval0, 0))) / diffSpeed);
      }
    }
    else
    if ((GetElement(interval0, 0) > GetElement(interval1, 1)))
    {
      let diffSpeed = (speed1 - speed0);
      if ((diffSpeed > zero))
      {
        let diffPos = (GetElement(interval0, 0) - GetElement(interval1, 1));
        this.$intersect = ((diffPos <= MultiplyDouble(maxTime, diffSpeed)));
        this.$firstTime = (diffPos / diffSpeed);
        this.$lastTime = (((GetElement(interval0, 1) - GetElement(interval1, 0))) / diffSpeed);
      }
    }
    else
    {
      this.$intersect = true;
      this.$firstTime = zero;
      if ((speed1 > speed0))
      {
        this.$lastTime = (((GetElement(interval0, 1) - GetElement(interval1, 0))) / ((speed1 - speed0)));
      }
      else
      if ((speed1 < speed0))
      {
        this.$lastTime = (((GetElement(interval1, 1) - GetElement(interval0, 0))) / ((speed0 - speed1)));
      }
      else
      {
        this.$lastTime = 1.7976931348623157E+308;
      }
    }
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return TIQueryIntervals.$create0(...args);
    }
    if (signature === "double[],double[]") {
      if (!(args.length === 2 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return TIQueryIntervals.$create1(...args);
    }
    if (signature === "double[],double,bool") {
      if (!(args.length === 3 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (typeof args[1] === 'number') && (typeof args[2] === 'boolean'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return TIQueryIntervals.$create2(...args);
    }
    if (signature === "double,bool,double,bool") {
      if (!(args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'boolean') && (typeof args[2] === 'number') && (typeof args[3] === 'boolean'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return TIQueryIntervals.$create3(...args);
    }
    if (signature === "double,double[],double,double[],double") {
      if (!(args.length === 5 && (typeof args[0] === 'number') && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array) && (typeof args[2] === 'number') && (args[3] === null || Array.isArray(args[3]) || args[3] instanceof Float64Array) && (typeof args[4] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return TIQueryIntervals.$create4(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new TIQueryIntervals(ConstructorTag, 0, args); }
  static $create1(...args) { return new TIQueryIntervals(ConstructorTag, 1, args); }
  static $create2(...args) { return new TIQueryIntervals(ConstructorTag, 2, args); }
  static $create3(...args) { return new TIQueryIntervals(ConstructorTag, 3, args); }
  static $create4(...args) { return new TIQueryIntervals(ConstructorTag, 4, args); }
  get Intersect() {
    return this.$intersect;
  }
  get FirstTime() {
    return this.$firstTime;
  }
  get LastTime() {
    return this.$lastTime;
  }
}

export class FIQueryIntervals {
  // C# backing state is prefixed with $; public members retain their original names.
  $intersect = false;
  $numIntersections = 0;
  $overlap = null;
  $type = 0;
  $firstTime = 0;
  $lastTime = 0;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'boolean') && (typeof args[2] === 'number') && (typeof args[3] === 'boolean')) {
      this.$ctor3(...args);
      return;
    }
    if (args.length === 5 && (typeof args[0] === 'number') && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array) && (typeof args[2] === 'number') && (args[3] === null || Array.isArray(args[3]) || args[3] instanceof Float64Array) && (typeof args[4] === 'number')) {
      this.$ctor4(...args);
      return;
    }
    if (args.length === 3 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (typeof args[1] === 'number') && (typeof args[2] === 'boolean')) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 2 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array)) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching FIQueryIntervals constructor. Use FIQueryIntervals.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$intersect = false;
    this.$numIntersections = 0;
    this.$overlap = [0, 0];
    this.$type = 0;
    this.$firstTime = 0;
    this.$lastTime = 0;
  }
  $ctor1(interval0, interval1) {
    this.$ctor0();
    if (((GetElement(interval0, 1) < GetElement(interval1, 0)) || (GetElement(interval0, 0) > GetElement(interval1, 1))))
    {
      this.$numIntersections = 0;
      SetElement(this.$overlap, 0, 0);
      SetElement(this.$overlap, 1, 0);
      this.$type = 0;
    }
    else
    if ((GetElement(interval0, 1) > GetElement(interval1, 0)))
    {
      if ((GetElement(interval0, 0) < GetElement(interval1, 1)))
      {
        SetElement(this.$overlap, 0, (((GetElement(interval0, 0) < GetElement(interval1, 0)) ? GetElement(interval1, 0) : GetElement(interval0, 0))));
        SetElement(this.$overlap, 1, (((GetElement(interval0, 1) > GetElement(interval1, 1)) ? GetElement(interval1, 1) : GetElement(interval0, 1))));
        if ((GetElement(this.$overlap, 0) < GetElement(this.$overlap, 1)))
        {
          this.$numIntersections = 2;
          this.$type = 2;
        }
        else
        {
          this.$numIntersections = 1;
          this.$type = 1;
        }
      }
      else
      {
        this.$numIntersections = 1;
        SetElement(this.$overlap, 0, GetElement(interval0, 0));
        SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
        this.$type = 1;
      }
    }
    else
    {
      this.$numIntersections = 1;
      SetElement(this.$overlap, 0, GetElement(interval0, 1));
      SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
      this.$type = 1;
    }
    this.$intersect = (this.$numIntersections > 0);
  }
  $ctor2(finite, a, isPositiveInfinite) {
    this.$ctor0();
    if (isPositiveInfinite)
    {
      if ((GetElement(finite, 1) > a))
      {
        SetElement(this.$overlap, 0, DotNetMath.Max(GetElement(finite, 0), a));
        SetElement(this.$overlap, 1, GetElement(finite, 1));
        if ((GetElement(this.$overlap, 0) < GetElement(this.$overlap, 1)))
        {
          this.$numIntersections = 2;
          this.$type = 2;
        }
        else
        {
          this.$numIntersections = 1;
          this.$type = 1;
        }
      }
      else
      if ((DotNetMath.Abs((GetElement(finite, 1) - a)) < 5E-324))
      {
        this.$numIntersections = 1;
        SetElement(this.$overlap, 0, a);
        SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
        this.$type = 1;
      }
      else
      {
        this.$numIntersections = 0;
        SetElement(this.$overlap, 0, 0);
        SetElement(this.$overlap, 1, 0);
        this.$type = 0;
      }
    }
    else
    {
      if ((GetElement(finite, 0) < a))
      {
        SetElement(this.$overlap, 0, GetElement(finite, 0));
        SetElement(this.$overlap, 1, DotNetMath.Min(GetElement(finite, 1), a));
        if ((GetElement(this.$overlap, 0) < GetElement(this.$overlap, 1)))
        {
          this.$numIntersections = 2;
          this.$type = 2;
        }
        else
        {
          this.$numIntersections = 1;
          this.$type = 1;
        }
      }
      else
      if ((DotNetMath.Abs((GetElement(finite, 0) - a)) < 5E-324))
      {
        this.$numIntersections = 1;
        SetElement(this.$overlap, 0, a);
        SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
        this.$type = 1;
      }
      else
      {
        this.$numIntersections = 0;
        SetElement(this.$overlap, 0, 0);
        SetElement(this.$overlap, 1, 0);
        this.$type = 0;
      }
    }
    this.$intersect = ((this.$numIntersections > 0));
  }
  $ctor3(a0, isPositiveInfinite0, a1, isPositiveInfinite1) {
    this.$ctor0();
    if (isPositiveInfinite0)
    {
      if (isPositiveInfinite1)
      {
        this.$numIntersections = 1;
        SetElement(this.$overlap, 0, DotNetMath.Max(a0, a1));
        SetElement(this.$overlap, 1, 1);
        this.$type = 3;
      }
      else
      {
        if ((a0 > a1))
        {
          this.$numIntersections = 0;
          SetElement(this.$overlap, 0, 0);
          SetElement(this.$overlap, 1, 0);
          this.$type = 0;
        }
        else
        if ((a0 < a1))
        {
          this.$numIntersections = 2;
          SetElement(this.$overlap, 0, a0);
          SetElement(this.$overlap, 1, a1);
          this.$type = 2;
        }
        else
        {
          this.$numIntersections = 1;
          SetElement(this.$overlap, 0, a0);
          SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
          this.$type = 1;
        }
      }
    }
    else
    {
      if (isPositiveInfinite1)
      {
        if ((a0 < a1))
        {
          this.$numIntersections = 0;
          SetElement(this.$overlap, 0, 0);
          SetElement(this.$overlap, 1, 0);
          this.$type = 0;
        }
        else
        if ((a0 > a1))
        {
          this.$numIntersections = 2;
          SetElement(this.$overlap, 0, a1);
          SetElement(this.$overlap, 1, a0);
          this.$type = 2;
        }
        else
        {
          this.$numIntersections = 1;
          SetElement(this.$overlap, 0, a1);
          SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
          this.$type = 1;
        }
        this.$intersect = (a0 >= a1);
      }
      else
      {
        this.$numIntersections = 1;
        SetElement(this.$overlap, 0, (-1));
        SetElement(this.$overlap, 1, DotNetMath.Min(a0, a1));
        this.$type = 4;
      }
    }
    this.$intersect = ((this.$numIntersections > 0));
  }
  $ctor4(maxTime, interval0, speed0, interval1, speed1) {
    this.$ctor0();
    this.$type = 5;
    if ((GetElement(interval0, 1) < GetElement(interval1, 0)))
    {
      let diffSpeed = (speed0 - speed1);
      if ((diffSpeed > 0))
      {
        let diffPos = (GetElement(interval1, 0) - GetElement(interval0, 1));
        this.$intersect = ((diffPos <= MultiplyDouble(maxTime, diffSpeed)));
        this.$numIntersections = 1;
        this.$firstTime = (diffPos / diffSpeed);
        this.$lastTime = (((GetElement(interval1, 1) - GetElement(interval0, 0))) / diffSpeed);
        SetElement(this.$overlap, 0, (GetElement(interval0, 0) + MultiplyDouble(this.$firstTime, speed0)));
        SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
      }
    }
    else
    if ((GetElement(interval0, 0) > GetElement(interval1, 1)))
    {
      let diffSpeed = (speed1 - speed0);
      if ((diffSpeed > 0))
      {
        let diffPos = (GetElement(interval0, 0) - GetElement(interval1, 1));
        this.$intersect = ((diffPos <= MultiplyDouble(maxTime, diffSpeed)));
        this.$numIntersections = 1;
        this.$firstTime = (diffPos / diffSpeed);
        this.$lastTime = (((GetElement(interval0, 1) - GetElement(interval1, 0))) / diffSpeed);
        SetElement(this.$overlap, 0, (GetElement(interval1, 1) + MultiplyDouble(this.$firstTime, speed1)));
        SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
      }
    }
    else
    {
      this.$intersect = true;
      this.$firstTime = 0;
      if ((speed1 > speed0))
      {
        this.$lastTime = (((GetElement(interval0, 1) - GetElement(interval1, 0))) / ((speed1 - speed0)));
      }
      else
      if ((speed1 < speed0))
      {
        this.$lastTime = (((GetElement(interval1, 1) - GetElement(interval0, 0))) / ((speed0 - speed1)));
      }
      else
      {
        this.$lastTime = 1.7976931348623157E+308;
      }
      if ((GetElement(interval0, 1) > GetElement(interval1, 0)))
      {
        if ((GetElement(interval0, 0) < GetElement(interval1, 1)))
        {
          this.$numIntersections = 2;
          SetElement(this.$overlap, 0, (((GetElement(interval0, 0) < GetElement(interval1, 0)) ? GetElement(interval1, 0) : GetElement(interval0, 0))));
          SetElement(this.$overlap, 1, (((GetElement(interval0, 1) > GetElement(interval1, 1)) ? GetElement(interval1, 1) : GetElement(interval0, 1))));
        }
        else
        {
          this.$numIntersections = 1;
          SetElement(this.$overlap, 0, GetElement(interval0, 0));
          SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
        }
      }
      else
      {
        this.$numIntersections = 1;
        SetElement(this.$overlap, 0, GetElement(interval0, 1));
        SetElement(this.$overlap, 1, GetElement(this.$overlap, 0));
      }
    }
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return FIQueryIntervals.$create0(...args);
    }
    if (signature === "double[],double[]") {
      if (!(args.length === 2 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return FIQueryIntervals.$create1(...args);
    }
    if (signature === "double[],double,bool") {
      if (!(args.length === 3 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array) && (typeof args[1] === 'number') && (typeof args[2] === 'boolean'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return FIQueryIntervals.$create2(...args);
    }
    if (signature === "double,bool,double,bool") {
      if (!(args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'boolean') && (typeof args[2] === 'number') && (typeof args[3] === 'boolean'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return FIQueryIntervals.$create3(...args);
    }
    if (signature === "double,double[],double,double[],double") {
      if (!(args.length === 5 && (typeof args[0] === 'number') && (args[1] === null || Array.isArray(args[1]) || args[1] instanceof Float64Array) && (typeof args[2] === 'number') && (args[3] === null || Array.isArray(args[3]) || args[3] instanceof Float64Array) && (typeof args[4] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return FIQueryIntervals.$create4(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new FIQueryIntervals(ConstructorTag, 0, args); }
  static $create1(...args) { return new FIQueryIntervals(ConstructorTag, 1, args); }
  static $create2(...args) { return new FIQueryIntervals(ConstructorTag, 2, args); }
  static $create3(...args) { return new FIQueryIntervals(ConstructorTag, 3, args); }
  static $create4(...args) { return new FIQueryIntervals(ConstructorTag, 4, args); }
  get Intersect() {
    return this.$intersect;
  }
  get NumIntersections() {
    return this.$numIntersections;
  }
  get Overlap() {
    return this.$overlap;
  }
  get Type() {
    return this.$type;
  }
  get FirstTime() {
    return this.$firstTime;
  }
  get LastTime() {
    return this.$lastTime;
  }
}
