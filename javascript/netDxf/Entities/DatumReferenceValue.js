// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class DatumReferenceValue {
  // C# backing state is prefixed with $; public members retain their original names.
  $datum = null;
  $materialCondition = 0;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 2 && (args[0] === null || typeof args[0] === 'string') && (typeof args[1] === 'number')) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching DatumReferenceValue constructor. Use DatumReferenceValue.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$datum = "";
    this.$materialCondition = 0;
  }
  $ctor1(value, materialCondition) {
    this.$datum = value;
    this.$materialCondition = materialCondition;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return DatumReferenceValue.$create0(...args);
    }
    if (signature === "string,netDxf.Entities.ToleranceMaterialCondition") {
      if (!(args.length === 2 && (args[0] === null || typeof args[0] === 'string') && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return DatumReferenceValue.$create1(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new DatumReferenceValue(ConstructorTag, 0, args); }
  static $create1(...args) { return new DatumReferenceValue(ConstructorTag, 1, args); }
  get Value() {
    return this.$datum;
  }
  set Value(value) {
    this.$datum = value;
  }
  get MaterialCondition() {
    return this.$materialCondition;
  }
  set MaterialCondition(value) {
    this.$materialCondition = value;
  }
  Clone() {
    return Init(DatumReferenceValue.$create0(), $new => { $new.Value = this.$datum; $new.MaterialCondition = this.$materialCondition; });
  }
}
