// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Vector2 } from './Vector2.js';

export class ClippingBoundary {
  // C# backing state is prefixed with $; public members retain their original names.
  $type = 0;
  $vertexes = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number')) {
      this.$ctor0(...args);
      return;
    }
    if (args.length === 1 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function')) {
      this.$ctor2(...args);
      return;
    }
    throw new ArgumentException("No matching ClippingBoundary constructor. Use ClippingBoundary.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(x, y, width, height) {
    this.$type = 1;
    this.$vertexes = new List([Vector2.$create1(x, y), Vector2.$create1((x + width), (y + height))]);
  }
  $ctor1(firstCorner, secondCorner) {
    this.$type = 1;
    this.$vertexes = new List([Copy(firstCorner), Copy(secondCorner)]);
  }
  $ctor2(vertexes) {
    this.$type = 2;
    this.$vertexes = new List(vertexes);
    if ((this.$vertexes.length < 3))
    {
      throw new Errors.ArgumentOutOfRangeException("vertexes", this.$vertexes.length, "The number of vertexes for the polygonal clipping boundary must be equal or greater than three.");
    }
  }
  static CreateOverload(signature, ...args) {
    if (signature === "double,double,double,double") {
      if (!(args.length === 4 && (typeof args[0] === 'number') && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return ClippingBoundary.$create0(...args);
    }
    if (signature === "netDxf.Vector2,netDxf.Vector2") {
      if (!(args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return ClippingBoundary.$create1(...args);
    }
    if (signature === "System.Collections.Generic.IEnumerable\u003CnetDxf.Vector2\u003E") {
      if (!(args.length === 1 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return ClippingBoundary.$create2(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new ClippingBoundary(ConstructorTag, 0, args); }
  static $create1(...args) { return new ClippingBoundary(ConstructorTag, 1, args); }
  static $create2(...args) { return new ClippingBoundary(ConstructorTag, 2, args); }
  get Type() {
    return this.$type;
  }
  get Vertexes() {
    return this.$vertexes;
  }
  Clone() {
    return ((this.$type === 1) ? ClippingBoundary.$create1(GetElement(this.$vertexes, 0), GetElement(this.$vertexes, 1)) : ClippingBoundary.$create2(this.$vertexes));
  }
}
