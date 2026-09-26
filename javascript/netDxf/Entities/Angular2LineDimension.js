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

export class Angular2LineDimension extends Dimension {
  // C# backing state is prefixed with $; public members retain their original names.
  $offset = 0;
  $startFirstLine = new Vector2();
  $endFirstLine = new Vector2();
  $startSecondLine = new Vector2();
  $endSecondLine = new Vector2();
  $arcDefinitionPoint = new Vector2();
  constructor(...args) {
    super(2);
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 6 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (args[3] instanceof Vector2) && (typeof args[4] === 'number') && (args[5] === null || args[5] instanceof DimensionStyle)) { this.$ctor6(...args); return; }
    if (args.length === 5 && (args[0] === null || args[0] instanceof Line) && (args[1] === null || args[1] instanceof Line) && (typeof args[2] === 'number') && (args[3] instanceof Vector3) && (args[4] === null || args[4] instanceof DimensionStyle)) { this.$ctor4(...args); return; }
    if (args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (args[3] instanceof Vector2) && (typeof args[4] === 'number')) { this.$ctor5(...args); return; }
    if (args.length === 4 && (args[0] === null || args[0] instanceof Line) && (args[1] === null || args[1] instanceof Line) && (typeof args[2] === 'number') && (args[3] instanceof Vector3)) { this.$ctor2(...args); return; }
    if (args.length === 4 && (args[0] === null || args[0] instanceof Line) && (args[1] === null || args[1] instanceof Line) && (typeof args[2] === 'number') && (args[3] === null || args[3] instanceof DimensionStyle)) { this.$ctor3(...args); return; }
    if (args.length === 3 && (args[0] === null || args[0] instanceof Line) && (args[1] === null || args[1] instanceof Line) && (typeof args[2] === 'number')) { this.$ctor1(...args); return; }
    if (args.length === 0) { this.$ctor0(...args); return; }
    throw new ArgumentException('No matching dimension constructor. Use CreateOverload for an explicit signature.');
  }
  $ctor0() {
    this.$ctor5(Vector2.Zero, Vector2.UnitX, Vector2.Zero, Vector2.UnitY, 0.1);
  }
  static $create0(...args) { return new Angular2LineDimension(ConstructorTag, 0, args); }
  $ctor1(firstLine, secondLine, offset) {
    this.$ctor4(firstLine, secondLine, offset, Vector3.UnitZ, DimensionStyle.Default);
  }
  static $create1(...args) { return new Angular2LineDimension(ConstructorTag, 1, args); }
  $ctor2(firstLine, secondLine, offset, normal) {
    this.$ctor4(firstLine, secondLine, offset, normal, DimensionStyle.Default);
  }
  static $create2(...args) { return new Angular2LineDimension(ConstructorTag, 2, args); }
  $ctor3(firstLine, secondLine, offset, style) {
    this.$ctor4(firstLine, secondLine, offset, Vector3.UnitZ, style);
  }
  static $create3(...args) { return new Angular2LineDimension(ConstructorTag, 3, args); }
  $ctor4(firstLine, secondLine, offset, normal, style) {
    if ((firstLine === null))
    {
      throw new Errors.ArgumentNullException("firstLine");
    }
    if ((secondLine === null))
    {
      throw new Errors.ArgumentNullException("secondLine");
    }
    if (Vector3.$AreParallel0(RequireReference(firstLine).Direction, RequireReference(secondLine).Direction))
    {
      throw new Errors.ArgumentException("The two lines that define the dimension are parallel.");
    }
    let ocsPoints = MathHelper.$Transform3([RequireReference(firstLine).StartPoint, RequireReference(firstLine).EndPoint, RequireReference(secondLine).StartPoint, RequireReference(secondLine).EndPoint], normal, 0, 1);
    this.$startFirstLine = Vector2.$create1(GetElement(ocsPoints, 0).X, GetElement(ocsPoints, 0).Y);
    this.$endFirstLine = Vector2.$create1(GetElement(ocsPoints, 1).X, GetElement(ocsPoints, 1).Y);
    this.$startSecondLine = Vector2.$create1(GetElement(ocsPoints, 2).X, GetElement(ocsPoints, 2).Y);
    this.$endSecondLine = Vector2.$create1(GetElement(ocsPoints, 3).X, GetElement(ocsPoints, 3).Y);
    if ((offset < 0))
    {
      throw new Errors.ArgumentOutOfRangeException("offset", "The offset value must be equal or greater than zero.");
    }
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Normal = Copy(normal);
    RequireReference(this).Elevation = GetElement(ocsPoints, 0).Z;
    RequireReference(this).Update();
  }
  static $create4(...args) { return new Angular2LineDimension(ConstructorTag, 4, args); }
  $ctor5(startFirstLine, endFirstLine, startSecondLine, endSecondLine, offset) {
    this.$ctor6(startFirstLine, endFirstLine, startSecondLine, endSecondLine, offset, DimensionStyle.Default);
  }
  static $create5(...args) { return new Angular2LineDimension(ConstructorTag, 5, args); }
  $ctor6(startFirstLine, endFirstLine, startSecondLine, endSecondLine, offset, style) {
    let dir1 = Vector2.op_Subtraction(endFirstLine, startFirstLine);
    let dir2 = Vector2.op_Subtraction(endSecondLine, startSecondLine);
    if (Vector2.$AreParallel0(dir1, dir2))
    {
      throw new Errors.ArgumentException("The two lines that define the dimension are parallel.");
    }
    this.$startFirstLine = Copy(startFirstLine);
    this.$endFirstLine = Copy(endFirstLine);
    this.$startSecondLine = Copy(startSecondLine);
    this.$endSecondLine = Copy(endSecondLine);
    if ((offset < 0))
    {
      throw new Errors.ArgumentOutOfRangeException("offset", "The offset value must be equal or greater than zero.");
    }
    this.$offset = offset;
    RequireReference(this).Style = (style ?? (() => { throw new Errors.ArgumentNullException("style"); })());
    RequireReference(this).Update();
  }
  static $create6(...args) { return new Angular2LineDimension(ConstructorTag, 6, args); }
  static CreateOverload(signature, ...args) {
    if (signature === "") { if (!(args.length === 0)) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular2LineDimension.$create0(...args); }
    if (signature === "netDxf.Entities.Line,netDxf.Entities.Line,double") { if (!(args.length === 3 && (args[0] === null || args[0] instanceof Line) && (args[1] === null || args[1] instanceof Line) && (typeof args[2] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular2LineDimension.$create1(...args); }
    if (signature === "netDxf.Entities.Line,netDxf.Entities.Line,double,netDxf.Vector3") { if (!(args.length === 4 && (args[0] === null || args[0] instanceof Line) && (args[1] === null || args[1] instanceof Line) && (typeof args[2] === 'number') && (args[3] instanceof Vector3))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular2LineDimension.$create2(...args); }
    if (signature === "netDxf.Entities.Line,netDxf.Entities.Line,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 4 && (args[0] === null || args[0] instanceof Line) && (args[1] === null || args[1] instanceof Line) && (typeof args[2] === 'number') && (args[3] === null || args[3] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular2LineDimension.$create3(...args); }
    if (signature === "netDxf.Entities.Line,netDxf.Entities.Line,double,netDxf.Vector3,netDxf.Tables.DimensionStyle") { if (!(args.length === 5 && (args[0] === null || args[0] instanceof Line) && (args[1] === null || args[1] instanceof Line) && (typeof args[2] === 'number') && (args[3] instanceof Vector3) && (args[4] === null || args[4] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular2LineDimension.$create4(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,netDxf.Vector2,netDxf.Vector2,double") { if (!(args.length === 5 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (args[3] instanceof Vector2) && (typeof args[4] === 'number'))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular2LineDimension.$create5(...args); }
    if (signature === "netDxf.Vector2,netDxf.Vector2,netDxf.Vector2,netDxf.Vector2,double,netDxf.Tables.DimensionStyle") { if (!(args.length === 6 && (args[0] instanceof Vector2) && (args[1] instanceof Vector2) && (args[2] instanceof Vector2) && (args[3] instanceof Vector2) && (typeof args[4] === 'number') && (args[5] === null || args[5] instanceof DimensionStyle))) throw new ArgumentException('Arguments do not match the selected constructor.'); return Angular2LineDimension.$create6(...args); }
    throw new ArgumentException('Unknown dimension constructor signature.', 'signature');
  }
  get CenterPoint() {
    return MathHelper.$FindIntersection0(this.$startFirstLine, Vector2.op_Subtraction(this.$endFirstLine, this.$startFirstLine), this.$startSecondLine, Vector2.op_Subtraction(this.$endSecondLine, this.$startSecondLine));
  }
  get StartFirstLine() {
    return Copy(this.$startFirstLine);
  }
  set StartFirstLine(value) {
    this.$startFirstLine = Copy(value);
  }
  get EndFirstLine() {
    return Copy(this.$endFirstLine);
  }
  set EndFirstLine(value) {
    this.$endFirstLine = Copy(value);
  }
  get StartSecondLine() {
    return Copy(this.$startSecondLine);
  }
  set StartSecondLine(value) {
    this.$startSecondLine = Copy(value);
  }
  get EndSecondLine() {
    return Copy(this.$endSecondLine);
  }
  set EndSecondLine(value) {
    this.$endSecondLine = Copy(value);
  }
  get ArcDefinitionPoint() {
    return Copy(this.$arcDefinitionPoint);
  }
  set ArcDefinitionPoint(value) {
    this.$arcDefinitionPoint = Copy(value);
  }
  get Offset() {
    return this.$offset;
  }
  set Offset(value) {
    if ((value < 0))
    {
      throw new Errors.ArgumentOutOfRangeException("value", "The offset value must be equal or greater than zero.");
    }
    this.$offset = value;
  }
  get Measurement() {
    let dirRef1 = Vector2.op_Subtraction(this.$endFirstLine, this.$startFirstLine);
    let dirRef2 = Vector2.op_Subtraction(this.$endSecondLine, this.$startSecondLine);
    return (Vector2.AngleBetween(dirRef1, dirRef2) * 57.29577951308232);
  }
  $SetDimensionLinePosition0(point) {
    this.$SetDimensionLinePosition1(point, true);
  }
  $SetDimensionLinePosition1(point, updateRefs) {
    let dir1 = Vector2.op_Subtraction(this.$endFirstLine, this.$startFirstLine);
    let dir2 = Vector2.op_Subtraction(this.$endSecondLine, this.$startSecondLine);
    if (Vector2.$AreParallel0(dir1, dir2))
    {
      throw new Errors.ArgumentException("The two lines that define the dimension are parallel.");
    }
    let center = RequireReference(this).CenterPoint;
    if (updateRefs)
    {
      let cross = Vector2.CrossProduct(Vector2.op_Subtraction(RequireReference(this).EndFirstLine, RequireReference(this).StartFirstLine), Vector2.op_Subtraction(RequireReference(this).EndSecondLine, RequireReference(this).StartSecondLine));
      if ((cross < 0))
      {
        ([this.$startFirstLine, this.$startSecondLine] = [Copy(this.$startSecondLine), Copy(this.$startFirstLine)]);
        ([this.$endFirstLine, this.$endSecondLine] = [Copy(this.$endSecondLine), Copy(this.$endFirstLine)]);
      }
      let ref1Start = RequireReference(this).StartFirstLine;
      let ref1End = RequireReference(this).EndFirstLine;
      let ref2Start = RequireReference(this).StartSecondLine;
      let ref2End = RequireReference(this).EndSecondLine;
      let dirRef1 = Vector2.op_Subtraction(ref1End, ref1Start);
      let dirRef2 = Vector2.op_Subtraction(ref2End, ref2Start);
      let dirOffset = Vector2.op_Subtraction(point, center);
      let crossStart = Vector2.CrossProduct(dirRef1, dirOffset);
      let crossEnd = Vector2.CrossProduct(dirRef2, dirOffset);
      if (((crossStart >= 0) && (crossEnd >= 0)))
      {
        RequireReference(this).StartFirstLine = Copy(ref2Start);
        RequireReference(this).EndFirstLine = Copy(ref2End);
        RequireReference(this).StartSecondLine = Copy(ref1End);
        RequireReference(this).EndSecondLine = Copy(ref1Start);
      }
      else
      if (((crossStart < 0) && (crossEnd >= 0)))
      {
        RequireReference(this).StartFirstLine = Copy(ref1End);
        RequireReference(this).EndFirstLine = Copy(ref1Start);
        RequireReference(this).StartSecondLine = Copy(ref2End);
        RequireReference(this).EndSecondLine = Copy(ref2Start);
      }
      else
      if (((crossStart < 0) && (crossEnd < 0)))
      {
        RequireReference(this).StartFirstLine = Copy(ref2End);
        RequireReference(this).EndFirstLine = Copy(ref2Start);
        RequireReference(this).StartSecondLine = Copy(ref1Start);
        RequireReference(this).EndSecondLine = Copy(ref1End);
      }
    }
    let newOffset = Vector2.Distance(center, point);
    this.$offset = (MathHelper.$IsZero0(newOffset) ? MathHelper.Epsilon : newOffset);
    this.$defPoint = Copy(this.$endSecondLine);
    let measure = (RequireReference(this).Measurement * 0.017453292519943295);
    let startAngle = Vector2.$Angle1(center, this.$endFirstLine);
    let midRot = (startAngle + (0.5 * measure));
    let midDim = Vector2.Polar(center, this.$offset, midRot);
    this.$arcDefinitionPoint = Copy(midDim);
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
      this.$textRefPoint = Vector2.op_Addition(midDim, Vector2.$op_Multiply1(gap, Vector2.Normalize(Vector2.op_Subtraction(midDim, center))));
    }
  }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    let newNormal = Matrix3.$op_Multiply1(transformation, RequireReference(this).Normal);
    if (Vector3.$Equals0(Vector3.Zero, newNormal))
    newNormal = RequireReference(this).Normal;
    let transOW = MathHelper.ArbitraryAxis(RequireReference(this).Normal);
    let transWO = MathHelper.ArbitraryAxis(newNormal).Transpose();
    let v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).StartFirstLine.X, RequireReference(this).StartFirstLine.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newStart1 = Vector2.$create1(v.X, v.Y);
    let newElevation = v.Z;
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).EndFirstLine.X, RequireReference(this).EndFirstLine.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newEnd1 = Vector2.$create1(v.X, v.Y);
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).StartSecondLine.X, RequireReference(this).StartSecondLine.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newStart2 = Vector2.$create1(v.X, v.Y);
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).EndSecondLine.X, RequireReference(this).EndSecondLine.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newEnd2 = Vector2.$create1(v.X, v.Y);
    let dir1 = Vector2.op_Subtraction(newEnd1, newStart1);
    let dir2 = Vector2.op_Subtraction(newEnd2, newStart2);
    if (Vector2.$AreParallel0(dir1, dir2))
    {
      // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
      return;
    }
    v = Matrix3.$op_Multiply1(transOW, Vector3.$create1(RequireReference(this).ArcDefinitionPoint.X, RequireReference(this).ArcDefinitionPoint.Y, RequireReference(this).Elevation));
    v = Vector3.op_Addition(Matrix3.$op_Multiply1(transformation, v), translation);
    v = Matrix3.$op_Multiply1(transWO, v);
    let newArcDefPoint = Vector2.$create1(v.X, v.Y);
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
    RequireReference(this).StartFirstLine = Copy(newStart1);
    RequireReference(this).EndFirstLine = Copy(newEnd1);
    RequireReference(this).StartSecondLine = Copy(newStart2);
    RequireReference(this).EndSecondLine = Copy(newEnd2);
    RequireReference(this).ArcDefinitionPoint = Copy(newArcDefPoint);
    RequireReference(this).Elevation = newElevation;
    RequireReference(this).Normal = Copy(newNormal);
    this.$SetDimensionLinePosition0(newArcDefPoint);
  }
  CalculateReferencePoints() {
    let dir1 = Vector2.op_Subtraction(this.$endFirstLine, this.$startFirstLine);
    let dir2 = Vector2.op_Subtraction(this.$endSecondLine, this.$startSecondLine);
    if (Vector2.$AreParallel0(dir1, dir2))
    {
      throw new Errors.ArgumentException("The two lines that define the dimension are parallel.");
    }
    let styleOverride = null;
    let measure = (RequireReference(this).Measurement * 0.017453292519943295);
    let center = RequireReference(this).CenterPoint;
    let startAngle = Vector2.$Angle1(center, this.$endFirstLine);
    let midRot = (startAngle + (0.5 * measure));
    let midDim = Vector2.Polar(center, this.$offset, midRot);
    this.$defPoint = Copy(this.$endSecondLine);
    this.$arcDefinitionPoint = Copy(midDim);
    if (RequireReference(this).TextPositionManuallySet)
    {
      let moveText = RequireReference(RequireReference(this).Style).FitTextMove;
      if (RequireReference(RequireReference(this).StyleOverrides).TryGetValue(37, { get value() { return styleOverride; }, set value(v) { styleOverride = v; } }))
      {
        moveText = DimensionValue(RequireReference(styleOverride).Value);
      }
      if ((moveText === 0))
      {
        this.$SetDimensionLinePosition1(this.$textRefPoint, false);
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
      this.$textRefPoint = Vector2.op_Addition(midDim, Vector2.$op_Multiply1(gap, Vector2.Normalize(Vector2.op_Subtraction(midDim, center))));
    }
  }
  BuildBlock(name) {
    return DimensionBlock.$Build4(this, name);
  }
  Clone() {
    let value;
    let entity = Init(Angular2LineDimension.$create0(), $new => { $new.Layer = RequireReference(RequireReference(this).Layer).Clone(); $new.Linetype = RequireReference(RequireReference(this).Linetype).Clone(); $new.Color = RequireReference(this).Color.Clone(); $new.Lineweight = RequireReference(this).Lineweight; $new.Transparency = RequireReference(this).Transparency.Clone(); $new.LinetypeScale = RequireReference(this).LinetypeScale; $new.Normal = RequireReference(this).Normal; $new.IsVisible = RequireReference(this).IsVisible; $new.Style = RequireReference(RequireReference(this).Style).Clone(); $new.DefinitionPoint = RequireReference(this).DefinitionPoint; $new.TextReferencePoint = RequireReference(this).TextReferencePoint; $new.TextPositionManuallySet = RequireReference(this).TextPositionManuallySet; $new.TextRotation = RequireReference(this).TextRotation; $new.AttachmentPoint = RequireReference(this).AttachmentPoint; $new.LineSpacingStyle = RequireReference(this).LineSpacingStyle; $new.LineSpacingFactor = RequireReference(this).LineSpacingFactor; $new.UserText = RequireReference(this).UserText; $new.Elevation = RequireReference(this).Elevation; $new.StartFirstLine = Copy(this.$startFirstLine); $new.EndFirstLine = Copy(this.$endFirstLine); $new.StartSecondLine = Copy(this.$startSecondLine); $new.EndSecondLine = Copy(this.$endSecondLine); $new.Offset = this.$offset; $new.$arcDefinitionPoint = Copy(this.$arcDefinitionPoint); });
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
  SetDimensionLinePosition(...args) {
    if (args.length === 2 && (args[0] instanceof Vector2) && (typeof args[1] === 'boolean')) return this.$SetDimensionLinePosition1(...args);
    if (args.length === 1 && (args[0] instanceof Vector2)) return this.$SetDimensionLinePosition0(...args);
    throw new ArgumentException("No matching Angular2LineDimension.SetDimensionLinePosition overload. Consult native-port-manifest.json.");
  }
}
