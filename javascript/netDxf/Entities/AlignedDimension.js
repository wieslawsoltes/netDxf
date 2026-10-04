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

export class AlignedDimension extends Dimension {
  // C# backing state is prefixed with $; public members retain their original names.
  $firstRefPoint = new Vector2();
  $secondRefPoint = new Vector2();
  $offset = 0;
  constructor(...args) {
    super(1);
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 4 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (args[2] instanceof Vector3) && (args[3] === null || args[3] instanceof DimensionStyle)) { this.$ctor4(...args); return; }
    if (args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (args[3] === null || args[3] instanceof DimensionStyle)) { this.$ctor6(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle)) { this.$ctor2(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (args[2] instanceof Vector3)) { this.$ctor3(...args); return; }
    if (args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number')) { this.$ctor5(...args); return; }
    if (args.length === 2 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number')) { this.$ctor1(...args); return; }
    if (args.length === 0) { this.$ctor0(...args); return; }
    throw new ArgumentException('No matching dimension constructor. Use CreateOverload for an explicit signature.');
  }
  $ctor0() {
    this.$ctor5(Vector2.Zero, Vector2.UnitX, 0.1);
  }
  static $create0(...args) { return new AlignedDimension(ConstructorTag, 0, args); }
  $ctor1(referenceLine, offset) {
    this.$ctor4(referenceLine, offset, Vector3.UnitZ, DimensionStyle.Default);
  }
  static $create1(...args) { return new AlignedDimension(ConstructorTag, 1, args); }
  $ctor2(referenceLine, offset, style) {
    this.$ctor4(referenceLine, offset, Vector3.UnitZ, style);
  }
  static $create2(...args) { return new AlignedDimension(ConstructorTag, 2, args); }
  $ctor3(referenceLine, offset, normal) {
    this.$ctor4(referenceLine, offset, normal, DimensionStyle.Default);
  }
  static $create3(...args) { return new AlignedDimension(ConstructorTag, 3, args); }
  $ctor4(referenceLine, offset, normal, style) {
    if ((referenceLine === null))
    {
      throw new Errors.ArgumentNullException("referenceLine");
    }
    let ocsPoints = MathHelper.$Transform3(new List([RequireReference(referenceLine).StartPoint, RequireReference(referenceLine).EndPoint]), normal, 0, 1);
    this.$firstRefPoint = Vector2.$create1(GetElement(ocsPoints, 0).X, GetElement(ocsPoints, 0).Y);
    this.$secondRefPoint = Vector2.$create1(GetElement(ocsPoints, 1).X, GetElement(ocsPoints, 1).Y);
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Normal = Copy(normal);
    RequireReference(this).Elevation = GetElement(ocsPoints, 0).Z;
    RequireReference(this).Update();
  }
  static $create4(...args) { return new AlignedDimension(ConstructorTag, 4, args); }
  $ctor5(firstPoint, secondPoint, offset) {
    this.$ctor6(firstPoint, secondPoint, offset, DimensionStyle.Default);
  }
  static $create5(...args) { return new AlignedDimension(ConstructorTag, 5, args); }
  $ctor6(firstPoint, secondPoint, offset, style) {
    this.$firstRefPoint = Copy(firstPoint);
    this.$secondRefPoint = Copy(secondPoint);
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Update();
  }
  static $create6(...args) { return new AlignedDimension(ConstructorTag, 6, args); }
  static CreateOverload(signature, ...args) {
    if (signature === "") { if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.'); return AlignedDimension.$create0(...args); }
    if (signature === "netDxf.Entities.Line,double") { if (!(args.length === 2 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return AlignedDimension.$create1(...args); }
    if (signature === "netDxf.Entities.Line,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (args[2] === null || args[2] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return AlignedDimension.$create2(...args); }
    if (signature === "netDxf.Entities.Line,double,netDxf.Vector3") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (args[2] instanceof Vector3))) throw new ArgumentException('Arguments do not match the selected constructor.'); return AlignedDimension.$create3(...args); }
    if (signature === "netDxf.Entities.Line,double,netDxf.Vector3,netDxf.Tables.DimensionStyle") { if (!(args.length === 4 && (args[0] === null || args[0] instanceof Line) && (typeof args[1] === 'number') && (args[2] instanceof Vector3) && (args[3] === null || args[3] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return AlignedDimension.$create4(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double") { if (!(args.length === 3 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return AlignedDimension.$create5(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 4 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (typeof args[2] === 'number') && (args[3] === null || args[3] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return AlignedDimension.$create6(...args); }
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
  get Offset() {
    return this.$offset;
  }
  set Offset(value) {
    this.$offset = value;
  }
  get Measurement() {
    return Vector2.Distance(this.$firstRefPoint, this.$secondRefPoint);
  }
  SetDimensionLinePosition(point) {
    let refDir = Vector2.op_Subtraction(this.$secondRefPoint, this.$firstRefPoint);
    let offsetDir = Vector2.op_Subtraction(point, this.$firstRefPoint);
    let cross = Vector2.CrossProduct(refDir, offsetDir);
    refDir.Normalize();
    let vec = Vector2.Perpendicular(refDir);
    this.$offset = MultiplyDouble(DotNetMath.Sign(cross), MathHelper.$PointLineDistance1(point, this.$firstRefPoint, refDir));
    this.$defPoint = Vector2.op_Addition(this.$secondRefPoint, Vector2.$op_Multiply1(this.$offset, vec));
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
      let gap = (this.$offset + MultiplyDouble(textGap, scale));
      this.$textRefPoint = Vector2.op_Addition(Vector2.MidPoint(this.$firstRefPoint, this.$secondRefPoint), Vector2.$op_Multiply1(gap, vec));
    }
  }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    let newNormal = Matrix3.$op_Multiply1(transformation, RequireReference(this).Normal);
    if (Vector3.$Equals0(Vector3.Zero, newNormal))
    newNormal = RequireReference(this).Normal;
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
    RequireReference(this).FirstReferencePoint = Copy(newStart);
    RequireReference(this).SecondReferencePoint = Copy(newEnd);
    RequireReference(this).Elevation = newElevation;
    RequireReference(this).Normal = Copy(newNormal);
    this.SetDimensionLinePosition(this.$defPoint);
  }
  CalculateReferencePoints() {
    let styleOverride = null;
    let ref1 = RequireReference(this).FirstReferencePoint;
    let ref2 = RequireReference(this).SecondReferencePoint;
    let dirRef = Vector2.op_Subtraction(ref2, ref1);
    let dirDesp = Vector2.Normalize(Vector2.Perpendicular(dirRef));
    let vec = Vector2.$op_Multiply1(this.$offset, dirDesp);
    let dimRef1 = Vector2.op_Addition(ref1, vec);
    let dimRef2 = Vector2.op_Addition(ref2, vec);
    this.$defPoint = Copy(dimRef2);
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
      this.$textRefPoint = Vector2.op_Addition(Vector2.MidPoint(dimRef1, dimRef2), Vector2.$op_Multiply1(gap, dirDesp));
    }
  }
  BuildBlock(name) {
    return DimensionBlock.$Build2(this, name);
  }
  Clone() {
    let value;
    let entity = Init(AlignedDimension.$create0(), $new => { $new.Layer = RequireReference(RequireReference(this).Layer).Clone(); $new.Linetype = RequireReference(RequireReference(this).Linetype).Clone(); $new.Color = RequireReference(this).Color.Clone(); $new.Lineweight = RequireReference(this).Lineweight; $new.Transparency = RequireReference(this).Transparency.Clone(); $new.LinetypeScale = RequireReference(this).LinetypeScale; $new.Normal = RequireReference(this).Normal; $new.IsVisible = RequireReference(this).IsVisible; $new.Style = RequireReference(RequireReference(this).Style).Clone(); $new.DefinitionPoint = RequireReference(this).DefinitionPoint; $new.TextReferencePoint = RequireReference(this).TextReferencePoint; $new.TextPositionManuallySet = RequireReference(this).TextPositionManuallySet; $new.TextRotation = RequireReference(this).TextRotation; $new.AttachmentPoint = RequireReference(this).AttachmentPoint; $new.LineSpacingStyle = RequireReference(this).LineSpacingStyle; $new.LineSpacingFactor = RequireReference(this).LineSpacingFactor; $new.UserText = RequireReference(this).UserText; $new.Elevation = RequireReference(this).Elevation; $new.FirstReferencePoint = Copy(this.$firstRefPoint); $new.SecondReferencePoint = Copy(this.$secondRefPoint); $new.Offset = this.$offset; });
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
