// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Arc } from './Arc.js';
import { Circle } from './Circle.js';
import { Dimension } from './Dimension.js';
import { DimensionBlock } from './DimensionBlock.js';
import { MathHelper } from './../MathHelper.js';
import { Matrix3 } from './../Matrix3.js';
import { DimensionStyle } from './../Tables/DimensionStyle.js';
import { DimensionStyleOverride } from './../Tables/DimensionStyleOverride.js';
import { Vector2 } from './../Vector2.js';
import { Vector3 } from './../Vector3.js';
import { DimensionValue, Cloneable, RequireReference, StringEquals, StringReplace, StringSubstring, DimensionCulture } from '../../runtime/DimensionRuntime.js';

export class RadialDimension extends Dimension {
  // C# backing state is prefixed with $; public members retain their original names.
  $center = new Vector2();
  $refPoint = new Vector2();
  constructor(...args) {
    super(4);
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] === null || args[2] instanceof DimensionStyle)) { this.$ctor6(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle)) { this.$ctor2(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Circle) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle)) { this.$ctor4(...args); return; }
    if (args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2)) { this.$ctor5(...args); return; }
    if (args.length === 2 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number')) { this.$ctor1(...args); return; }
    if (args.length === 2 && (args[0] === null || args[0] instanceof Circle) && (typeof args[1] === 'number')) { this.$ctor3(...args); return; }
    if (args.length === 0) { this.$ctor0(...args); return; }
    throw new ArgumentException('No matching dimension constructor. Use CreateOverload for an explicit signature.');
  }
  $ctor0() {
    this.$ctor6(Vector2.Zero, Vector2.UnitX, DimensionStyle.Default);
  }
  static $create0(...args) { return new RadialDimension(ConstructorTag, 0, args); }
  $ctor1(arc, rotation) {
    this.$ctor2(arc, rotation, DimensionStyle.Default);
  }
  static $create1(...args) { return new RadialDimension(ConstructorTag, 1, args); }
  $ctor2(arc, rotation, style) {
    if ((arc === null))
    {
      throw new Errors.ArgumentNullException("arc");
    }
    let ocsCenter = MathHelper.$Transform2(RequireReference(arc).Center, RequireReference(arc).Normal, 0, 1);
    this.$center = Vector2.$create1(ocsCenter.X, ocsCenter.Y);
    this.$refPoint = Vector2.Polar(this.$center, RequireReference(arc).Radius, (rotation * 0.017453292519943295));
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Normal = RequireReference(arc).Normal;
    RequireReference(this).Elevation = ocsCenter.Z;
    RequireReference(this).Update();
  }
  static $create2(...args) { return new RadialDimension(ConstructorTag, 2, args); }
  $ctor3(circle, rotation) {
    this.$ctor4(circle, rotation, DimensionStyle.Default);
  }
  static $create3(...args) { return new RadialDimension(ConstructorTag, 3, args); }
  $ctor4(circle, rotation, style) {
    if ((circle === null))
    {
      throw new Errors.ArgumentNullException("circle");
    }
    let ocsCenter = MathHelper.$Transform2(RequireReference(circle).Center, RequireReference(circle).Normal, 0, 1);
    this.$center = Vector2.$create1(ocsCenter.X, ocsCenter.Y);
    this.$refPoint = Vector2.Polar(this.$center, RequireReference(circle).Radius, (rotation * 0.017453292519943295));
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Normal = RequireReference(circle).Normal;
    RequireReference(this).Elevation = ocsCenter.Z;
    RequireReference(this).Update();
  }
  static $create4(...args) { return new RadialDimension(ConstructorTag, 4, args); }
  $ctor5(centerPoint, referencePoint) {
    this.$ctor6(centerPoint, referencePoint, DimensionStyle.Default);
  }
  static $create5(...args) { return new RadialDimension(ConstructorTag, 5, args); }
  $ctor6(centerPoint, referencePoint, style) {
    if (Vector2.$Equals0(centerPoint, referencePoint))
    {
      throw new Errors.ArgumentException("The center and the reference point cannot be the same");
    }
    this.$center = Copy(centerPoint);
    this.$refPoint = Copy(referencePoint);
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Update();
  }
  static $create6(...args) { return new RadialDimension(ConstructorTag, 6, args); }
  static CreateOverload(signature, ...args) {
    if (signature === "") { if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.'); return RadialDimension.$create0(...args); }
    if (signature === "netDxf.Entities.Arc,double") { if (!(args.length === 2 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return RadialDimension.$create1(...args); }
    if (signature === "netDxf.Entities.Arc,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return RadialDimension.$create2(...args); }
    if (signature === "netDxf.Entities.Circle,double") { if (!(args.length === 2 && (args[0] === null || args[0] instanceof Circle) && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return RadialDimension.$create3(...args); }
    if (signature === "netDxf.Entities.Circle,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Circle) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return RadialDimension.$create4(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2") { if (!(args.length === 2 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2))) throw new ArgumentException('Arguments do not match the selected constructor.'); return RadialDimension.$create5(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,netDxf.Tables.DimensionStyle") { if (!(args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] === null || args[2] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return RadialDimension.$create6(...args); }
    throw new ArgumentException('Unknown dimension constructor signature.', 'signature');
  }
  get CenterPoint() {
    return Copy(this.$center);
  }
  set CenterPoint(value) {
    this.$center = Copy(value);
  }
  get ReferencePoint() {
    return Copy(this.$refPoint);
  }
  set ReferencePoint(value) {
    this.$refPoint = Copy(value);
  }
  get Measurement() {
    return Vector2.Distance(this.$center, this.$refPoint);
  }
  SetDimensionLinePosition(point) {
    let radius = Vector2.Distance(this.$center, this.$refPoint);
    let rotation = Vector2.$Angle1(this.$center, point);
    this.$defPoint = Copy(this.$center);
    this.$refPoint = Vector2.Polar(this.$center, radius, rotation);
    if ((!RequireReference(this).TextPositionManuallySet))
    {
      let styleOverride = null;
      let textGap = RequireReference(RequireReference(this).Style).TextOffset;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(29, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        textGap = DimensionValue(RequireReference(styleOverride).Value);
      }
      let scale = RequireReference(RequireReference(this).Style).DimScaleOverall;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(34, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        scale = DimensionValue(RequireReference(styleOverride).Value);
      }
      let arrowSize = RequireReference(RequireReference(this).Style).ArrowSize;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(19, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        arrowSize = DimensionValue(RequireReference(styleOverride).Value);
      }
      let vect = Vector2.Normalize(Vector2.op_Subtraction(this.$refPoint, this.$center));
      if (Vector2.IsNaN(vect))
      {
        vect = Vector2.Zero;
      }
      let minOffset = MultiplyDouble((((2 * arrowSize) + textGap)), scale);
      this.$textRefPoint = Vector2.op_Addition(this.$refPoint, Vector2.$op_Multiply1(minOffset, vect));
    }
  }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    let newNormal = Matrix3.$op_Multiply1(transformation, RequireReference(this).Normal);
    if (Vector3.$Equals0(Vector3.Zero, newNormal))
    {
      newNormal = RequireReference(this).Normal;
    }
    let transOW = MathHelper.ArbitraryAxis(RequireReference(this).Normal);
    let transWO = MathHelper.ArbitraryAxis(newNormal).Transpose();
    let v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).CenterPoint.X, RequireReference(this).CenterPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newCenter = Vector2.$create1(v.X, v.Y);
    let newElevation = v.Z;
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).ReferencePoint.X, RequireReference(this).ReferencePoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newRefPoint = Vector2.$create1(v.X, v.Y);
    if (Vector2.$Equals0(newCenter, newRefPoint))
    {
      // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
      return;
    }
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(this.$textRefPoint.X, this.$textRefPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    this.$textRefPoint = Vector2.$create1(v.X, v.Y);
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(this.$defPoint.X, this.$defPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    this.$defPoint = Vector2.$create1(v.X, v.Y);
    RequireReference(this).CenterPoint = Copy(newCenter);
    RequireReference(this).ReferencePoint = Copy(newRefPoint);
    RequireReference(this).Elevation = newElevation;
    RequireReference(this).Normal = Copy(newNormal);
  }
  CalculateReferencePoints() {
    if (Vector2.$Equals0(this.$center, this.$refPoint))
    {
      throw new Errors.ArgumentException("The center and the reference point cannot be the same");
    }
    this.$defPoint = Copy(this.$center);
    if (RequireReference(this).TextPositionManuallySet)
    {
      this.SetDimensionLinePosition(this.$textRefPoint);
    }
    else
    {
      let styleOverride = null;
      let textGap = RequireReference(RequireReference(this).Style).TextOffset;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(29, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        textGap = DimensionValue(RequireReference(styleOverride).Value);
      }
      let scale = RequireReference(RequireReference(this).Style).DimScaleOverall;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(34, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        scale = DimensionValue(RequireReference(styleOverride).Value);
      }
      let arrowSize = RequireReference(RequireReference(this).Style).ArrowSize;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(19, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        arrowSize = DimensionValue(RequireReference(styleOverride).Value);
      }
      let vec = Vector2.Normalize(Vector2.op_Subtraction(this.$refPoint, this.$center));
      let minOffset = MultiplyDouble((((2 * arrowSize) + textGap)), scale);
      this.$textRefPoint = Vector2.op_Addition(this.$refPoint, Vector2.$op_Multiply1(minOffset, vec));
    }
  }
  BuildBlock(name) {
    return DimensionBlock.$Build7(this, name);
  }
  Clone() {
    let value;
    let entity = Init(RadialDimension.$create0(), $new => { $new.Layer = RequireReference(RequireReference(this).Layer).Clone(); $new.Linetype = RequireReference(RequireReference(this).Linetype).Clone(); $new.Color = RequireReference(this).Color.Clone(); $new.Lineweight = RequireReference(this).Lineweight; $new.Transparency = RequireReference(this).Transparency.Clone(); $new.LinetypeScale = RequireReference(this).LinetypeScale; $new.Normal = RequireReference(this).Normal; $new.IsVisible = RequireReference(this).IsVisible; $new.Style = RequireReference(RequireReference(this).Style).Clone(); $new.DefinitionPoint = RequireReference(this).DefinitionPoint; $new.TextReferencePoint = RequireReference(this).TextReferencePoint; $new.TextPositionManuallySet = RequireReference(this).TextPositionManuallySet; $new.TextRotation = RequireReference(this).TextRotation; $new.AttachmentPoint = RequireReference(this).AttachmentPoint; $new.LineSpacingStyle = RequireReference(this).LineSpacingStyle; $new.LineSpacingFactor = RequireReference(this).LineSpacingFactor; $new.UserText = RequireReference(this).UserText; $new.Elevation = RequireReference(this).Elevation; $new.CenterPoint = Copy(this.$center); $new.ReferencePoint = Copy(this.$refPoint); });
    for (const $item0 of RequireReference(RequireReference(this).StyleOverrides).Values) {
      let styleOverride = $item0;
      let copy = ((Cloneable(RequireReference(styleOverride).Value) && ((value = RequireReference(styleOverride).Value), true)) ? RequireReference(value).Clone() : RequireReference(styleOverride).Value);
      RequireReference(RequireReference(entity).StyleOverrides).Add(new DimensionStyleOverride(RequireReference(styleOverride).Type, copy));
    }
    for (const $item1 of RequireReference(RequireReference(this).XData).Values) {
      let data = $item1;
      RequireReference(RequireReference(entity).XData).Add(RequireReference(data).Clone());
    }
    RequireReference(this).CopyCommonDataTo(entity);
    return entity;
  }
}
