// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { MathHelper } from './../MathHelper.js';
import { Vector2 } from './../Vector2.js';

export class HatchPatternLineDefinition {
  // C# backing state is prefixed with $; public members retain their original names.
  $angle = 0;
  $origin = new Vector2();
  $delta = new Vector2();
  $dashPattern = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 0) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching HatchPatternLineDefinition constructor. Use HatchPatternLineDefinition.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0() {
    this.$angle = 0;
    this.$origin = Vector2.Zero;
    this.$delta = Vector2.Zero;
    this.$dashPattern = new List();
  }
  static CreateOverload(signature, ...args) {
    if (signature === "") {
      if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.');
      return HatchPatternLineDefinition.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new HatchPatternLineDefinition(ConstructorTag, 0, args); }
  get Angle() {
    return this.$angle;
  }
  set Angle(value) {
    this.$angle = MathHelper.NormalizeAngle(value);
  }
  get Origin() {
    return Copy(this.$origin);
  }
  set Origin(value) {
    this.$origin = Copy(value);
  }
  get Delta() {
    return Copy(this.$delta);
  }
  set Delta(value) {
    this.$delta = Copy(value);
  }
  get DashPattern() {
    return this.$dashPattern;
  }
  Clone() {
    let copy = Init(HatchPatternLineDefinition.$create0(), $new => { $new.Angle = this.$angle; $new.Origin = Copy(this.$origin); $new.Delta = Copy(this.$delta); });
    for (const $item0 of this.$dashPattern) {
      let dash = $item0;
      copy.DashPattern.Add(dash);
    }
    return copy;
  }
}
