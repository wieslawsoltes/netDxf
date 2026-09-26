// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class HeaderVariable {
  // C# backing state is prefixed with $; public members retain their original names.
  $name = null;
  $groupCode = 0;
  $variable = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (args[0] === null || typeof args[0] === 'string') && (Number.isInteger(args[1]) && args[1] >= -32768 && args[1] <= 32767) && (true)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching HeaderVariable constructor. Use HeaderVariable.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(name, groupCode, value) {
    if ((!NativeString.StartsWith(name, "$", 3)))
    throw new Errors.ArgumentException("Header variable names always starts with \u0027$\u0027", "name");
    this.$name = name;
    this.$groupCode = groupCode;
    this.$variable = value;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "string,short,object") {
      if (!(args.length === 3 && (args[0] === null || typeof args[0] === 'string') && (Number.isInteger(args[1]) && args[1] >= -32768 && args[1] <= 32767) && (true))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return HeaderVariable.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new HeaderVariable(ConstructorTag, 0, args); }
  get Name() {
    return this.$name;
  }
  get GroupCode() {
    return this.$groupCode;
  }
  get Value() {
    return this.$variable;
  }
  set Value(value) {
    this.$variable = value;
  }
  ToString() {
    return Format("{0}:{1}", this.$name, this.$variable);
  }
}
