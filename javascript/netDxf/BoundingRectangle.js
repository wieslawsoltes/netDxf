// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Vector2 } from './Vector2.js';

export class BoundingRectangle {
  // C# backing state is prefixed with $; public members retain their original names.
  $min = new Vector2();
  $max = new Vector2();
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) {
      this.$ctor3(...args);
      return;
    }
    if (args.length === 4 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number')) {
      this.$ctor0(...args);
      return;
    }
    if (args.length === 3 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number')) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 2 && (args[0] instanceof Vector2) && (typeof args[1] === 'number')) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 1 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function')) {
      this.$ctor4(...args);
      return;
    }
    throw new ArgumentException("No matching BoundingRectangle constructor. Use BoundingRectangle.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(center, majorAxis, minorAxis, rotation) {
    let rot = (rotation * 0.017453292519943295);
    let a = MultiplyDouble((majorAxis * 0.5), DotNetMath.Cos(rot));
    let b = MultiplyDouble((minorAxis * 0.5), DotNetMath.Sin(rot));
    let c = MultiplyDouble((majorAxis * 0.5), DotNetMath.Sin(rot));
    let d = MultiplyDouble((minorAxis * 0.5), DotNetMath.Cos(rot));
    let width = (DotNetMath.Sqrt((MultiplyDouble(a, a) + MultiplyDouble(b, b))) * 2);
    let height = (DotNetMath.Sqrt((MultiplyDouble(c, c) + MultiplyDouble(d, d))) * 2);
    this.$min = Vector2.$create1((center.X - (width * 0.5)), (center.Y - (height * 0.5)));
    this.$max = Vector2.$create1((center.X + (width * 0.5)), (center.Y + (height * 0.5)));
  }
  $ctor1(center, radius) {
    this.$min = Vector2.$create1((center.X - radius), (center.Y - radius));
    this.$max = Vector2.$create1((center.X + radius), (center.Y + radius));
  }
  $ctor2(center, width, height) {
    this.$min = Vector2.$create1((center.X - (width * 0.5)), (center.Y - (height * 0.5)));
    this.$max = Vector2.$create1((center.X + (width * 0.5)), (center.Y + (height * 0.5)));
  }
  $ctor3(min, max) {
    this.$min = Copy(min);
    this.$max = Copy(max);
  }
  $ctor4(points) {
    if ((points === null))
    {
      throw new Errors.ArgumentNullException("points");
    }
    let minX = 1.7976931348623157E+308;
    let minY = 1.7976931348623157E+308;
    let maxX = -1.7976931348623157E+308;
    let maxY = -1.7976931348623157E+308;
    let any = false;
    for (const $item0 of points) {
      let point = Copy($item0);
      any = true;
      if ((minX > point.X))
      {
        minX = point.X;
      }
      if ((minY > point.Y))
      {
        minY = point.Y;
      }
      if ((maxX < point.X))
      {
        maxX = point.X;
      }
      if ((maxY < point.Y))
      {
        maxY = point.Y;
      }
    }
    if (any)
    {
      this.$min = Vector2.$create1(minX, minY);
      this.$max = Vector2.$create1(maxX, maxY);
    }
    else
    {
      this.$min = Vector2.$create1(-1.7976931348623157E+308, -1.7976931348623157E+308);
      this.$max = Vector2.$create1(1.7976931348623157E+308, 1.7976931348623157E+308);
    }
  }
  static CreateOverload(signature, ...args) {
    if (signature === "netDxf.Vector2,double,double,double") {
      if (!(args.length === 4 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BoundingRectangle.$create0(...args);
    }
    if (signature === "netDxf.Vector2,double") {
      if (!(args.length === 2 && (args[0] instanceof Vector2) && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BoundingRectangle.$create1(...args);
    }
    if (signature === "netDxf.Vector2,double,double") {
      if (!(args.length === 3 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BoundingRectangle.$create2(...args);
    }
    if (signature === "netDxf.Vector2,netDxf.Vector2") {
      if (!(args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BoundingRectangle.$create3(...args);
    }
    if (signature === "System.Collections.Generic.IEnumerable\u003CnetDxf.Vector2\u003E") {
      if (!(args.length === 1 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function'))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BoundingRectangle.$create4(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new BoundingRectangle(ConstructorTag, 0, args); }
  static $create1(...args) { return new BoundingRectangle(ConstructorTag, 1, args); }
  static $create2(...args) { return new BoundingRectangle(ConstructorTag, 2, args); }
  static $create3(...args) { return new BoundingRectangle(ConstructorTag, 3, args); }
  static $create4(...args) { return new BoundingRectangle(ConstructorTag, 4, args); }
  get Min() {
    return Copy(this.$min);
  }
  set Min(value) {
    this.$min = Copy(value);
  }
  get Max() {
    return Copy(this.$max);
  }
  set Max(value) {
    this.$max = Copy(value);
  }
  get Center() {
    return Vector2.$op_Multiply0((Vector2.op_Addition(this.$min, this.$max)), 0.5);
  }
  get Radius() {
    return (Vector2.Distance(this.$min, this.$max) * 0.5);
  }
  get Width() {
    return (this.$max.X - this.$min.X);
  }
  get Height() {
    return (this.$max.Y - this.$min.Y);
  }
  PointInside(point) {
    return ((((point.X >= this.$min.X) && (point.X <= this.$max.X)) && (point.Y >= this.$min.Y)) && (point.Y <= this.$max.Y));
  }
  static $Union0(aabr1, aabr2) {
    if (((aabr1 === null) && (aabr2 === null)))
    {
      return null;
    }
    if ((aabr1 === null))
    {
      return aabr2;
    }
    if ((aabr2 === null))
    {
      return aabr1;
    }
    let min = new Vector2();
    let max = new Vector2();
    for (let i = 0; (i < 2); (i++))
    {
      if ((aabr1.Min.get_Item(i) <= aabr2.Min.get_Item(i)))
      {
        min.set_Item(i, aabr1.Min.get_Item(i));
      }
      else
      {
        min.set_Item(i, aabr2.Min.get_Item(i));
      }
      if ((aabr1.Max.get_Item(i) >= aabr2.Max.get_Item(i)))
      {
        max.set_Item(i, aabr1.Max.get_Item(i));
      }
      else
      {
        max.set_Item(i, aabr2.Max.get_Item(i));
      }
    }
    return BoundingRectangle.$create3(min, max);
  }
  static $Union1(rectangles) {
    if ((rectangles === null))
    {
      throw new Errors.ArgumentNullException("rectangles");
    }
    let rtnAABR = null;
    for (const $item1 of rectangles) {
      let aabr = $item1;
      rtnAABR = BoundingRectangle.$Union0(rtnAABR, aabr);
    }
    return rtnAABR;
  }
  static Union(...args) {
    if (args.length === 2 && (args[0] === null || args[0] instanceof BoundingRectangle) && (args[1] === null || args[1] instanceof BoundingRectangle)) return BoundingRectangle.$Union0(...args);
    if (args.length === 1 && (args[0] == null || typeof args[0][Symbol.iterator] === 'function')) return BoundingRectangle.$Union1(...args);
    throw new ArgumentException("No matching BoundingRectangle.Union overload. Consult native-port-manifest.json.");
  }
}
