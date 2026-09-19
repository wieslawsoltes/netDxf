// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class DxfClass {
  // C# backing state is prefixed with $; public members retain their original names.
  $applicationName = null;
  $instanceCount = null;
  $property_Name = null;
  $property_CppClassName = null;
  $property_ProxyFlags = 0;
  $property_WasProxy = false;
  $property_IsEntity = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (args[0] === null || typeof args[0] === 'string') && (args[1] === null || typeof args[1] === 'string') && (args[2] === null || typeof args[2] === 'string')) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching DxfClass constructor. Use DxfClass.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(name, cppClassName, applicationName) {
    DxfClass.ValidateString(name, "name", false);
    DxfClass.ValidateString(cppClassName, "cppClassName", false);
    this.$property_Name = name;
    this.$property_CppClassName = cppClassName;
    this.ApplicationName = applicationName;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "string,string,string") {
      if (!(args.length === 3 && (args[0] === null || typeof args[0] === 'string') && (args[1] === null || typeof args[1] === 'string') && (args[2] === null || typeof args[2] === 'string'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return DxfClass.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new DxfClass(ConstructorTag, 0, args); }
  get Name() {
    return this.$property_Name;
  }
  get CppClassName() {
    return this.$property_CppClassName;
  }
  get ApplicationName() {
    return this.$applicationName;
  }
  set ApplicationName(value) {
    DxfClass.ValidateString(value, "value", true);
    this.$applicationName = value;
  }
  get ProxyFlags() {
    return this.$property_ProxyFlags;
  }
  set ProxyFlags(value) {
    this.$property_ProxyFlags = value;
  }
  get InstanceCount() {
    return this.$instanceCount;
  }
  set InstanceCount(value) {
    if (((value !== null) && (value < 0)))
    throw new Errors.ArgumentOutOfRangeException("value", value, "A class instance count cannot be negative.");
    this.$instanceCount = value;
  }
  get WasProxy() {
    return this.$property_WasProxy;
  }
  set WasProxy(value) {
    this.$property_WasProxy = value;
  }
  get IsEntity() {
    return this.$property_IsEntity;
  }
  set IsEntity(value) {
    this.$property_IsEntity = value;
  }
  Clone() {
    return MemberwiseClone(this);
  }
  static ValidateString(value, parameter, allowEmpty) {
    if ((value === null))
    throw new Errors.ArgumentNullException(parameter);
    if (((((!allowEmpty) && NativeString.IsNullOrWhiteSpace(value))) || (NativeString.IndexOfAny(value, ["\u0000", "\r", "\n"]) >= 0)))
    throw new Errors.ArgumentException("CLASS strings must have valid content and cannot contain NUL or line terminators.", parameter);
  }
}
