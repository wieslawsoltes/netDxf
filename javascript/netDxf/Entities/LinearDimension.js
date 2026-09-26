// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Dimension } from './Dimension.js';
import { DimensionBlock } from './DimensionBlock.js';
import { Line } from './Line.js';
import { MathHelper } from './../MathHelper.js';
import { Matrix3 } from './../Matrix3.js';
import { DimensionStyle } from './../Tables/DimensionStyle.js';
import { DimensionStyleOverride } from './../Tables/DimensionStyleOverride.js';
import { Vector2 } from './../Vector2.js';
import { Vector3 } from './../Vector3.js';
import { DimensionValue, Cloneable, RequireReference, StringEquals, StringReplace, StringSubstring, DimensionCulture } from '../../runtime/DimensionRuntime.js';

export class LinearDimension extends Dimension {
  // C# backing state is prefixed with $; public members retain their original names.
  $firstRefPoint = new Vector2();
  $secondRefPoint = new Vector2();
  $offset = 0;
  $rotation = 0;
  constructor(...args) {
    super(0);
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 5 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (args[3] instanceof Vector3) && (args[4] === null || args[4] instanceof DimensionStyle)) { this.$ctor4(...args); return; }
    if (args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle)) { this.$ctor6(...args); return; }
    if (args.length === 4 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (args[3] === null || args[3] instanceof DimensionStyle)) { this.$ctor2(...args); return; }
    if (args.length === 4 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (args[3] instanceof Vector3)) { this.$ctor3(...args); return; }
    if (args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number')) { this.$ctor5(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (typeof args[2] === 'number')) { this.$ctor1(...args); return; }
    if (args.length === 0) { this.$ctor0(...args); return; }
    throw new ArgumentException('No matching dimension constructor. Use CreateOverload for an explicit signature.');
  }
  $ctor0() {
    this.$ctor5(Vector2.Zero, Vector2.UnitX, 0.1, 0);
  }
  static $create0(...args) { return new LinearDimension(ConstructorTag, 0, args); }
  $ctor1(referenceLine, offset, rotation) {
    this.$ctor4(referenceLine, offset, rotation, Vector3.UnitZ, DimensionStyle.Default);
  }
  static $create1(...args) { return new LinearDimension(ConstructorTag, 1, args); }
  $ctor2(referenceLine, offset, rotation, style) {
    this.$ctor4(referenceLine, offset, rotation, Vector3.UnitZ, style);
  }
  static $create2(...args) { return new LinearDimension(ConstructorTag, 2, args); }
  $ctor3(referenceLine, offset, rotation, normal) {
    this.$ctor4(referenceLine, offset, rotation, normal, DimensionStyle.Default);
  }
  static $create3(...args) { return new LinearDimension(ConstructorTag, 3, args); }
  $ctor4(referenceLine, offset, rotation, normal, style) {
    if ((referenceLine === null))
    {
      throw new Errors.ArgumentNullException("referenceLine");
    }
    let ocsPoints = MathHelper.$Transform3(new List([RequireReference(referenceLine).StartPoint, RequireReference(referenceLine).EndPoint]), normal, 0, 1);
    this.$firstRefPoint = Vector2.$create1(GetElement(ocsPoints, 0).X, GetElement(ocsPoints, 0).Y);
    this.$secondRefPoint = Vector2.$create1(GetElement(ocsPoints, 1).X, GetElement(ocsPoints, 1).Y);
    this.$offset = offset;
    this.$rotation = MathHelper.NormalizeAngle(rotation);
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Normal = Copy(normal);
    RequireReference(this).Elevation = GetElement(ocsPoints, 0).Z;
    RequireReference(this).Update();
  }
  static $create4(...args) { return new LinearDimension(ConstructorTag, 4, args); }
  $ctor5(firstPoint, secondPoint, offset, rotation) {
    this.$ctor6(firstPoint, secondPoint, offset, rotation, DimensionStyle.Default);
  }
  static $create5(...args) { return new LinearDimension(ConstructorTag, 5, args); }
  $ctor6(firstPoint, secondPoint, offset, rotation, style) {
    this.$firstRefPoint = Copy(firstPoint);
    this.$secondRefPoint = Copy(secondPoint);
    this.$offset = offset;
    this.$rotation = MathHelper.NormalizeAngle(rotation);
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Update();
  }
  static $create6(...args) { return new LinearDimension(ConstructorTag, 6, args); }
  static CreateOverload(signature, ...args) {
    if (signature === "") { if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.'); return LinearDimension.$create0(...args); }
    if (signature === "netDxf.Entities.Line,double,double") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return LinearDimension.$create1(...args); }
    if (signature === "netDxf.Entities.Line,double,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 4 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (args[3] === null || args[3] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return LinearDimension.$create2(...args); }
    if (signature === "netDxf.Entities.Line,double,double,netDxf.Vector3") { if (!(args.length === 4 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (args[3] instanceof Vector3))) throw new ArgumentException('Arguments do not match the selected constructor.'); return LinearDimension.$create3(...args); }
    if (signature === "netDxf.Entities.Line,double,double,netDxf.Vector3,netDxf.Tables.DimensionStyle") { if (!(args.length === 5 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (typeof args[2] === 'number') && (args[3] instanceof Vector3) && (args[4] === null || args[4] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return LinearDimension.$create4(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,double") { if (!(args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return LinearDimension.$create5(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (typeof args[3] === 'number') && (args[4] === null || args[4] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return LinearDimension.$create6(...args); }
    throw new ArgumentException('Unknown dimension constructor signature.', 'signature');
  }
  get FirstReferencePoint() {
    return Copy(this.$firstRefPoint);
  }
  set FirstReferencePoint(value) {
    this.$firstRefPoint = Copy(value);
  }
  get SecondReferencePoint() {
    return Copy(this.$secondRefPoint);
  }
  set SecondReferencePoint(value) {
    this.$secondRefPoint = Copy(value);
  }
  get DimLinePosition() {
    return Copy(this.$defPoint);
  }
  get Rotation() {
    return this.$rotation;
  }
  set Rotation(value) {
    this.$rotation = MathHelper.NormalizeAngle(value);
  }
  get Offset() {
    return this.$offset;
  }
  set Offset(value) {
    this.$offset = value;
  }
  get Measurement() {
    let refRot = Vector2.$Angle1(this.$firstRefPoint, this.$secondRefPoint);
    return DotNetMath.Abs(MultiplyDouble(Vector2.Distance(this.$firstRefPoint, this.$secondRefPoint), DotNetMath.Cos(((this.$rotation * 0.017453292519943295) - refRot))));
  }
  SetDimensionLinePosition(point) {
    let midRef = Vector2.MidPoint(this.$firstRefPoint, this.$secondRefPoint);
    let dimRotation = (RequireReference(this).Rotation * 0.017453292519943295);
    let pointDir = Vector2.op_Subtraction(point, this.$firstRefPoint);
    let dimDir = Vector2.Normalize(Vector2.Rotate(Vector2.UnitX, dimRotation));
    this.$offset = MathHelper.$PointLineDistance1(midRef, point, dimDir);
    let cross = Vector2.CrossProduct(dimDir, pointDir);
    if ((cross < 0))
    {
      this.$offset *= (-1);
    }
    let offsetDir = Vector2.Perpendicular(dimDir);
    let midDimLine = Vector2.op_Addition(midRef, Vector2.$op_Multiply1(this.$offset, offsetDir));
    this.$defPoint = Vector2.op_Addition(midDimLine, Vector2.$op_Multiply1((0.5 * RequireReference(this).Measurement), dimDir));
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
      if (((dimRotation > 1.5707963267948966) && (dimRotation <= 4.71238898038469)))
      {
        gap = (-gap);
      }
      this.$textRefPoint = Vector2.op_Addition(midDimLine, Vector2.$op_Multiply1(gap, offsetDir));
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
    let v = new Vector3();
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).FirstReferencePoint.X, RequireReference(this).FirstReferencePoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newStart = Vector2.$create1(v.X, v.Y);
    let newElevation = v.Z;
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).SecondReferencePoint.X, RequireReference(this).SecondReferencePoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newEnd = Vector2.$create1(v.X, v.Y);
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
    let refAxis = Vector2.Rotate(Vector2.UnitX, (RequireReference(this).Rotation * 0.017453292519943295));
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(refAxis.X, refAxis.Y, RequireReference(this).Elevation));
    v = Matrix3.$op_Multiply1(transformation, v);
    v = Matrix3.$op_Multiply1(transWO, v);
    let axis = Vector2.$create1(v.X, v.Y);
    let newRotation = (Vector2.$Angle0(axis) * 57.29577951308232);
    RequireReference(this).Rotation = newRotation;
    RequireReference(this).FirstReferencePoint = Copy(newStart);
    RequireReference(this).SecondReferencePoint = Copy(newEnd);
    RequireReference(this).Elevation = newElevation;
    RequireReference(this).Normal = Copy(newNormal);
    this.SetDimensionLinePosition(this.$defPoint);
  }
  CalculateReferencePoints() {
    let styleOverride = null;
    let measure = RequireReference(this).Measurement;
    let midRef = Vector2.MidPoint(this.$firstRefPoint, this.$secondRefPoint);
    let dimRotation = (RequireReference(this).Rotation * 0.017453292519943295);
    let vec = Vector2.Normalize(Vector2.Rotate(Vector2.UnitY, dimRotation));
    let midDimLine = Vector2.op_Addition(midRef, Vector2.$op_Multiply1(this.$offset, vec));
    let cross = Vector2.CrossProduct(Vector2.op_Subtraction(this.$secondRefPoint, this.$firstRefPoint), vec);
    if ((cross < 0))
    {
      this.$offset *= (-1);
    }
    this.$defPoint = Vector2.op_Subtraction(midDimLine, Vector2.$op_Multiply1((measure * 0.5), Vector2.Perpendicular(vec)));
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
      if (((dimRotation > 1.5707963267948966) && (dimRotation <= 4.71238898038469)))
      {
        gap = (-gap);
      }
      this.$textRefPoint = Vector2.op_Addition(midDimLine, Vector2.$op_Multiply1(gap, vec));
    }
  }
  BuildBlock(name) {
    return DimensionBlock.$Build3(this, name);
  }
  Clone() {
    let value;
    let entity = Init(LinearDimension.$create0(), $new => { $new.Layer = RequireReference(RequireReference(this).Layer).Clone(); $new.Linetype = RequireReference(RequireReference(this).Linetype).Clone(); $new.Color = RequireReference(this).Color.Clone(); $new.Lineweight = RequireReference(this).Lineweight; $new.Transparency = RequireReference(this).Transparency.Clone(); $new.LinetypeScale = RequireReference(this).LinetypeScale; $new.Normal = RequireReference(this).Normal; $new.IsVisible = RequireReference(this).IsVisible; $new.Style = RequireReference(RequireReference(this).Style).Clone(); $new.DefinitionPoint = RequireReference(this).DefinitionPoint; $new.TextReferencePoint = RequireReference(this).TextReferencePoint; $new.TextPositionManuallySet = RequireReference(this).TextPositionManuallySet; $new.TextRotation = RequireReference(this).TextRotation; $new.AttachmentPoint = RequireReference(this).AttachmentPoint; $new.LineSpacingStyle = RequireReference(this).LineSpacingStyle; $new.LineSpacingFactor = RequireReference(this).LineSpacingFactor; $new.UserText = RequireReference(this).UserText; $new.FirstReferencePoint = Copy(this.$firstRefPoint); $new.SecondReferencePoint = Copy(this.$secondRefPoint); $new.Rotation = this.$rotation; $new.Offset = this.$offset; $new.Elevation = RequireReference(this).Elevation; });
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
