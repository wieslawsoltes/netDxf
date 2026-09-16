// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class MeshEdge {
  // C# backing state is prefixed with $; public members retain their original names.
  $startVertexIndex = 0;
  $endVertexIndex = 0;
  $crease = 0;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (typeof args[2] === 'number')) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching MeshEdge constructor. Use MeshEdge.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(startVertexIndex, endVertexIndex) {
    this.$ctor1(startVertexIndex, endVertexIndex, 0);
  }
  $ctor1(startVertexIndex, endVertexIndex, crease) {
    if ((startVertexIndex < 0))
    throw new Errors.ArgumentOutOfRangeException("startVertexIndex", startVertexIndex, "The vertex index must be positive.");
    this.$startVertexIndex = startVertexIndex;
    if ((endVertexIndex < 0))
    throw new Errors.ArgumentOutOfRangeException("endVertexIndex", endVertexIndex, "The vertex index must be positive.");
    this.$endVertexIndex = endVertexIndex;
    this.$crease = ((crease < 0) ? (-1) : crease);
  }
  static CreateOverload(signature, ...args) {
    if (signature === "int,int") {
      if (!(args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return MeshEdge.$create0(...args);
    }
    if (signature === "int,int,double") {
      if (!(args.length === 3 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return MeshEdge.$create1(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new MeshEdge(ConstructorTag, 0, args); }
  static $create1(...args) { return new MeshEdge(ConstructorTag, 1, args); }
  get StartVertexIndex() {
    return this.$startVertexIndex;
  }
  set StartVertexIndex(value) {
    if ((value < 0))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The vertex index must be must be equals or greater than zero.");
    this.$startVertexIndex = value;
  }
  get EndVertexIndex() {
    return this.$endVertexIndex;
  }
  set EndVertexIndex(value) {
    if ((value < 0))
    throw new Errors.ArgumentOutOfRangeException("value", value, "The vertex index must be must be equals or greater than zero.");
    this.$endVertexIndex = value;
  }
  get Crease() {
    return this.$crease;
  }
  set Crease(value) {
    this.$crease = ((value < 0) ? (-1) : value);
  }
  $ToString0() {
    return Format("{0}: ({1}{4} {2}) crease={3}", "SplineVertex", this.$startVertexIndex, this.$endVertexIndex, this.$crease, Culture.ListSeparator);
  }
  $ToString1(provider) {
    return Format("{0}: ({1}{4} {2}) crease={3}", "SplineVertex", NumberText(this.$startVertexIndex, provider), NumberText(this.$endVertexIndex, provider), NumberText(this.$crease, provider), Culture.ListSeparator);
  }
  Clone() {
    return MeshEdge.$create1(this.$startVertexIndex, this.$endVertexIndex, this.$crease);
  }
  ToString(...args) {
    if (args.length === 1 && (args[0] === null || typeof args[0] === 'object' || typeof args[0] === 'string')) return this.$ToString1(...args);
    if (args.length === 0) return this.$ToString0(...args);
    throw new ArgumentException("No matching MeshEdge.ToString overload. Consult native-port-manifest.json.");
  }
}
