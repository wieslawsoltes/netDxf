// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Dimension } from './Dimension.js';
import { DimensionBlock } from './DimensionBlock.js';
import { MathHelper } from './../MathHelper.js';
import { Matrix3 } from './../Matrix3.js';
import { DimensionStyle } from './../Tables/DimensionStyle.js';
import { DimensionStyleOverride } from './../Tables/DimensionStyleOverride.js';
import { Vector2 } from './../Vector2.js';
import { Vector3 } from './../Vector3.js';
import { DimensionValue, Cloneable, RequireReference, StringEquals, StringReplace, StringSubstring, DimensionCulture } from '../../runtime/DimensionRuntime.js';

export class OrdinateDimension extends Dimension {
  // C# backing state is prefixed with $; public members retain their original names.
  $rotation = 0;
  $axis = 0;
  $firstPoint = new Vector2();
  $secondPoint = new Vector2();
  constructor(...args) {
    super(6);
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle)) { this.$ctor3(...args); return; }
    if (args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (args[3] === null || args[3] instanceof DimensionStyle)) { this.$ctor2(...args); return; }
    if (args.length === 6 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number') && (args[5] === null || args[5] instanceof DimensionStyle)) { this.$ctor7(...args); return; }
    if (args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle)) { this.$ctor5(...args); return; }
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2)) { this.$ctor1(...args); return; }
    if (args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number')) { this.$ctor6(...args); return; }
    if (args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number')) { this.$ctor4(...args); return; }
    if (args.length === 0) { this.$ctor0(...args); return; }
    throw new ArgumentException('No matching dimension constructor. Use CreateOverload for an explicit signature.');
  }
  $ctor0() {
    this.$ctor3(Vector2.Zero, Vector2.$create1(0.5, 0), Vector2.$create1(1, 0), 1, DimensionStyle.Default);
  }
  static $create0(...args) { return new OrdinateDimension(ConstructorTag, 0, args); }
  $ctor1(origin, featurePoint, leaderEndPoint) {
    this.$ctor2(origin, featurePoint, leaderEndPoint, DimensionStyle.Default);
  }
  static $create1(...args) { return new OrdinateDimension(ConstructorTag, 1, args); }
  $ctor2(origin, featurePoint, leaderEndPoint, style) {
    this.$defPoint = Copy(origin);
    this.$firstPoint = Copy(featurePoint);
    this.$secondPoint = Copy(leaderEndPoint);
    this.$textRefPoint = Copy(leaderEndPoint);
    let vec = Vector2.op_Subtraction(leaderEndPoint, featurePoint);
    this.$axis = ((vec.Y > vec.X) ? 0 : 1);
    this.$rotation = 0;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
  }
  static $create2(...args) { return new OrdinateDimension(ConstructorTag, 2, args); }
  $ctor3(origin, featurePoint, leaderEndPoint, axis, style) {
    this.$defPoint = Copy(origin);
    this.$firstPoint = Copy(featurePoint);
    this.$secondPoint = Copy(leaderEndPoint);
    this.$textRefPoint = Copy(leaderEndPoint);
    this.$axis = axis;
    this.$rotation = 0;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
  }
  static $create3(...args) { return new OrdinateDimension(ConstructorTag, 3, args); }
  $ctor4(origin, featurePoint, length, axis) {
    this.$ctor7(origin, featurePoint, length, axis, 0, DimensionStyle.Default);
  }
  static $create4(...args) { return new OrdinateDimension(ConstructorTag, 4, args); }
  $ctor5(origin, featurePoint, length, axis, style) {
    this.$ctor7(origin, featurePoint, length, axis, 0, style);
  }
  static $create5(...args) { return new OrdinateDimension(ConstructorTag, 5, args); }
  $ctor6(origin, featurePoint, length, axis, rotation) {
    this.$ctor7(origin, featurePoint, length, axis, rotation, DimensionStyle.Default);
  }
  static $create6(...args) { return new OrdinateDimension(ConstructorTag, 6, args); }
  $ctor7(origin, featurePoint, length, axis, rotation, style) {
    this.$defPoint = Copy(origin);
    this.$rotation = MathHelper.NormalizeAngle(rotation);
    this.$firstPoint = Copy(featurePoint);
    this.$axis = axis;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    let angle = (rotation * 0.017453292519943295);
    if ((RequireReference(this).Axis === 0))
    {
      angle += 1.5707963267948966;
    }
    this.$secondPoint = Vector2.Polar(featurePoint, length, angle);
    this.$textRefPoint = Copy(this.$secondPoint);
  }
  static $create7(...args) { return new OrdinateDimension(ConstructorTag, 7, args); }
  static CreateOverload(signature, ...args) {
    if (signature === "") { if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.'); return OrdinateDimension.$create0(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,netDxf.Vector2") { if (!(args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2))) throw new ArgumentException('Arguments do not match the selected constructor.'); return OrdinateDimension.$create1(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,netDxf.Vector2,netDxf.Tables.DimensionStyle") { if (!(args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (args[3] === null || args[3] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return OrdinateDimension.$create2(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,netDxf.Vector2,netDxf.Entities.OrdinateDimensionAxis,netDxf.Tables.DimensionStyle") { if (!(args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return OrdinateDimension.$create3(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,netDxf.Entities.OrdinateDimensionAxis") { if (!(args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return OrdinateDimension.$create4(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,netDxf.Entities.OrdinateDimensionAxis,netDxf.Tables.DimensionStyle") { if (!(args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return OrdinateDimension.$create5(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,netDxf.Entities.OrdinateDimensionAxis,double") { if (!(args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return OrdinateDimension.$create6(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,netDxf.Entities.OrdinateDimensionAxis,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 6 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (typeof args[4] === 'number') && (args[5] === null || args[5] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return OrdinateDimension.$create7(...args); }
    throw new ArgumentException('Unknown dimension constructor signature.', 'signature');
  }
  get Origin() {
    return Copy(this.$defPoint);
  }
  set Origin(value) {
    this.$defPoint = Copy(value);
  }
  get FeaturePoint() {
    return Copy(this.$firstPoint);
  }
  set FeaturePoint(value) {
    this.$firstPoint = Copy(value);
  }
  get LeaderEndPoint() {
    return Copy(this.$secondPoint);
  }
  set LeaderEndPoint(value) {
    this.$secondPoint = Copy(value);
  }
  get Rotation() {
    return this.$rotation;
  }
  set Rotation(value) {
    MathHelper.NormalizeAngle(this.$rotation = value);
  }
  get Axis() {
    return this.$axis;
  }
  set Axis(value) {
    this.$axis = value;
  }
  get Measurement() {
    let dirRef = Vector2.Rotate(((this.$axis === 0) ? Vector2.UnitY : Vector2.UnitX), (this.$rotation * 0.017453292519943295));
    return MathHelper.$PointLineDistance1(this.$firstPoint, this.$defPoint, dirRef);
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
    let refAxis = Matrix3.$op_Multiply1(transOW, Vector3.UnitX);
    refAxis = Matrix3.$op_Multiply1(transformation, refAxis);
    refAxis = Matrix3.$op_Multiply1(transWO, refAxis);
    let newRotation = (Vector2.$Angle0(Vector2.$create1(refAxis.X, refAxis.Y)) * 57.29577951308232);
    let v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).FeaturePoint.X, RequireReference(this).FeaturePoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newStart = Vector2.$create1(v.X, v.Y);
    let newElevation = v.Z;
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).LeaderEndPoint.X, RequireReference(this).LeaderEndPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newEnd = Vector2.$create1(v.X, v.Y);
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(this.$textRefPoint.X, this.$textRefPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    this.$textRefPoint = Vector2.$create1(v.X, v.Y);
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(this.$defPoint.X, this.$defPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    this.$defPoint = Vector2.$create1(v.X, v.Y);
    RequireReference(this).Rotation += newRotation;
    RequireReference(this).FeaturePoint = Copy(newStart);
    RequireReference(this).LeaderEndPoint = Copy(newEnd);
    RequireReference(this).Elevation = newElevation;
    RequireReference(this).Normal = Copy(newNormal);
  }
  CalculateReferencePoints() {
    if (RequireReference(this).TextPositionManuallySet)
    {
      let moveText = RequireReference(RequireReference(this).Style).FitTextMove;
      let styleOverride = null;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(37, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        moveText = DimensionValue(RequireReference(styleOverride).Value);
      }
      if ((moveText !== 2))
      {
        this.$secondPoint = Copy(this.$textRefPoint);
      }
    }
    else
    {
      this.$textRefPoint = Copy(this.$secondPoint);
    }
  }
  BuildBlock(name) {
    return DimensionBlock.$Build8(this, name);
  }
  Clone() {
    let value;
    let entity = Init(OrdinateDimension.$create0(), $new => { $new.Layer = RequireReference(RequireReference(this).Layer).Clone(); $new.Linetype = RequireReference(RequireReference(this).Linetype).Clone(); $new.Color = RequireReference(this).Color.Clone(); $new.Lineweight = RequireReference(this).Lineweight; $new.Transparency = RequireReference(this).Transparency.Clone(); $new.LinetypeScale = RequireReference(this).LinetypeScale; $new.Normal = RequireReference(this).Normal; $new.IsVisible = RequireReference(this).IsVisible; $new.Style = RequireReference(RequireReference(this).Style).Clone(); $new.DefinitionPoint = Copy(this.$defPoint); $new.TextReferencePoint = RequireReference(this).TextReferencePoint; $new.TextPositionManuallySet = RequireReference(this).TextPositionManuallySet; $new.TextRotation = RequireReference(this).TextRotation; $new.AttachmentPoint = RequireReference(this).AttachmentPoint; $new.LineSpacingStyle = RequireReference(this).LineSpacingStyle; $new.LineSpacingFactor = RequireReference(this).LineSpacingFactor; $new.UserText = RequireReference(this).UserText; $new.Elevation = RequireReference(this).Elevation; $new.FeaturePoint = Copy(this.$firstPoint); $new.LeaderEndPoint = Copy(this.$secondPoint); $new.Rotation = this.$rotation; $new.Axis = this.$axis; });
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
