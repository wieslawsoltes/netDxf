// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Arc } from './Arc.js';
import { Dimension } from './Dimension.js';
import { DimensionBlock } from './DimensionBlock.js';
import { Polyline2DVertex } from './Polyline2DVertex.js';
import { MathHelper } from './../MathHelper.js';
import { Matrix3 } from './../Matrix3.js';
import { DimensionStyle } from './../Tables/DimensionStyle.js';
import { DimensionStyleOverride } from './../Tables/DimensionStyleOverride.js';
import { Vector2 } from './../Vector2.js';
import { Vector3 } from './../Vector3.js';
import { DimensionValue, Cloneable, RequireReference, StringEquals, StringReplace, StringSubstring, DimensionCulture } from '../../runtime/DimensionRuntime.js';

export class ArcLengthDimension extends Dimension {
  // C# backing state is prefixed with $; public members retain their original names.
  $offset = 0;
  $center = new Vector2();
  $radius = 0;
  $startAngle = 0;
  $endAngle = 0;
  constructor(...args) {
    super(7);
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle)) { this.$ctor5(...args); return; }
    if (args.length === 6 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number') && (args[5] === null || args[5] instanceof DimensionStyle)) { this.$ctor7(...args); return; }
    if (args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number')) { this.$ctor4(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle)) { this.$ctor2(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Polyline2DVertex) && (args[1] === null || args[1] instanceof Polyline2DVertex) && (typeof args[2] === 'number')) { this.$ctor3(...args); return; }
    if (args.length === 5 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number')) { this.$ctor6(...args); return; }
    if (args.length === 2 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number')) { this.$ctor1(...args); return; }
    if (args.length === 0) { this.$ctor0(...args); return; }
    throw new ArgumentException('No matching dimension constructor. Use CreateOverload for an explicit signature.');
  }
  $ctor0() {
    this.$ctor6(Vector2.Zero, 1, 0, 0, 0.1);
  }
  static $create0(...args) { return new ArcLengthDimension(ConstructorTag, 0, args); }
  $ctor1(arc, offset) {
    this.$ctor2(arc, offset, DimensionStyle.Default);
  }
  static $create1(...args) { return new ArcLengthDimension(ConstructorTag, 1, args); }
  $ctor2(arc, offset, style) {
    if ((arc === null))
    {
      throw new Errors.ArgumentNullException("arc");
    }
    RequireReference(this).CodeName = "ARC_DIMENSION";
    let refPoint = MathHelper.$Transform2(RequireReference(arc).Center, RequireReference(arc).Normal, 0, 1);
    this.$center = Vector2.$create1(refPoint.X, refPoint.Y);
    this.$radius = RequireReference(arc).Radius;
    this.$startAngle = RequireReference(arc).StartAngle;
    this.$endAngle = RequireReference(arc).EndAngle;
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Normal = RequireReference(arc).Normal;
    RequireReference(this).Elevation = refPoint.Z;
    RequireReference(this).Update();
  }
  static $create2(...args) { return new ArcLengthDimension(ConstructorTag, 2, args); }
  $ctor3(startPoint, endPoint, offset) {
    this.$ctor4(RequireReference(startPoint).Position, RequireReference(endPoint).Position, RequireReference(startPoint).Bulge, offset);
  }
  static $create3(...args) { return new ArcLengthDimension(ConstructorTag, 3, args); }
  $ctor4(startPoint, endPoint, bulge, offset) {
    this.$ctor5(startPoint, endPoint, bulge, offset, DimensionStyle.Default);
  }
  static $create4(...args) { return new ArcLengthDimension(ConstructorTag, 4, args); }
  $ctor5(startPoint, endPoint, bulge, offset, style) {
    RequireReference(this).CodeName = "ARC_DIMENSION";
    let arcData = MathHelper.ArcFromBulge(startPoint, endPoint, bulge);
    this.$center = arcData.Item1;
    this.$radius = arcData.Item2;
    this.$startAngle = arcData.Item3;
    this.$endAngle = arcData.Item4;
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Update();
  }
  static $create5(...args) { return new ArcLengthDimension(ConstructorTag, 5, args); }
  $ctor6(center, radius, startAngle, endAngle, offset) {
    this.$ctor7(center, radius, startAngle, endAngle, offset, DimensionStyle.Default);
  }
  static $create6(...args) { return new ArcLengthDimension(ConstructorTag, 6, args); }
  $ctor7(center, radius, startAngle, endAngle, offset, style) {
    RequireReference(this).CodeName = "ARC_DIMENSION";
    this.$center = Copy(center);
    if ((radius <= 0))
    {
      throw new Errors.ArgumentOutOfRangeException("radius", radius, "The arc radius must be greater than zero.");
    }
    this.$radius = radius;
    this.$startAngle = MathHelper.NormalizeAngle(startAngle);
    this.$endAngle = MathHelper.NormalizeAngle(endAngle);
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Update();
  }
  static $create7(...args) { return new ArcLengthDimension(ConstructorTag, 7, args); }
  static CreateOverload(signature, ...args) {
    if (signature === "") { if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.'); return ArcLengthDimension.$create0(...args); }
    if (signature === "netDxf.Entities.Arc,double") { if (!(args.length === 2 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return ArcLengthDimension.$create1(...args); }
    if (signature === "netDxf.Entities.Arc,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return ArcLengthDimension.$create2(...args); }
    if (signature === "netDxf.Entities.Polyline2DVertex,netDxf.Entities.Polyline2DVertex,double") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Polyline2DVertex) && (args[1] === null || args[1] instanceof Polyline2DVertex) && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return ArcLengthDimension.$create3(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,double") { if (!(args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return ArcLengthDimension.$create4(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return ArcLengthDimension.$create5(...args); }
    if (signature === "netDxf.Vector2,double,double,double,double") { if (!(args.length === 5 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return ArcLengthDimension.$create6(...args); }
    if (signature === "netDxf.Vector2,double,double,double,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 6 && (args[0] instanceof Vector2) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number') && (args[5] === null || args[5] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return ArcLengthDimension.$create7(...args); }
    throw new ArgumentException('Unknown dimension constructor signature.', 'signature');
  }
  get CenterPoint() {
    return Copy(this.$center);
  }
  set CenterPoint(value) {
    this.$center = Copy(value);
  }
  get Radius() {
    return this.$radius;
  }
  set Radius(value) {
    if ((value <= 0))
    {
      throw new Errors.ArgumentOutOfRangeException("value", value, "The arc radius must be greater than zero.");
    }
    this.$radius = value;
  }
  get StartAngle() {
    return this.$startAngle;
  }
  set StartAngle(value) {
    this.$startAngle = MathHelper.NormalizeAngle(value);
  }
  get EndAngle() {
    return this.$endAngle;
  }
  set EndAngle(value) {
    this.$endAngle = MathHelper.NormalizeAngle(value);
  }
  get ArcDefinitionPoint() {
    return Copy(this.$defPoint);
  }
  get Offset() {
    return this.$offset;
  }
  set Offset(value) {
    this.$offset = value;
  }
  get ArcAngle() {
    let angle = MathHelper.NormalizeAngle((this.$endAngle - this.$startAngle));
    if ((this.$offset < 0))
    {
      return (360 - angle);
    }
    return angle;
  }
  get Measurement() {
    return (MultiplyDouble(this.$radius, RequireReference(this).ArcAngle) * 0.017453292519943295);
  }
  SetDimensionLinePosition(point) {
    let newOffset = Vector2.Distance(this.$center, point);
    this.$offset = newOffset;
    let start = Vector2.Polar(this.$center, this.$radius, (this.$startAngle * 0.017453292519943295));
    let end = Vector2.Polar(this.$center, this.$radius, (this.$endAngle * 0.017453292519943295));
    let dirPoint = Vector2.op_Subtraction(point, this.$center);
    let cross1 = Vector2.CrossProduct(Vector2.op_Subtraction(start, this.$center), dirPoint);
    let cross2 = Vector2.CrossProduct(Vector2.op_Subtraction(end, this.$center), dirPoint);
    if (((!((cross1 >= 0))) || (!((cross2 < 0)))))
    {
      this.$offset *= (-1);
    }
    let angle = ((this.$offset >= 0) ? this.$startAngle : this.$endAngle);
    let midRot = (((angle + (0.5 * RequireReference(this).ArcAngle))) * 0.017453292519943295);
    let midDim = Vector2.Polar(this.$center, DotNetMath.Abs(this.$offset), midRot);
    this.$defPoint = Copy(midDim);
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
      let gap = MultiplyDouble(textGap, scale);
      this.$textRefPoint = Vector2.op_Addition(midDim, Vector2.$op_Multiply1(gap, Vector2.Normalize(Vector2.op_Subtraction(midDim, this.$center))));
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
    let newCenter = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).CenterPoint.X, RequireReference(this).CenterPoint.Y, RequireReference(this).Elevation));
    newCenter = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, newCenter), translation);
    newCenter = Matrix3.$op_Multiply1(transWO, newCenter);
    let axis = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).Radius, 0, 0));
    axis = Matrix3.$op_Multiply1(transformation, axis);
    axis = Matrix3.$op_Multiply1(transWO, axis);
    let axisPoint = Vector2.$create1(axis.X, axis.Y);
    let newRadius = axisPoint.Modulus();
    if (MathHelper.$IsZero0(newRadius))
    {
      newRadius = MathHelper.Epsilon;
    }
    let start = Vector2.Rotate(Vector2.$create1(RequireReference(this).Radius, 0), (RequireReference(this).StartAngle * 0.017453292519943295));
    let end = Vector2.Rotate(Vector2.$create1(RequireReference(this).Radius, 0), (RequireReference(this).EndAngle * 0.017453292519943295));
    let vStart = Matrix3.$op_Multiply1(transOW, Vector3.$create1(start.X, start.Y, 0));
    vStart = Matrix3.$op_Multiply1(transformation, vStart);
    vStart = Matrix3.$op_Multiply1(transWO, vStart);
    let vEnd = Matrix3.$op_Multiply1(transOW, Vector3.$create1(end.X, end.Y, 0));
    vEnd = Matrix3.$op_Multiply1(transformation, vEnd);
    vEnd = Matrix3.$op_Multiply1(transWO, vEnd);
    let startPoint = Vector2.$create1(vStart.X, vStart.Y);
    let endPoint = Vector2.$create1(vEnd.X, vEnd.Y);
    RequireReference(this).Normal = Copy(newNormal);
    RequireReference(this).CenterPoint = Vector2.$create1(newCenter.X, newCenter.Y);
    RequireReference(this).Radius = newRadius;
    RequireReference(this).Elevation = newCenter.Z;
    if ((DotNetMath.Sign(MultiplyDouble(MultiplyDouble(transformation.M11, transformation.M22), transformation.M33)) < 0))
    {
      RequireReference(this).EndAngle = (Vector2.$Angle0(startPoint) * 57.29577951308232);
      RequireReference(this).StartAngle = (Vector2.$Angle0(endPoint) * 57.29577951308232);
    }
    else
    {
      RequireReference(this).StartAngle = (Vector2.$Angle0(startPoint) * 57.29577951308232);
      RequireReference(this).EndAngle = (Vector2.$Angle0(endPoint) * 57.29577951308232);
    }
    let v = new Vector3();
    if (RequireReference(this).TextPositionManuallySet)
    {
      v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(this.$textRefPoint.X, this.$textRefPoint.Y, RequireReference(this).Elevation));
      v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
      v = Matrix3.$op_Multiply1(transWO, v);
      this.$textRefPoint = Vector2.$create1(v.X, v.Y);
    }
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(this.$defPoint.X, this.$defPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    this.$defPoint = Vector2.$create1(v.X, v.Y);
    this.SetDimensionLinePosition(this.$defPoint);
  }
  CalculateReferencePoints() {
    let styleOverride = null;
    let start = ((this.$offset >= 0) ? this.$startAngle : this.$endAngle);
    let midRot = (((start + (0.5 * RequireReference(this).ArcAngle))) * 0.017453292519943295);
    let midDim = Vector2.Polar(this.$center, DotNetMath.Abs(this.$offset), midRot);
    this.$defPoint = Copy(midDim);
    if (RequireReference(this).TextPositionManuallySet)
    {
      let moveText = RequireReference(RequireReference(this).Style).FitTextMove;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(37, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        moveText = DimensionValue(RequireReference(styleOverride).Value);
      }
      if ((moveText === 0))
      {
        this.SetDimensionLinePosition(this.$textRefPoint);
      }
    }
    else
    {
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
      let gap = MultiplyDouble(textGap, scale);
      this.$textRefPoint = Vector2.op_Addition(midDim, Vector2.$op_Multiply1(gap, Vector2.Normalize(Vector2.op_Subtraction(midDim, this.$center))));
    }
  }
  BuildBlock(name) {
    return DimensionBlock.$Build9(this, name);
  }
  Clone() {
    let value;
    let entity = Init(ArcLengthDimension.$create0(), $new => { $new.Layer = RequireReference(RequireReference(this).Layer).Clone(); $new.Linetype = RequireReference(RequireReference(this).Linetype).Clone(); $new.Color = RequireReference(this).Color.Clone(); $new.Lineweight = RequireReference(this).Lineweight; $new.Transparency = RequireReference(this).Transparency.Clone(); $new.LinetypeScale = RequireReference(this).LinetypeScale; $new.Normal = RequireReference(this).Normal; $new.IsVisible = RequireReference(this).IsVisible; $new.Style = RequireReference(RequireReference(this).Style).Clone(); $new.DefinitionPoint = RequireReference(this).DefinitionPoint; $new.TextReferencePoint = RequireReference(this).TextReferencePoint; $new.TextPositionManuallySet = RequireReference(this).TextPositionManuallySet; $new.TextRotation = RequireReference(this).TextRotation; $new.AttachmentPoint = RequireReference(this).AttachmentPoint; $new.LineSpacingStyle = RequireReference(this).LineSpacingStyle; $new.LineSpacingFactor = RequireReference(this).LineSpacingFactor; $new.UserText = RequireReference(this).UserText; $new.Elevation = RequireReference(this).Elevation; $new.CenterPoint = Copy(this.$center); $new.Radius = this.$radius; $new.StartAngle = this.$startAngle; $new.EndAngle = this.$endAngle; $new.Offset = this.$offset; });
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
