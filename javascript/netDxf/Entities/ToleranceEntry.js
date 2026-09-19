// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class ToleranceEntry {
  // C# backing state is prefixed with $; public members retain their original names.
  $geometricSymbol = 0;
  $tolerance1 = null;
  $tolerance2 = null;
  $datum1 = null;
  $datum2 = null;
  $datum3 = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching ToleranceEntry constructor. Use ToleranceEntry.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$geometricSymbol = 0;
    this.$tolerance1 = null;
    this.$tolerance2 = null;
    this.$datum1 = null;
    this.$datum2 = null;
    this.$datum3 = null;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return ToleranceEntry.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new ToleranceEntry(ConstructorTag, 0, args); }
  get GeometricSymbol() {
    return this.$geometricSymbol;
  }
  set GeometricSymbol(value) {
    this.$geometricSymbol = value;
  }
  get Tolerance1() {
    return this.$tolerance1;
  }
  set Tolerance1(value) {
    this.$tolerance1 = value;
  }
  get Tolerance2() {
    return this.$tolerance2;
  }
  set Tolerance2(value) {
    this.$tolerance2 = value;
  }
  get Datum1() {
    return this.$datum1;
  }
  set Datum1(value) {
    this.$datum1 = value;
  }
  get Datum2() {
    return this.$datum2;
  }
  set Datum2(value) {
    this.$datum2 = value;
  }
  get Datum3() {
    return this.$datum3;
  }
  set Datum3(value) {
    this.$datum3 = value;
  }
  Clone() {
    return Init(ToleranceEntry.$create0(), $new => { $new.GeometricSymbol = this.$geometricSymbol; $new.Tolerance1 = (this.$tolerance1?.Clone() ?? null); $new.Tolerance2 = (this.$tolerance2?.Clone() ?? null); $new.Datum1 = (this.$datum1?.Clone() ?? null); $new.Datum2 = (this.$datum2?.Clone() ?? null); $new.Datum3 = (this.$datum3?.Clone() ?? null); });
  }
}
