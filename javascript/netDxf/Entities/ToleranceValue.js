// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class ToleranceValue {
  // C# backing state is prefixed with $; public members retain their original names.
  $showDiameterSymbol = false;
  $tolerance = null;
  $materialCondition = 0;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (typeof args[0] === 'boolean') && (args[1] === null || typeof args[1] === 'string') && (typeof args[2] === 'number')) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching ToleranceValue constructor. Use ToleranceValue.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$showDiameterSymbol = false;
    this.$tolerance = "";
    this.$materialCondition = 0;
  }
  $ctor1(showDiameterSymbol, value, materialCondition) {
    this.$showDiameterSymbol = showDiameterSymbol;
    this.$tolerance = (NativeString.IsNullOrEmpty(value) ? "" : value);
    this.$materialCondition = materialCondition;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return ToleranceValue.$create0(...args);
    }
    if (signature === "bool,string,netDxf.Entities.ToleranceMaterialCondition") {
      if (!(args.length === 3 && (typeof args[0] === 'boolean') && (args[1] === null || typeof args[1] === 'string') && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return ToleranceValue.$create1(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new ToleranceValue(ConstructorTag, 0, args); }
  static $create1(...args) { return new ToleranceValue(ConstructorTag, 1, args); }
  get ShowDiameterSymbol() {
    return this.$showDiameterSymbol;
  }
  set ShowDiameterSymbol(value) {
    this.$showDiameterSymbol = value;
  }
  get Value() {
    return this.$tolerance;
  }
  set Value(value) {
    this.$tolerance = (NativeString.IsNullOrEmpty(value) ? "" : value);
  }
  get MaterialCondition() {
    return this.$materialCondition;
  }
  set MaterialCondition(value) {
    this.$materialCondition = value;
  }
  Clone() {
    return Init(ToleranceValue.$create0(), $new => { $new.ShowDiameterSymbol = this.$showDiameterSymbol; $new.Value = this.$tolerance; $new.MaterialCondition = this.$materialCondition; });
  }
}
