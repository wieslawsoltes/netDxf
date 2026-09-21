// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Arc } from './Arc.js';
import { Dimension } from './Dimension.js';
import { DimensionBlock } from './DimensionBlock.js';
import { MathHelper } from './../MathHelper.js';
import { Matrix3 } from './../Matrix3.js';
import { DimensionStyle } from './../Tables/DimensionStyle.js';
import { DimensionStyleOverride } from './../Tables/DimensionStyleOverride.js';
import { Vector2 } from './../Vector2.js';
import { Vector3 } from './../Vector3.js';
import { DimensionValue, Cloneable, RequireReference, StringEquals, StringReplace, StringSubstring, DimensionCulture } from '../../runtime/DimensionRuntime.js';

export class Angular3PointDimension extends Dimension {
  // C# backing state is prefixed with $; public members retain their original names.
  $offset = 0;
  $center = new Vector2();
  $start = new Vector2();
  $end = new Vector2();
  constructor(...args) {
    super(5);
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle)) { this.$ctor4(...args); return; }
    if (args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (typeof args[3] === 'number')) { this.$ctor3(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle)) { this.$ctor2(...args); return; }
    if (args.length === 2 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number')) { this.$ctor1(...args); return; }
    if (args.length === 0) { this.$ctor0(...args); return; }
    throw new ArgumentException('No matching dimension constructor. Use CreateOverload for an explicit signature.');
  }
  $ctor0() {
    this.$ctor3(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, 0.1);
  }
  static $create0(...args) { return new Angular3PointDimension(ConstructorTag, 0, args); }
  $ctor1(arc, offset) {
    this.$ctor2(arc, offset, DimensionStyle.Default);
  }
  static $create1(...args) { return new Angular3PointDimension(ConstructorTag, 1, args); }
  $ctor2(arc, offset, style) {
    if ((arc === null))
    {
      throw new Errors.ArgumentNullException("arc");
    }
    let refPoint = MathHelper.$Transform2(RequireReference(arc).Center, RequireReference(arc).Normal, 0, 1);
    this.$center = Vector2.$create1(refPoint.X, refPoint.Y);
    this.$start = Vector2.Polar(this.$center, RequireReference(arc).Radius, (RequireReference(arc).StartAngle * 0.017453292519943295));
    this.$end = Vector2.Polar(this.$center, RequireReference(arc).Radius, (RequireReference(arc).EndAngle * 0.017453292519943295));
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Normal = RequireReference(arc).Normal;
    RequireReference(this).Elevation = refPoint.Z;
    RequireReference(this).Update();
  }
  static $create2(...args) { return new Angular3PointDimension(ConstructorTag, 2, args); }
  $ctor3(centerPoint, startPoint, endPoint, offset) {
    this.$ctor4(centerPoint, startPoint, endPoint, offset, DimensionStyle.Default);
  }
  static $create3(...args) { return new Angular3PointDimension(ConstructorTag, 3, args); }
  $ctor4(centerPoint, startPoint, endPoint, offset, style) {
    this.$center = Copy(centerPoint);
    this.$start = Copy(startPoint);
    this.$end = Copy(endPoint);
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Update();
  }
  static $create4(...args) { return new Angular3PointDimension(ConstructorTag, 4, args); }
  static CreateOverload(signature, ...args) {
    if (signature === "") { if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular3PointDimension.$create0(...args); }
    if (signature === "netDxf.Entities.Arc,double") { if (!(args.length === 2 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular3PointDimension.$create1(...args); }
    if (signature === "netDxf.Entities.Arc,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Arc) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular3PointDimension.$create2(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,netDxf.Vector2,double") { if (!(args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (typeof args[3] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular3PointDimension.$create3(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,netDxf.Vector2,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular3PointDimension.$create4(...args); }
    throw new ArgumentException('Unknown dimension constructor signature.', 'signature');
  }
  get CenterPoint() {
    return Copy(this.$center);
  }
  set CenterPoint(value) {
    this.$center = Copy(value);
  }
  get StartPoint() {
    return Copy(this.$start);
  }
  set StartPoint(value) {
    this.$start = Copy(value);
  }
  get EndPoint() {
    return Copy(this.$end);
  }
  set EndPoint(value) {
    this.$end = Copy(value);
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
  get Measurement() {
    let dirRef1 = Vector2.op_Subtraction(this.$start, this.$center);
    let dirRef2 = Vector2.op_Subtraction(this.$end, this.$center);
    if (Vector2.$Equals0(dirRef1, dirRef2))
    {
      return 0;
    }
    if (Vector2.$AreParallel0(dirRef1, dirRef2))
    {
      return 180;
    }
    let angle = (Vector2.AngleBetween(dirRef1, dirRef2) * 57.29577951308232);
    if ((this.$offset < 0))
    {
      return (360 - angle);
    }
    return angle;
  }
  SetDimensionLinePosition(point) {
    let newOffset = Vector2.Distance(this.$center, point);
    this.$offset = newOffset;
    let dirPoint = Vector2.op_Subtraction(point, this.$center);
    let cross1 = Vector2.CrossProduct(Vector2.op_Subtraction(this.$start, this.$center), dirPoint);
    let cross2 = Vector2.CrossProduct(Vector2.op_Subtraction(this.$end, this.$center), dirPoint);
    if (((!((cross1 >= 0))) || (!((cross2 < 0)))))
    {
      this.$offset *= (-1);
    }
    let startAngle = ((this.$offset >= 0) ? Vector2.$Angle1(this.$center, this.$start) : Vector2.$Angle1(this.$center, this.$end));
    let midRot = (startAngle + ((0.5 * RequireReference(this).Measurement) * 0.017453292519943295));
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
    let v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).StartPoint.X, RequireReference(this).StartPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newStart = Vector2.$create1(v.X, v.Y);
    let newElevation = v.Z;
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).EndPoint.X, RequireReference(this).EndPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newEnd = Vector2.$create1(v.X, v.Y);
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).CenterPoint.X, RequireReference(this).CenterPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newCenter = Vector2.$create1(v.X, v.Y);
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
    RequireReference(this).StartPoint = Copy(newStart);
    RequireReference(this).EndPoint = Copy(newEnd);
    RequireReference(this).CenterPoint = Copy(newCenter);
    RequireReference(this).Elevation = newElevation;
    RequireReference(this).Normal = Copy(newNormal);
    this.SetDimensionLinePosition(this.$defPoint);
  }
  CalculateReferencePoints() {
    let styleOverride = null;
    let startAngle = ((this.$offset >= 0) ? Vector2.$Angle1(this.$center, this.$start) : Vector2.$Angle1(this.$center, this.$end));
    let midRot = (startAngle + ((0.5 * RequireReference(this).Measurement) * 0.017453292519943295));
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
    return DimensionBlock.$Build5(this, name);
  }
  Clone() {
    let value;
    let entity = Init(Angular3PointDimension.$create0(), $new => { $new.Layer = RequireReference(RequireReference(this).Layer).Clone(); $new.Linetype = RequireReference(RequireReference(this).Linetype).Clone(); $new.Color = RequireReference(this).Color.Clone(); $new.Lineweight = RequireReference(this).Lineweight; $new.Transparency = RequireReference(this).Transparency.Clone(); $new.LinetypeScale = RequireReference(this).LinetypeScale; $new.Normal = RequireReference(this).Normal; $new.IsVisible = RequireReference(this).IsVisible; $new.Style = RequireReference(RequireReference(this).Style).Clone(); $new.DefinitionPoint = RequireReference(this).DefinitionPoint; $new.TextReferencePoint = RequireReference(this).TextReferencePoint; $new.TextPositionManuallySet = RequireReference(this).TextPositionManuallySet; $new.TextRotation = RequireReference(this).TextRotation; $new.AttachmentPoint = RequireReference(this).AttachmentPoint; $new.LineSpacingStyle = RequireReference(this).LineSpacingStyle; $new.LineSpacingFactor = RequireReference(this).LineSpacingFactor; $new.UserText = RequireReference(this).UserText; $new.Elevation = RequireReference(this).Elevation; $new.CenterPoint = Copy(this.$center); $new.StartPoint = Copy(this.$start); $new.EndPoint = Copy(this.$end); $new.Offset = this.$offset; });
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
