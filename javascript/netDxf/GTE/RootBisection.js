// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class RootsBisection {
  // C# backing state is prefixed with $; public members retain their original names.
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching RootsBisection constructor. Use RootsBisection.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $Find0(f, t0, t1, maxIterations, root) {
    root.value = t0;
    if ((t0 < t1))
    {
      let f0 = GteInvoke(f, t0);
      if ((DotNetMath.Abs(f0) < 5E-324))
      {
        root.value = t0;
        return 1;
      }
      let f1 = GteInvoke(f, t1);
      if ((DotNetMath.Abs(f1) < 5E-324))
      {
        root.value = t1;
        return 1;
      }
      if ((MultiplyDouble(f0, f1) > 0))
      {
        return 0;
      }
      let i = 0;
      for (i = 2; (i <= maxIterations); (++i))
      {
        root.value = (0.5 * ((t0 + t1)));
        if (((DotNetMath.Abs((root.value - t0)) < 5E-324) || (DotNetMath.Abs((root.value - t1)) < 5E-324)))
        {
          break;
        }
        let fm = GteInvoke(f, root.value);
        let product = MultiplyDouble(fm, f0);
        if ((product < 0))
        {
          t1 = root.value;
          f1 = fm;
        }
        else
        if ((product > 0))
        {
          t0 = root.value;
          f0 = fm;
        }
        else
        {
          break;
        }
      }
      return i;
    }
    return 0;
  }
  static $Find1(f, t0, t1, f0, f1, maxIterations, root) {
    root.value = t0;
    if ((t0 < t1))
    {
      if ((DotNetMath.Abs(f0) < 5E-324))
      {
        root.value = t0;
        return 1;
      }
      if ((DotNetMath.Abs(f1) < 5E-324))
      {
        root.value = t1;
        return 1;
      }
      if ((MultiplyDouble(f0, f1) > 0))
      {
        return 0;
      }
      let i = 0;
      root.value = t0;
      for (i = 2; (i <= maxIterations); (++i))
      {
        root.value = (0.5 * ((t0 + t1)));
        if (((DotNetMath.Abs((root.value - t0)) < 5E-324) || (DotNetMath.Abs((root.value - t1)) < 5E-324)))
        {
          break;
        }
        let fm = GteInvoke(f, root.value);
        let product = MultiplyDouble(fm, f0);
        if ((product < 0))
        {
          t1 = root.value;
          f1 = fm;
        }
        else
        if ((product > 0))
        {
          t0 = root.value;
          f0 = fm;
        }
        else
        {
          break;
        }
      }
      return i;
    }
    return 0;
  }
  static Find(...args) {
    if (args.length === 7 && (args[0] === null || typeof args[0] === 'function') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number') && (Number.isInteger(args[5]) && args[5] >= -2147483648 && args[5] <= 2147483647) && (args[6] != null && typeof args[6] === 'object' && 'value' in args[6])) return RootsBisection.$Find1(...args);
    if (args.length === 5 && (args[0] === null || typeof args[0] === 'function') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (Number.isInteger(args[3]) && args[3] >= -2147483648 && args[3] <= 2147483647) && (args[4] != null && typeof args[4] === 'object' && 'value' in args[4])) return RootsBisection.$Find0(...args);
    throw new ArgumentException("No matching RootsBisection.Find overload. Consult native-port-manifest.json.");
  }
}
