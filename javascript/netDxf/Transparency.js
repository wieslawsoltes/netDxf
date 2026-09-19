// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class Transparency {
  // C# backing state is prefixed with $; public members retain their original names.
  $transparency = 0;
  $storedAlphaValue = null;
  $property_HasValueEdit = false;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -32768 && args[0] <= 32767) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 1 && (Number.isInteger(args[0]) && args[0] >= -32768 && args[0] <= 32767)) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching Transparency constructor. Use Transparency.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$transparency = (-1);
  }
  $ctor1(value) {
    if (((value < 0) || (value > 90)))
    {
      throw new Errors.ArgumentOutOfRangeException("value", value, "Accepted transparency values range from 0 to 90.");
    }
    this.$transparency = value;
  }
  $ctor2(effectiveValue, storedValue) {
    this.$ctor1(effectiveValue);
    this.$storedAlphaValue = storedValue;
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Transparency.$create0(...args);
    }
    if (signature === "short") {
      if (!(args.length === 1 && (Number.isInteger(args[0]) && args[0] >= -32768 && args[0] <= 32767))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Transparency.$create1(...args);
    }
    if (signature === "short,int") {
      if (!(args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -32768 && args[0] <= 32767) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return Transparency.$create2(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new Transparency(ConstructorTag, 0, args); }
  static $create1(...args) { return new Transparency(ConstructorTag, 1, args); }
  static $create2(...args) { return new Transparency(ConstructorTag, 2, args); }
  get HasValueEdit() {
    return this.$property_HasValueEdit;
  }
  set HasValueEdit(value) {
    this.$property_HasValueEdit = value;
  }
  static get ByLayer() {
    return Init(Transparency.$create0(), $new => { $new.$transparency = (-1); });
  }
  static get ByBlock() {
    return Init(Transparency.$create0(), $new => { $new.$transparency = 100; });
  }
  get IsByLayer() {
    return (this.$transparency === (-1));
  }
  get IsByBlock() {
    return (this.$transparency === 100);
  }
  get StoredAlphaValue() {
    return this.$storedAlphaValue;
  }
  get Value() {
    return this.$transparency;
  }
  set Value(value) {
    if (((value < 0) || (value > 90)))
    {
      throw new Errors.ArgumentOutOfRangeException("value", value, "Accepted transparency values range from 0 to 90.");
    }
    this.$transparency = value;
    this.$storedAlphaValue = null;
    this.HasValueEdit = true;
  }
  static ToAlphaValue(transparency) {
    if ((transparency === null))
    {
      throw new Errors.ArgumentNullException("transparency");
    }
    if ((transparency.$storedAlphaValue !== null))
    return transparency.$storedAlphaValue;
    let alpha = Byte((((255 * ((100 - transparency.Value))) / 100)));
    let bytes = (transparency.IsByBlock ? [0, 0, 0, 1] : [alpha, 0, 0, 2]);
    return Int32FromBytes(bytes, 0);
  }
  static FromAlphaValue(value) {
    let bytes = Array.from(Uint8Array.of(Byte(value), Byte((value) >> 8), Byte((value) >> 16), Byte((value) >> 24)));
    let alpha = Int16(((100 - (((GetElement(bytes, 0) / 255)) * 100))));
    let result = Transparency.FromCadIndex(alpha);
    result.$storedAlphaValue = value;
    return result;
  }
  static FromCadIndex(alpha) {
    if ((alpha === (-1)))
    {
      return Transparency.ByLayer;
    }
    if ((alpha === 100))
    {
      return Transparency.ByBlock;
    }
    if ((alpha < 0))
    {
      return Transparency.$create1(0);
    }
    if ((alpha > 90))
    {
      return Transparency.$create1(90);
    }
    return Transparency.$create1(alpha);
  }
  Clone() {
    return Init(Transparency.$create0(), $new => { $new.$transparency = this.$transparency; $new.$storedAlphaValue = this.$storedAlphaValue; $new.HasValueEdit = this.HasValueEdit; });
  }
  Equals(other) {
    if ((other === null))
    {
      return false;
    }
    return (other.$transparency === this.$transparency);
  }
  ToString() {
    if ((this.$transparency === (-1)))
    {
      return "ByLayer";
    }
    if ((this.$transparency === 100))
    {
      return "ByBlock";
    }
    return NumberText(this.$transparency, Culture.Current);
  }
}
