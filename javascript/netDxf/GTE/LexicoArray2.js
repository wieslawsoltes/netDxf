// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { GTE } from './GTE.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class LexicoArray2 {
  // C# backing state is prefixed with $; public members retain their original names.
  $numRows = 0;
  $numCols = 0;
  $matrix = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (args[2] === null || Array.isArray(args[2]) || args[2] instanceof Float64Array)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching LexicoArray2 constructor. Use LexicoArray2.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(numRows, numCols, matrix) {
    this.$numRows = numRows;
    this.$numCols = numCols;
    this.$matrix = matrix;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "int,int,double[]") {
      if (!(args.length === 3 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (args[2] === null || Array.isArray(args[2]) || args[2] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return LexicoArray2.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new LexicoArray2(ConstructorTag, 0, args); }
  get NumRows() {
    return this.$numRows;
  }
  get NumCols() {
    return this.$numCols;
  }
  get_Item(r, c) {
    return (GTE.UseRowMajor ? GetElement(this.$matrix, (c + (this.$numCols * r))) : GetElement(this.$matrix, (r + (this.$numRows * c))));
  }
  set_Item(r, c, value) {
    if (GTE.UseRowMajor)
    {
      SetElement(this.$matrix, (c + (this.$numCols * r)), value);
    }
    else
    {
      SetElement(this.$matrix, (r + (this.$numRows * c)), value);
    }
  }
  CopyTo(array, index) {
    GteCopyTo(this.$matrix, array, 0);
  }
}
