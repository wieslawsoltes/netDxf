// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { Block } from './../Blocks/Block.js';
import { AlignedDimension } from './AlignedDimension.js';
import { Angular2LineDimension } from './Angular2LineDimension.js';
import { Angular3PointDimension } from './Angular3PointDimension.js';
import { Arc } from './Arc.js';
import { ArcLengthDimension } from './ArcLengthDimension.js';
import { DiametricDimension } from './DiametricDimension.js';
import { Dimension } from './Dimension.js';
import { Insert } from './Insert.js';
import { Line } from './Line.js';
import { LinearDimension } from './LinearDimension.js';
import { MText } from './MText.js';
import { OrdinateDimension } from './OrdinateDimension.js';
import { Point } from './Point.js';
import { RadialDimension } from './RadialDimension.js';
import { Solid } from './Solid.js';
import { MathHelper } from './../MathHelper.js';
import { DimensionStyle } from './../Tables/DimensionStyle.js';
import { Layer } from './../Tables/Layer.js';
import { AngleUnitFormat } from './../Units/AngleUnitFormat.js';
import { LinearUnitFormat } from './../Units/LinearUnitFormat.js';
import { UnitStyleFormat } from './../Units/UnitStyleFormat.js';
import { Vector2 } from './../Vector2.js';
import { Vector3 } from './../Vector3.js';
import { DimensionValue, Cloneable, RequireReference, StringEquals, StringReplace, StringSubstring, DimensionCulture } from '../../runtime/DimensionRuntime.js';

export class DimensionBlock {
  // C# backing state is prefixed with $; public members retain their original names.
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching DimensionBlock constructor. Use DimensionBlock.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static FormatDimensionText(measure, dimType, userText, style, owner) {
    let texts = new List();
    if ((userText === " "))
    {
      texts.Add("");
      return texts;
    }
    let dimText = "";
    let unitFormat = Init(UnitStyleFormat.$create0(), $new => { $new.LinearDecimalPlaces = RequireReference(style).LengthPrecision; $new.AngularDecimalPlaces = ((RequireReference(style).AngularPrecision === (-1)) ? RequireReference(style).LengthPrecision : RequireReference(style).AngularPrecision); $new.DecimalSeparator = String(RequireReference(style).DecimalSeparator); $new.FractionHeightScale = RequireReference(style).TextFractionHeightScale; $new.FractionType = RequireReference(style).FractionType; $new.SuppressLinearLeadingZeros = RequireReference(style).SuppressLinearLeadingZeros; $new.SuppressLinearTrailingZeros = RequireReference(style).SuppressLinearTrailingZeros; $new.SuppressAngularLeadingZeros = RequireReference(style).SuppressAngularLeadingZeros; $new.SuppressAngularTrailingZeros = RequireReference(style).SuppressAngularTrailingZeros; $new.SuppressZeroFeet = RequireReference(style).SuppressZeroFeet; $new.SuppressZeroInches = RequireReference(style).SuppressZeroInches; });
    if (((dimType === 2) || (dimType === 5)))
    {
      { const $switch0 = RequireReference(style).DimAngularUnits;
      switch (true) {
        case $switch0 === 0:
          dimText = AngleUnitFormat.ToDecimal(measure, unitFormat);
          break;
        case $switch0 === 1:
          dimText = AngleUnitFormat.ToDegreesMinutesSeconds(measure, unitFormat);
          break;
        case $switch0 === 2:
          dimText = AngleUnitFormat.ToGradians(measure, unitFormat);
          break;
        case $switch0 === 3:
          dimText = AngleUnitFormat.ToRadians(measure, unitFormat);
          break;
        case $switch0 === 4:
          dimText = AngleUnitFormat.ToDecimal(measure, unitFormat);
          break;
      } }
    }
    else
    {
      let scale = DotNetMath.Abs(RequireReference(style).DimScaleLinear);
      if ((owner !== null))
      {
        let layout = RequireReference(RequireReference(owner).Record).Layout;
        if ((layout !== null))
        {
          if (((RequireReference(style).DimScaleLinear < 0) && (!RequireReference(layout).IsPaperSpace)))
          {
            scale = 1;
          }
        }
      }
      if ((RequireReference(style).DimRoundoff > 0))
      {
        measure = MathHelper.RoundToNearest(MultiplyDouble(measure, scale), RequireReference(style).DimRoundoff);
      }
      else
      {
        measure *= scale;
      }
      { const $switch1 = RequireReference(style).DimLengthUnits;
      switch (true) {
        case $switch1 === 4:
          dimText = LinearUnitFormat.ToArchitectural(measure, unitFormat);
          break;
        case $switch1 === 2:
          dimText = LinearUnitFormat.ToDecimal(measure, unitFormat);
          break;
        case $switch1 === 3:
          dimText = LinearUnitFormat.ToEngineering(measure, unitFormat);
          break;
        case $switch1 === 5:
          dimText = LinearUnitFormat.ToFractional(measure, unitFormat);
          break;
        case $switch1 === 1:
          dimText = LinearUnitFormat.ToScientific(measure, unitFormat);
          break;
        case $switch1 === 6:
          RequireReference(unitFormat).LinearDecimalPlaces = Int16(DimensionCulture().NumberDecimalDigits);
          RequireReference(unitFormat).DecimalSeparator = DimensionCulture().NumberDecimalSeparator;
          dimText = LinearUnitFormat.ToDecimal(MultiplyDouble(measure, RequireReference(style).DimScaleLinear), unitFormat);
          break;
      } }
    }
    let prefix = "";
    if ((dimType === 3))
    {
      prefix = (NativeString.IsNullOrEmpty(RequireReference(style).DimPrefix) ? "\u00D8" : RequireReference(style).DimPrefix);
    }
    if ((dimType === 4))
    {
      prefix = (NativeString.IsNullOrEmpty(RequireReference(style).DimPrefix) ? "R" : RequireReference(style).DimPrefix);
    }
    dimText = Format("{0}{1}{2}", prefix, dimText, RequireReference(style).DimSuffix);
    if ((!NativeString.IsNullOrEmpty(userText)))
    {
      let splitPos = 0;
      for (let i = 0; (i < userText.length); (i++))
      {
        if ((GetElement(userText, i) === "\\"))
        {
          let j = (i + 1);
          if ((j >= userText.length))
          {
            break;
          }
          if ((GetElement(userText, j) === "X"))
          {
            splitPos = i;
            break;
          }
        }
      }
      if ((splitPos > 0))
      {
        texts.Add(StringReplace(StringSubstring(userText, 0, splitPos), "\u003C\u003E", dimText));
        texts.Add(StringReplace(StringSubstring(userText, (splitPos + 2), (userText.length - ((splitPos + 2)))), "\u003C\u003E", dimText));
      }
      else
      {
        texts.Add(StringReplace(userText, "\u003C\u003E", dimText));
      }
    }
    else
    {
      texts.Add(dimText);
    }
    return texts;
  }
  static DimensionLine(start, end, rotation, style) {
    let ext1 = MultiplyDouble(RequireReference(style).ArrowSize, RequireReference(style).DimScaleOverall);
    let ext2 = MultiplyDouble((-RequireReference(style).ArrowSize), RequireReference(style).DimScaleOverall);
    let block = null;
    block = RequireReference(style).DimArrow1;
    if ((block !== null))
    {
      if ((((StringEquals(RequireReference(block).Name, "_OBLIQUE", 5) || StringEquals(RequireReference(block).Name, "_ARCHTICK", 5)) || StringEquals(RequireReference(block).Name, "_INTEGRAL", 5)) || StringEquals(RequireReference(block).Name, "_NONE", 5)))
      {
        ext1 = MultiplyDouble((-RequireReference(style).DimLineExtend), RequireReference(style).DimScaleOverall);
      }
    }
    block = RequireReference(style).DimArrow2;
    if ((block !== null))
    {
      if ((((StringEquals(RequireReference(block).Name, "_OBLIQUE", 5) || StringEquals(RequireReference(block).Name, "_ARCHTICK", 5)) || StringEquals(RequireReference(block).Name, "_INTEGRAL", 5)) || StringEquals(RequireReference(block).Name, "_NONE", 5)))
      {
        ext2 = MultiplyDouble(RequireReference(style).DimLineExtend, RequireReference(style).DimScaleOverall);
      }
    }
    start = Vector2.Polar(start, ext1, rotation);
    end = Vector2.Polar(end, ext2, rotation);
    return Init(new Line(start, end), $new => { $new.Color = RequireReference(style).DimLineColor; $new.Linetype = RequireReference(style).DimLineLinetype; $new.Lineweight = RequireReference(style).DimLineLineweight; });
  }
  static DimensionArc(center, start, end, startAngle, endAngle, radius, style, e1, e2) {
    let ext1 = MultiplyDouble(RequireReference(style).ArrowSize, RequireReference(style).DimScaleOverall);
    let ext2 = MultiplyDouble((-RequireReference(style).ArrowSize), RequireReference(style).DimScaleOverall);
    e1.value = ext1;
    e2.value = ext2;
    let block = null;
    if ((radius < (2 * ext1)))
    {
      ext1 = 0;
      e1.value = 0;
      ext2 = 0;
      e2.value = 0;
    }
    block = RequireReference(style).DimArrow1;
    if ((block !== null))
    {
      if ((((StringEquals(RequireReference(block).Name, "_OBLIQUE", 5) || StringEquals(RequireReference(block).Name, "_ARCHTICK", 5)) || StringEquals(RequireReference(block).Name, "_INTEGRAL", 5)) || StringEquals(RequireReference(block).Name, "_NONE", 5)))
      {
        ext1 = 0;
        e1.value = 0;
      }
    }
    block = RequireReference(style).DimArrow2;
    if ((block !== null))
    {
      if ((((StringEquals(RequireReference(block).Name, "_OBLIQUE", 5) || StringEquals(RequireReference(block).Name, "_ARCHTICK", 5)) || StringEquals(RequireReference(block).Name, "_INTEGRAL", 5)) || StringEquals(RequireReference(block).Name, "_NONE", 5)))
      {
        ext2 = 0;
        e2.value = 0;
      }
    }
    start = Vector2.Polar(start, ext1, (startAngle + 1.5707963267948966));
    end = Vector2.Polar(end, ext2, (endAngle + 1.5707963267948966));
    return Init(new Arc(center, radius, (Vector2.$Angle1(center, start) * 57.29577951308232), (Vector2.$Angle1(center, end) * 57.29577951308232)), $new => { $new.Color = RequireReference(style).DimLineColor; $new.Linetype = RequireReference(style).DimLineLinetype; $new.Lineweight = RequireReference(style).DimLineLineweight; });
  }
  static DimensionRadialLine(start, end, rotation, reversed, style) {
    let ext = MultiplyDouble((-RequireReference(style).ArrowSize), RequireReference(style).DimScaleOverall);
    let block = null;
    block = RequireReference(style).DimArrow2;
    if ((block !== null))
    {
      if ((((StringEquals(RequireReference(block).Name, "_OBLIQUE", 5) || StringEquals(RequireReference(block).Name, "_ARCHTICK", 5)) || StringEquals(RequireReference(block).Name, "_INTEGRAL", 5)) || StringEquals(RequireReference(block).Name, "_NONE", 5)))
      ext = MultiplyDouble(RequireReference(style).DimLineExtend, RequireReference(style).DimScaleOverall);
    }
    end = Vector2.Polar(end, MultiplyDouble(reversed, ext), rotation);
    return Init(new Line(start, end), $new => { $new.Color = RequireReference(style).DimLineColor; $new.Linetype = RequireReference(style).DimLineLinetype; $new.Lineweight = RequireReference(style).DimLineLineweight; });
  }
  static ExtensionLine(start, end, style, linetype) {
    return Init(new Line(start, end), $new => { $new.Color = RequireReference(style).ExtLineColor; $new.Linetype = linetype; $new.Lineweight = RequireReference(style).ExtLineLineweight; });
  }
  static StartArrowHead(position, rotation, style) {
    let block = RequireReference(style).DimArrow1;
    if ((block === null))
    {
      let arrowRef = Vector2.Polar(position, MultiplyDouble((-RequireReference(style).ArrowSize), RequireReference(style).DimScaleOverall), rotation);
      let arrow = Init(new Solid(position, Vector2.Polar(arrowRef, MultiplyDouble((-((RequireReference(style).ArrowSize / 6))), RequireReference(style).DimScaleOverall), (rotation + 1.5707963267948966)), Vector2.Polar(arrowRef, MultiplyDouble(((RequireReference(style).ArrowSize / 6)), RequireReference(style).DimScaleOverall), (rotation + 1.5707963267948966))), $new => { $new.Color = RequireReference(style).DimLineColor; });
      return arrow;
    }
    else
    {
      let arrow = Init(new Insert(block, position), $new => { $new.Color = RequireReference(style).DimLineColor; $new.Scale = Vector3.$create0(MultiplyDouble(RequireReference(style).ArrowSize, RequireReference(style).DimScaleOverall)); $new.Rotation = (rotation * 57.29577951308232); $new.Lineweight = RequireReference(style).DimLineLineweight; });
      return arrow;
    }
  }
  static EndArrowHead(position, rotation, style) {
    let block = RequireReference(style).DimArrow2;
    if ((block === null))
    {
      let arrowRef = Vector2.Polar(position, MultiplyDouble((-RequireReference(style).ArrowSize), RequireReference(style).DimScaleOverall), rotation);
      let arrow = Init(new Solid(position, Vector2.Polar(arrowRef, MultiplyDouble((-((RequireReference(style).ArrowSize / 6))), RequireReference(style).DimScaleOverall), (rotation + 1.5707963267948966)), Vector2.Polar(arrowRef, MultiplyDouble(((RequireReference(style).ArrowSize / 6)), RequireReference(style).DimScaleOverall), (rotation + 1.5707963267948966))), $new => { $new.Color = RequireReference(style).DimLineColor; });
      return arrow;
    }
    else
    {
      let arrow = Init(new Insert(block, position), $new => { $new.Color = RequireReference(style).DimLineColor; $new.Scale = Vector3.$create0(MultiplyDouble(RequireReference(style).ArrowSize, RequireReference(style).DimScaleOverall)); $new.Rotation = (rotation * 57.29577951308232); $new.Lineweight = RequireReference(style).DimLineLineweight; });
      return arrow;
    }
  }
  static DimensionText(position, attachmentPoint, rotation, text, style) {
    if (NativeString.IsNullOrEmpty(text))
    {
      return null;
    }
    let mText = Init(new MText(text, position, MultiplyDouble(RequireReference(style).TextHeight, RequireReference(style).DimScaleOverall), 0, RequireReference(style).TextStyle), $new => { $new.Color = RequireReference(style).TextColor; $new.AttachmentPoint = attachmentPoint; $new.Rotation = (rotation * 57.29577951308232); });
    return mText;
  }
  static CenterCross(center, radius, style) {
    let lines = new List();
    if (MathHelper.$IsZero0(RequireReference(style).CenterMarkSize))
    {
      return lines;
    }
    let c1 = new Vector2();
    let c2 = new Vector2();
    let dist = DotNetMath.Abs(MultiplyDouble(RequireReference(style).CenterMarkSize, RequireReference(style).DimScaleOverall));
    c1 = Vector2.op_Addition(Vector2.$create1(0, (-dist)), center);
    c2 = Vector2.op_Addition(Vector2.$create1(0, dist), center);
    lines.Add(Init(new Line(c1, c2), $new => { $new.Color = RequireReference(style).ExtLineColor; $new.Lineweight = RequireReference(style).ExtLineLineweight; }));
    c1 = Vector2.op_Addition(Vector2.$create1((-dist), 0), center);
    c2 = Vector2.op_Addition(Vector2.$create1(dist, 0), center);
    lines.Add(Init(new Line(c1, c2), $new => { $new.Color = RequireReference(style).ExtLineColor; $new.Lineweight = RequireReference(style).ExtLineLineweight; }));
    if ((RequireReference(style).CenterMarkSize < 0))
    {
      c1 = Vector2.op_Addition(Vector2.$create1((2 * dist), 0), center);
      c2 = Vector2.op_Addition(Vector2.$create1((radius + dist), 0), center);
      lines.Add(Init(new Line(c1, c2), $new => { $new.Color = RequireReference(style).ExtLineColor; $new.Lineweight = RequireReference(style).ExtLineLineweight; }));
      c1 = Vector2.op_Addition(Vector2.$create1(((-2) * dist), 0), center);
      c2 = Vector2.op_Addition(Vector2.$create1(((-radius) - dist), 0), center);
      lines.Add(Init(new Line(c1, c2), $new => { $new.Color = RequireReference(style).ExtLineColor; $new.Lineweight = RequireReference(style).ExtLineLineweight; }));
      c1 = Vector2.op_Addition(Vector2.$create1(0, (2 * dist)), center);
      c2 = Vector2.op_Addition(Vector2.$create1(0, (radius + dist)), center);
      lines.Add(Init(new Line(c1, c2), $new => { $new.Color = RequireReference(style).ExtLineColor; $new.Lineweight = RequireReference(style).ExtLineLineweight; }));
      c1 = Vector2.op_Addition(Vector2.$create1(0, ((-2) * dist)), center);
      c2 = Vector2.op_Addition(Vector2.$create1(0, ((-radius) - dist)), center);
      lines.Add(Init(new Line(c1, c2), $new => { $new.Color = RequireReference(style).ExtLineColor; $new.Lineweight = RequireReference(style).ExtLineLineweight; }));
    }
    return lines;
  }
  static BuildDimensionStyleOverride(dim) {
    if ((RequireReference(RequireReference(dim).StyleOverrides).Count === 0))
    {
      return RequireReference(dim).Style;
    }
    let copy = Init(new DimensionStyle(RequireReference(RequireReference(dim).Style).Name), $new => { $new.DimLineColor = RequireReference(RequireReference(dim).Style).DimLineColor; $new.DimLineLinetype = RequireReference(RequireReference(dim).Style).DimLineLinetype; $new.DimLineLineweight = RequireReference(RequireReference(dim).Style).DimLineLineweight; $new.DimLine1Off = RequireReference(RequireReference(dim).Style).DimLine1Off; $new.DimLine2Off = RequireReference(RequireReference(dim).Style).DimLine2Off; $new.DimBaselineSpacing = RequireReference(RequireReference(dim).Style).DimBaselineSpacing; $new.DimLineExtend = RequireReference(RequireReference(dim).Style).DimLineExtend; $new.ExtLineColor = RequireReference(RequireReference(dim).Style).ExtLineColor; $new.ExtLine1Linetype = RequireReference(RequireReference(dim).Style).ExtLine1Linetype; $new.ExtLine2Linetype = RequireReference(RequireReference(dim).Style).ExtLine2Linetype; $new.ExtLineLineweight = RequireReference(RequireReference(dim).Style).ExtLineLineweight; $new.ExtLine1Off = RequireReference(RequireReference(dim).Style).ExtLine1Off; $new.ExtLine2Off = RequireReference(RequireReference(dim).Style).ExtLine2Off; $new.ExtLineOffset = RequireReference(RequireReference(dim).Style).ExtLineOffset; $new.ExtLineExtend = RequireReference(RequireReference(dim).Style).ExtLineExtend; $new.ArrowSize = RequireReference(RequireReference(dim).Style).ArrowSize; $new.CenterMarkSize = RequireReference(RequireReference(dim).Style).CenterMarkSize; $new.TextStyle = RequireReference(RequireReference(dim).Style).TextStyle; $new.TextColor = RequireReference(RequireReference(dim).Style).TextColor; $new.TextHeight = RequireReference(RequireReference(dim).Style).TextHeight; $new.TextOffset = RequireReference(RequireReference(dim).Style).TextOffset; $new.TextFractionHeightScale = RequireReference(RequireReference(dim).Style).TextFractionHeightScale; $new.AngularPrecision = RequireReference(RequireReference(dim).Style).AngularPrecision; $new.LengthPrecision = RequireReference(RequireReference(dim).Style).LengthPrecision; $new.DimPrefix = RequireReference(RequireReference(dim).Style).DimPrefix; $new.DimSuffix = RequireReference(RequireReference(dim).Style).DimSuffix; $new.DecimalSeparator = RequireReference(RequireReference(dim).Style).DecimalSeparator; $new.DimScaleLinear = RequireReference(RequireReference(dim).Style).DimScaleLinear; $new.DimLengthUnits = RequireReference(RequireReference(dim).Style).DimLengthUnits; $new.DimAngularUnits = RequireReference(RequireReference(dim).Style).DimAngularUnits; $new.FractionType = RequireReference(RequireReference(dim).Style).FractionType; $new.SuppressLinearLeadingZeros = RequireReference(RequireReference(dim).Style).SuppressLinearLeadingZeros; $new.SuppressLinearTrailingZeros = RequireReference(RequireReference(dim).Style).SuppressLinearTrailingZeros; $new.SuppressAngularLeadingZeros = RequireReference(RequireReference(dim).Style).SuppressAngularLeadingZeros; $new.SuppressAngularTrailingZeros = RequireReference(RequireReference(dim).Style).SuppressAngularTrailingZeros; $new.SuppressZeroFeet = RequireReference(RequireReference(dim).Style).SuppressZeroFeet; $new.SuppressZeroInches = RequireReference(RequireReference(dim).Style).SuppressZeroInches; $new.DimRoundoff = RequireReference(RequireReference(dim).Style).DimRoundoff; $new.LeaderArrow = RequireReference(RequireReference(dim).Style).LeaderArrow; $new.DimArrow1 = RequireReference(RequireReference(dim).Style).DimArrow1; $new.DimArrow2 = RequireReference(RequireReference(dim).Style).DimArrow2; });
    for (const $item2 of RequireReference(RequireReference(dim).StyleOverrides).Values) {
      let styleOverride = $item2;
      { const $switch3 = RequireReference(styleOverride).Type;
      switch (true) {
        case $switch3 === 0:
          RequireReference(copy).DimLineColor = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 1:
          RequireReference(copy).DimLineLinetype = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 2:
          RequireReference(copy).DimLineLineweight = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 3:
          RequireReference(copy).DimLine1Off = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 4:
          RequireReference(copy).DimLine2Off = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 5:
          RequireReference(copy).DimLineExtend = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 6:
          RequireReference(copy).ExtLineColor = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 7:
          RequireReference(copy).ExtLine1Linetype = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 8:
          RequireReference(copy).ExtLine2Linetype = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 9:
          RequireReference(copy).ExtLineLineweight = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 10:
          RequireReference(copy).ExtLine1Off = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 11:
          RequireReference(copy).ExtLine2Off = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 12:
          RequireReference(copy).ExtLineOffset = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 13:
          RequireReference(copy).ExtLineExtend = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 19:
          RequireReference(copy).ArrowSize = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 20:
          RequireReference(copy).CenterMarkSize = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 18:
          RequireReference(copy).LeaderArrow = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 16:
          RequireReference(copy).DimArrow1 = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 17:
          RequireReference(copy).DimArrow2 = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 21:
          RequireReference(copy).TextStyle = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 22:
          RequireReference(copy).TextColor = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 24:
          RequireReference(copy).TextHeight = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 29:
          RequireReference(copy).TextOffset = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 31:
          RequireReference(copy).TextFractionHeightScale = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 34:
          RequireReference(copy).DimScaleOverall = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 38:
          RequireReference(copy).AngularPrecision = Int16(DimensionValue(RequireReference(styleOverride).Value));
          break;
        case $switch3 === 39:
          RequireReference(copy).LengthPrecision = Int16(DimensionValue(RequireReference(styleOverride).Value));
          break;
        case $switch3 === 40:
          RequireReference(copy).DimPrefix = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 41:
          RequireReference(copy).DimSuffix = RequireReference(styleOverride).Value;
          break;
        case $switch3 === 42:
          RequireReference(copy).DecimalSeparator = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 43:
          RequireReference(copy).DimScaleLinear = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 44:
          RequireReference(copy).DimLengthUnits = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 45:
          RequireReference(copy).DimAngularUnits = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 46:
          RequireReference(copy).FractionType = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 47:
          RequireReference(copy).SuppressLinearLeadingZeros = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 48:
          RequireReference(copy).SuppressLinearTrailingZeros = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 49:
          RequireReference(copy).SuppressAngularLeadingZeros = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 50:
          RequireReference(copy).SuppressAngularTrailingZeros = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 51:
          RequireReference(copy).SuppressZeroFeet = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 52:
          RequireReference(copy).SuppressZeroInches = DimensionValue(RequireReference(styleOverride).Value);
          break;
        case $switch3 === 53:
          RequireReference(copy).DimRoundoff = DimensionValue(RequireReference(styleOverride).Value);
          break;
      } }
    }
    return copy;
  }
  static $Build0(dim) {
    return DimensionBlock.$Build1(dim, "DimBlock");
  }
  static $Build1(dim, name) {
    let block = null;
    { const $switch4 = RequireReference(dim).DimensionType;
    switch (true) {
      case $switch4 === 0:
        block = DimensionBlock.$Build3(dim, name);
        break;
      case $switch4 === 1:
        block = DimensionBlock.$Build2(dim, name);
        break;
      case $switch4 === 2:
        block = DimensionBlock.$Build4(dim, name);
        break;
      case $switch4 === 5:
        block = DimensionBlock.$Build5(dim, name);
        break;
      case $switch4 === 3:
        block = DimensionBlock.$Build6(dim, name);
        break;
      case $switch4 === 4:
        block = DimensionBlock.$Build7(dim, name);
        break;
      case $switch4 === 6:
        block = DimensionBlock.$Build8(dim, name);
        break;
      case $switch4 === 7:
        block = DimensionBlock.$Build9(dim, name);
        break;
      default:
        block = null;
        break;
    } }
    return block;
  }
  static $Build2(dim, name) {
    let style = DimensionBlock.BuildDimensionStyleOverride(dim);
    let entities = new List();
    let measure = RequireReference(dim).Measurement;
    let ref1 = RequireReference(dim).FirstReferencePoint;
    let ref2 = RequireReference(dim).SecondReferencePoint;
    let vec = Vector2.Normalize(Vector2.Perpendicular(Vector2.op_Subtraction(ref2, ref1)));
    let dimRef1 = Vector2.op_Addition(ref1, Vector2.$op_Multiply1(RequireReference(dim).Offset, vec));
    let dimRef2 = Vector2.op_Addition(ref2, Vector2.$op_Multiply1(RequireReference(dim).Offset, vec));
    let refAngle = Vector2.$Angle1(ref1, ref2);
    let defPointLayer = Init(new Layer("Defpoints"), $new => { $new.Plot = false; });
    entities.Add(Init(new Point(ref1), $new => { $new.Layer = defPointLayer; }));
    entities.Add(Init(new Point(ref2), $new => { $new.Layer = defPointLayer; }));
    entities.Add(Init(new Point(dimRef2), $new => { $new.Layer = defPointLayer; }));
    if (((!RequireReference(style).DimLine1Off) && (!RequireReference(style).DimLine2Off)))
    {
      entities.Add(DimensionBlock.DimensionLine(dimRef1, dimRef2, refAngle, style));
      entities.Add(DimensionBlock.StartArrowHead(dimRef1, (refAngle + 3.141592653589793), style));
      entities.Add(DimensionBlock.EndArrowHead(dimRef2, refAngle, style));
    }
    let dimexo = MultiplyDouble(MultiplyDouble(DotNetMath.Sign(RequireReference(dim).Offset), RequireReference(style).ExtLineOffset), RequireReference(style).DimScaleOverall);
    let dimexe = MultiplyDouble(MultiplyDouble(DotNetMath.Sign(RequireReference(dim).Offset), RequireReference(style).ExtLineExtend), RequireReference(style).DimScaleOverall);
    if ((!RequireReference(style).ExtLine1Off))
    {
      entities.Add(DimensionBlock.ExtensionLine(Vector2.op_Addition(ref1, Vector2.$op_Multiply1(dimexo, vec)), Vector2.op_Addition(dimRef1, Vector2.$op_Multiply1(dimexe, vec)), style, RequireReference(style).ExtLine1Linetype));
    }
    if ((!RequireReference(style).ExtLine2Off))
    {
      entities.Add(DimensionBlock.ExtensionLine(Vector2.op_Addition(ref2, Vector2.$op_Multiply1(dimexo, vec)), Vector2.op_Addition(dimRef2, Vector2.$op_Multiply1(dimexe, vec)), style, RequireReference(style).ExtLine2Linetype));
    }
    let textRef = Vector2.MidPoint(dimRef1, dimRef2);
    let gap = MultiplyDouble(RequireReference(style).TextOffset, RequireReference(style).DimScaleOverall);
    let textRot = refAngle;
    if (((textRot > 1.5707963267948966) && (textRot <= 4.71238898038469)))
    {
      gap = (-gap);
      textRot += 3.141592653589793;
    }
    let texts = DimensionBlock.FormatDimensionText(measure, RequireReference(dim).DimensionType, RequireReference(dim).UserText, style, RequireReference(dim).Owner);
    let mText = DimensionBlock.DimensionText(Vector2.op_Addition(textRef, Vector2.$op_Multiply1(gap, vec)), 8, textRot, GetElement(texts, 0), style);
    if ((mText !== null))
    {
      entities.Add(mText);
    }
    if ((texts.length > 1))
    {
      let mText2 = DimensionBlock.DimensionText(Vector2.op_Subtraction(textRef, Vector2.$op_Multiply1(gap, vec)), 2, textRot, GetElement(texts, 1), style);
      if ((mText2 !== null))
      {
        entities.Add(mText2);
      }
    }
    RequireReference(dim).TextReferencePoint = Vector2.op_Addition(textRef, Vector2.$op_Multiply1(gap, vec));
    RequireReference(dim).TextPositionManuallySet = false;
    return Init(Block.CreateOverload("string,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.EntityObject\u003E,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.AttributeDefinition\u003E,bool", name, entities, null, false), $new => { $new.Flags = 1; });
  }
  static $Build3(dim, name) {
    let style = DimensionBlock.BuildDimensionStyleOverride(dim);
    let entities = new List();
    let measure = RequireReference(dim).Measurement;
    let dimRotation = (RequireReference(dim).Rotation * 0.017453292519943295);
    let ref1 = RequireReference(dim).FirstReferencePoint;
    let ref2 = RequireReference(dim).SecondReferencePoint;
    let vec = Vector2.Normalize(Vector2.Rotate(Vector2.UnitY, dimRotation));
    let cross = Vector2.CrossProduct(Vector2.op_Subtraction(ref2, ref1), vec);
    if ((cross < 0))
    {
      ([ref1, ref2] = [Copy(ref2), Copy(ref1)]);
    }
    let dimRef1 = Vector2.op_Addition(RequireReference(dim).DimLinePosition, Vector2.$op_Multiply1(measure, Vector2.Perpendicular(vec)));
    let dimRef2 = RequireReference(dim).DimLinePosition;
    let defPointLayer = Init(new Layer("Defpoints"), $new => { $new.Plot = false; });
    entities.Add(Init(new Point(ref1), $new => { $new.Layer = defPointLayer; }));
    entities.Add(Init(new Point(ref2), $new => { $new.Layer = defPointLayer; }));
    entities.Add(Init(new Point(dimRef1), $new => { $new.Layer = defPointLayer; }));
    if (((!RequireReference(style).DimLine1Off) && (!RequireReference(style).DimLine2Off)))
    {
      entities.Add(DimensionBlock.DimensionLine(dimRef1, dimRef2, dimRotation, style));
      entities.Add(DimensionBlock.StartArrowHead(dimRef1, (dimRotation + 3.141592653589793), style));
      entities.Add(DimensionBlock.EndArrowHead(dimRef2, dimRotation, style));
    }
    let dirRef1 = Vector2.Normalize(Vector2.op_Subtraction(dimRef1, ref1));
    let dirRef2 = Vector2.Normalize(Vector2.op_Subtraction(dimRef2, ref2));
    let dimexo = MultiplyDouble(RequireReference(style).ExtLineOffset, RequireReference(style).DimScaleOverall);
    let dimexe = MultiplyDouble(RequireReference(style).ExtLineExtend, RequireReference(style).DimScaleOverall);
    if ((!RequireReference(style).ExtLine1Off))
    {
      entities.Add(DimensionBlock.ExtensionLine(Vector2.op_Addition(ref1, Vector2.$op_Multiply1(dimexo, dirRef1)), Vector2.op_Addition(dimRef1, Vector2.$op_Multiply1(dimexe, dirRef1)), style, RequireReference(style).ExtLine1Linetype));
    }
    if ((!RequireReference(style).ExtLine2Off))
    {
      entities.Add(DimensionBlock.ExtensionLine(Vector2.op_Addition(ref2, Vector2.$op_Multiply1(dimexo, dirRef2)), Vector2.op_Addition(dimRef2, Vector2.$op_Multiply1(dimexe, dirRef2)), style, RequireReference(style).ExtLine2Linetype));
    }
    let textRef = Vector2.MidPoint(dimRef1, dimRef2);
    let gap = MultiplyDouble(RequireReference(style).TextOffset, RequireReference(style).DimScaleOverall);
    let textRot = dimRotation;
    if (((textRot > 1.5707963267948966) && (textRot <= 4.71238898038469)))
    {
      gap = (-gap);
      textRot += 3.141592653589793;
    }
    let texts = DimensionBlock.FormatDimensionText(measure, RequireReference(dim).DimensionType, RequireReference(dim).UserText, style, RequireReference(dim).Owner);
    let mText = DimensionBlock.DimensionText(Vector2.op_Addition(textRef, Vector2.$op_Multiply1(gap, vec)), 8, textRot, GetElement(texts, 0), style);
    if ((mText !== null))
    {
      entities.Add(mText);
    }
    if ((texts.length > 1))
    {
      let mText2 = DimensionBlock.DimensionText(Vector2.op_Subtraction(textRef, Vector2.$op_Multiply1(gap, vec)), 2, textRot, GetElement(texts, 1), style);
      if ((mText2 !== null))
      {
        entities.Add(mText2);
      }
    }
    RequireReference(dim).TextReferencePoint = Vector2.op_Addition(textRef, Vector2.$op_Multiply1(gap, vec));
    RequireReference(dim).TextPositionManuallySet = false;
    return Init(Block.CreateOverload("string,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.EntityObject\u003E,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.AttributeDefinition\u003E,bool", name, entities, null, false), $new => { $new.Flags = 1; });
  }
  static $Build4(dim, name) {
    let ext1, ext2;
    let offset = (MathHelper.$IsZero0(RequireReference(dim).Offset) ? MathHelper.Epsilon : RequireReference(dim).Offset);
    let measure = RequireReference(dim).Measurement;
    let style = DimensionBlock.BuildDimensionStyleOverride(dim);
    let entities = new List();
    let ref1Start = RequireReference(dim).StartFirstLine;
    let ref1End = RequireReference(dim).EndFirstLine;
    let ref2Start = RequireReference(dim).StartSecondLine;
    let ref2End = RequireReference(dim).EndSecondLine;
    let center = RequireReference(dim).CenterPoint;
    let startAngle = Vector2.$Angle1(ref1Start, ref1End);
    let endAngle = Vector2.$Angle1(ref2Start, ref2End);
    let midRot = (startAngle + ((measure * 0.017453292519943295) * 0.5));
    let dimRef1 = Vector2.Polar(center, offset, startAngle);
    let dimRef2 = Vector2.Polar(center, offset, endAngle);
    let midDim = RequireReference(dim).ArcDefinitionPoint;
    let defPoints = Init(new Layer("Defpoints"), $new => { $new.Plot = false; });
    entities.Add(Init(new Point(ref1Start), $new => { $new.Layer = defPoints; }));
    entities.Add(Init(new Point(ref1End), $new => { $new.Layer = defPoints; }));
    entities.Add(Init(new Point(ref2Start), $new => { $new.Layer = defPoints; }));
    entities.Add(Init(new Point(ref2End), $new => { $new.Layer = defPoints; }));
    if (((!RequireReference(style).DimLine1Off) && (!RequireReference(style).DimLine2Off)))
    {
      let arc = DimensionBlock.DimensionArc(center, dimRef1, dimRef2, startAngle, endAngle, offset, style, { get value() { return ext1; }, set value(v) { ext1 = v; } }, { get value() { return ext2; }, set value(v) { ext2 = v; } });
      if ((!MathHelper.$IsZero0(measure)))
      {
        entities.Add(arc);
      }
      let angle1 = DotNetMath.Asin(((ext1 * 0.5) / offset));
      let angle2 = DotNetMath.Asin(((ext2 * 0.5) / offset));
      entities.Add(DimensionBlock.StartArrowHead(dimRef1, ((angle1 + startAngle) - 1.5707963267948966), style));
      entities.Add(DimensionBlock.EndArrowHead(dimRef2, ((angle2 + endAngle) + 1.5707963267948966), style));
    }
    let dimexo = MultiplyDouble(RequireReference(style).ExtLineOffset, RequireReference(style).DimScaleOverall);
    let dimexe = MultiplyDouble(RequireReference(style).ExtLineExtend, RequireReference(style).DimScaleOverall);
    let t = 0;
    t = MathHelper.$PointInSegment1(dimRef1, ref1Start, ref1End);
    if (((!RequireReference(style).ExtLine1Off) && (t !== 0)))
    {
      let s = Vector2.Polar(((t < 0) ? ref1Start : ref1End), MultiplyDouble(t, dimexo), startAngle);
      entities.Add(DimensionBlock.ExtensionLine(s, Vector2.Polar(dimRef1, MultiplyDouble(t, dimexe), startAngle), style, RequireReference(style).ExtLine1Linetype));
    }
    t = MathHelper.$PointInSegment1(dimRef2, ref2Start, ref2End);
    if (((!RequireReference(style).ExtLine2Off) && (t !== 0)))
    {
      let s = Vector2.Polar(((t < 0) ? ref2Start : ref2End), MultiplyDouble(t, dimexo), endAngle);
      entities.Add(DimensionBlock.ExtensionLine(s, Vector2.Polar(dimRef2, MultiplyDouble(t, dimexe), endAngle), style, RequireReference(style).ExtLine1Linetype));
    }
    let textRot = (midRot - 1.5707963267948966);
    let gap = MultiplyDouble(RequireReference(style).TextOffset, RequireReference(style).DimScaleOverall);
    if (((textRot > 1.5707963267948966) && (textRot <= 4.71238898038469)))
    {
      textRot += 3.141592653589793;
      gap *= (-1);
    }
    let texts = DimensionBlock.FormatDimensionText(measure, RequireReference(dim).DimensionType, RequireReference(dim).UserText, style, RequireReference(dim).Owner);
    let dimText = null;
    let position = new Vector2();
    let attachmentPoint = 0;
    if ((texts.length > 1))
    {
      position = Copy(midDim);
      dimText = ((GetElement(texts, 0) + "\\P") + GetElement(texts, 1));
      attachmentPoint = 5;
    }
    else
    {
      position = Vector2.Polar(midDim, gap, midRot);
      dimText = GetElement(texts, 0);
      attachmentPoint = 8;
    }
    let mText = DimensionBlock.DimensionText(position, attachmentPoint, textRot, dimText, style);
    if ((mText !== null))
    {
      entities.Add(mText);
    }
    RequireReference(dim).TextReferencePoint = Copy(position);
    RequireReference(dim).TextPositionManuallySet = false;
    return Init(Block.CreateOverload("string,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.EntityObject\u003E,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.AttributeDefinition\u003E,bool", name, entities, null, false), $new => { $new.Flags = 1; });
  }
  static $Build5(dim, name) {
    let ext1, ext2;
    let offset = (MathHelper.$IsZero0(RequireReference(dim).Offset) ? MathHelper.Epsilon : DotNetMath.Abs(RequireReference(dim).Offset));
    let measure = RequireReference(dim).Measurement;
    let style = DimensionBlock.BuildDimensionStyleOverride(dim);
    let entities = new List();
    let refCenter = RequireReference(dim).CenterPoint;
    let ref1 = RequireReference(dim).StartPoint;
    let ref2 = RequireReference(dim).EndPoint;
    if ((RequireReference(dim).Offset < 0))
    {
      ([ref1, ref2] = [Copy(ref2), Copy(ref1)]);
    }
    let startAngle = Vector2.$Angle1(refCenter, ref1);
    let endAngle = Vector2.$Angle1(refCenter, ref2);
    let midRot = (startAngle + ((0.5 * measure) * 0.017453292519943295));
    let dimRef1 = Vector2.Polar(refCenter, offset, startAngle);
    let dimRef2 = Vector2.Polar(refCenter, offset, endAngle);
    let midDim = RequireReference(dim).ArcDefinitionPoint;
    let defPoints = Init(new Layer("Defpoints"), $new => { $new.Plot = false; });
    entities.Add(Init(new Point(ref1), $new => { $new.Layer = defPoints; }));
    entities.Add(Init(new Point(ref2), $new => { $new.Layer = defPoints; }));
    entities.Add(Init(new Point(refCenter), $new => { $new.Layer = defPoints; }));
    if (((!RequireReference(style).DimLine1Off) && (!RequireReference(style).DimLine2Off)))
    {
      let arc = DimensionBlock.DimensionArc(refCenter, dimRef1, dimRef2, startAngle, endAngle, offset, style, { get value() { return ext1; }, set value(v) { ext1 = v; } }, { get value() { return ext2; }, set value(v) { ext2 = v; } });
      if ((!MathHelper.$IsZero0(measure)))
      {
        entities.Add(arc);
      }
      let angle1 = DotNetMath.Asin(((ext1 * 0.5) / offset));
      let angle2 = DotNetMath.Asin(((ext2 * 0.5) / offset));
      entities.Add(DimensionBlock.StartArrowHead(dimRef1, ((angle1 + startAngle) - 1.5707963267948966), style));
      entities.Add(DimensionBlock.EndArrowHead(dimRef2, ((angle2 + endAngle) + 1.5707963267948966), style));
    }
    let refAngle = 0;
    if ((Vector2.Distance(refCenter, ref1) > Vector2.Distance(refCenter, dimRef1)))
    {
      refAngle = 3.141592653589793;
    }
    let dimexo = MultiplyDouble(RequireReference(style).ExtLineOffset, RequireReference(style).DimScaleOverall);
    let dimexe = MultiplyDouble(RequireReference(style).ExtLineExtend, RequireReference(style).DimScaleOverall);
    if ((!RequireReference(style).ExtLine1Off))
    {
      entities.Add(DimensionBlock.ExtensionLine(Vector2.Polar(ref1, dimexo, (startAngle + refAngle)), Vector2.Polar(dimRef1, dimexe, (startAngle + refAngle)), style, RequireReference(style).ExtLine1Linetype));
    }
    if ((!RequireReference(style).ExtLine2Off))
    {
      entities.Add(DimensionBlock.ExtensionLine(Vector2.Polar(ref2, dimexo, (endAngle + refAngle)), Vector2.Polar(dimRef2, dimexe, (endAngle + refAngle)), style, RequireReference(style).ExtLine1Linetype));
    }
    let textRot = (midRot - 1.5707963267948966);
    let gap = MultiplyDouble(RequireReference(style).TextOffset, RequireReference(style).DimScaleOverall);
    if (((textRot > 1.5707963267948966) && (textRot <= 4.71238898038469)))
    {
      textRot += 3.141592653589793;
      gap *= (-1);
    }
    let texts = DimensionBlock.FormatDimensionText(measure, RequireReference(dim).DimensionType, RequireReference(dim).UserText, style, RequireReference(dim).Owner);
    let dimText = null;
    let position = new Vector2();
    let attachmentPoint = 0;
    if ((texts.length > 1))
    {
      position = Copy(midDim);
      dimText = ((GetElement(texts, 0) + "\\P") + GetElement(texts, 1));
      attachmentPoint = 5;
    }
    else
    {
      position = Vector2.Polar(midDim, gap, midRot);
      dimText = GetElement(texts, 0);
      attachmentPoint = 8;
    }
    let mText = DimensionBlock.DimensionText(position, attachmentPoint, textRot, dimText, style);
    if ((mText !== null))
    {
      entities.Add(mText);
    }
    RequireReference(dim).TextReferencePoint = Copy(position);
    RequireReference(dim).TextPositionManuallySet = false;
    return Init(Block.CreateOverload("string,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.EntityObject\u003E,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.AttributeDefinition\u003E,bool", name, entities, null, false), $new => { $new.Flags = 1; });
  }
  static $Build6(dim, name) {
    let measure = RequireReference(dim).Measurement;
    let offset = Vector2.Distance(RequireReference(dim).CenterPoint, RequireReference(dim).TextReferencePoint);
    let radius = (measure * 0.5);
    let style = DimensionBlock.BuildDimensionStyleOverride(dim);
    let entities = new List();
    let centerRef = RequireReference(dim).CenterPoint;
    let ref1 = RequireReference(dim).ReferencePoint;
    let defPoint = RequireReference(dim).DefinitionPoint;
    let angleRef = Vector2.$Angle1(centerRef, ref1);
    let inside = 0;
    let minOffset = MultiplyDouble((((2 * RequireReference(style).ArrowSize) + RequireReference(style).TextOffset)), RequireReference(style).DimScaleOverall);
    if (((offset >= radius) && (offset <= (radius + minOffset))))
    {
      offset = (radius + minOffset);
      inside = (-1);
    }
    else
    if (((offset >= (radius - minOffset)) && (offset <= radius)))
    {
      offset = (radius - minOffset);
      inside = 1;
    }
    else
    if ((offset > radius))
    {
      inside = (-1);
    }
    else
    {
      inside = 1;
    }
    let dimRef = Vector2.Polar(centerRef, (offset - MultiplyDouble(RequireReference(style).TextOffset, RequireReference(style).DimScaleOverall)), angleRef);
    let defPoints = Init(new Layer("Defpoints"), $new => { $new.Plot = false; });
    entities.Add(Init(new Point(ref1), $new => { $new.Layer = defPoints; }));
    if (((!RequireReference(style).DimLine1Off) && (!RequireReference(style).DimLine2Off)))
    {
      if ((inside > 0))
      {
        entities.Add(DimensionBlock.DimensionRadialLine(dimRef, ref1, angleRef, inside, style));
        entities.Add(DimensionBlock.EndArrowHead(ref1, angleRef, style));
      }
      else
      {
        entities.Add(Init(new Line(defPoint, ref1), $new => { $new.Color = RequireReference(style).DimLineColor; $new.Linetype = RequireReference(style).DimLineLinetype; $new.Lineweight = RequireReference(style).DimLineLineweight; }));
        entities.Add(DimensionBlock.DimensionRadialLine(dimRef, ref1, angleRef, inside, style));
        entities.Add(DimensionBlock.EndArrowHead(ref1, (3.141592653589793 + angleRef), style));
        let dimRef2 = Vector2.Polar(centerRef, ((radius + minOffset) - MultiplyDouble(RequireReference(style).TextOffset, RequireReference(style).DimScaleOverall)), (3.141592653589793 + angleRef));
        entities.Add(DimensionBlock.DimensionRadialLine(dimRef2, defPoint, (3.141592653589793 + angleRef), inside, style));
        entities.Add(DimensionBlock.EndArrowHead(defPoint, angleRef, style));
      }
    }
    if ((!MathHelper.$IsZero0(RequireReference(style).CenterMarkSize)))
    {
      entities.AddRange(DimensionBlock.CenterCross(centerRef, radius, style));
    }
    let texts = DimensionBlock.FormatDimensionText(measure, RequireReference(dim).DimensionType, RequireReference(dim).UserText, style, RequireReference(dim).Owner);
    let dimText = null;
    if ((texts.length > 1))
    {
      dimText = ((GetElement(texts, 0) + "\\P") + GetElement(texts, 1));
    }
    else
    {
      dimText = GetElement(texts, 0);
    }
    let textRot = angleRef;
    let reverse = 1;
    if (((textRot > 1.5707963267948966) && (textRot <= 4.71238898038469)))
    {
      textRot += 3.141592653589793;
      reverse = (-1);
    }
    let textPos = Vector2.Polar(dimRef, MultiplyDouble(MultiplyDouble(((-reverse) * inside), RequireReference(style).TextOffset), RequireReference(style).DimScaleOverall), textRot);
    let attachmentPoint = (((reverse * inside) < 0) ? 4 : 6);
    let mText = DimensionBlock.DimensionText(textPos, attachmentPoint, textRot, dimText, style);
    if ((mText !== null))
    {
      entities.Add(mText);
    }
    RequireReference(dim).TextReferencePoint = Copy(textPos);
    RequireReference(dim).TextPositionManuallySet = false;
    return Init(Block.CreateOverload("string,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.EntityObject\u003E,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.AttributeDefinition\u003E,bool", name, entities, null, false), $new => { $new.Flags = 1; });
  }
  static $Build7(dim, name) {
    let offset = Vector2.Distance(RequireReference(dim).CenterPoint, RequireReference(dim).TextReferencePoint);
    let radius = RequireReference(dim).Measurement;
    let style = DimensionBlock.BuildDimensionStyleOverride(dim);
    let entities = new List();
    let centerRef = RequireReference(dim).CenterPoint;
    let ref1 = RequireReference(dim).ReferencePoint;
    let angleRef = Vector2.$Angle1(centerRef, ref1);
    let side = 0;
    let minOffset = MultiplyDouble((2 * RequireReference(style).ArrowSize), RequireReference(style).DimScaleOverall);
    if (((offset >= radius) && (offset <= (radius + minOffset))))
    {
      offset = (radius + minOffset);
      side = (-1);
    }
    else
    if (((offset >= (radius - minOffset)) && (offset <= radius)))
    {
      offset = (radius - minOffset);
      side = 1;
    }
    else
    if ((offset > radius))
    {
      side = (-1);
    }
    else
    {
      side = 1;
    }
    let dimRef = Vector2.Polar(centerRef, (offset - MultiplyDouble(RequireReference(style).TextOffset, RequireReference(style).DimScaleOverall)), angleRef);
    let defPoints = Init(new Layer("Defpoints"), $new => { $new.Plot = false; });
    entities.Add(Init(new Point(ref1), $new => { $new.Layer = defPoints; }));
    if (((!RequireReference(style).DimLine1Off) && (!RequireReference(style).DimLine2Off)))
    {
      if ((side > 0))
      {
        entities.Add(DimensionBlock.DimensionRadialLine(dimRef, ref1, angleRef, side, style));
        entities.Add(DimensionBlock.EndArrowHead(ref1, angleRef, style));
      }
      else
      {
        entities.Add(Init(new Line(centerRef, ref1), $new => { $new.Color = RequireReference(style).DimLineColor; $new.Linetype = RequireReference(style).DimLineLinetype; $new.Lineweight = RequireReference(style).DimLineLineweight; }));
        entities.Add(DimensionBlock.DimensionRadialLine(dimRef, ref1, angleRef, side, style));
        entities.Add(DimensionBlock.EndArrowHead(ref1, (3.141592653589793 + angleRef), style));
      }
    }
    if ((!MathHelper.$IsZero0(RequireReference(RequireReference(dim).Style).CenterMarkSize)))
    entities.AddRange(DimensionBlock.CenterCross(centerRef, radius, style));
    let texts = DimensionBlock.FormatDimensionText(radius, RequireReference(dim).DimensionType, RequireReference(dim).UserText, style, RequireReference(dim).Owner);
    let dimText = null;
    if ((texts.length > 1))
    {
      dimText = ((GetElement(texts, 0) + "\\P") + GetElement(texts, 1));
    }
    else
    {
      dimText = GetElement(texts, 0);
    }
    let textRot = angleRef;
    let reverse = 1;
    if (((textRot > 1.5707963267948966) && (textRot <= 4.71238898038469)))
    {
      textRot += 3.141592653589793;
      reverse = (-1);
    }
    let textPos = Vector2.Polar(dimRef, MultiplyDouble(MultiplyDouble(((-reverse) * side), RequireReference(style).TextOffset), RequireReference(style).DimScaleOverall), textRot);
    let attachmentPoint = (((reverse * side) < 0) ? 4 : 6);
    let mText = DimensionBlock.DimensionText(textPos, attachmentPoint, textRot, dimText, style);
    if ((mText !== null))
    {
      entities.Add(mText);
    }
    RequireReference(dim).TextReferencePoint = Copy(textPos);
    RequireReference(dim).TextPositionManuallySet = false;
    return Init(Block.CreateOverload("string,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.EntityObject\u003E,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.AttributeDefinition\u003E,bool", name, entities, null, false), $new => { $new.Flags = 1; });
  }
  static $Build8(dim, name) {
    let style = DimensionBlock.BuildDimensionStyleOverride(dim);
    let entities = new List();
    let measure = RequireReference(dim).Measurement;
    let minOffset = (2 * RequireReference(RequireReference(dim).Style).ArrowSize);
    let ref1 = RequireReference(dim).FeaturePoint;
    let ref2 = RequireReference(dim).LeaderEndPoint;
    let refDim = Vector2.op_Subtraction(ref2, ref1);
    let pto1 = new Vector2();
    let pto2 = new Vector2();
    let rotation = (RequireReference(dim).Rotation * 0.017453292519943295);
    let side = 1;
    if ((RequireReference(dim).Axis === 0))
    {
      rotation += 1.5707963267948966;
    }
    let ocsDimRef = Vector2.Rotate(refDim, (-rotation));
    if ((ocsDimRef.X >= 0))
    {
      if ((ocsDimRef.X >= (2 * minOffset)))
      {
        pto1 = Vector2.$create1((ocsDimRef.X - minOffset), 0);
        pto2 = Vector2.$create1((ocsDimRef.X - minOffset), ocsDimRef.Y);
      }
      else
      {
        pto1 = Vector2.$create1(minOffset, 0);
        pto2 = Vector2.$create1((ocsDimRef.X - minOffset), ocsDimRef.Y);
      }
    }
    else
    {
      if ((ocsDimRef.X <= ((-2) * minOffset)))
      {
        pto1 = Vector2.$create1((ocsDimRef.X + minOffset), 0);
        pto2 = Vector2.$create1((ocsDimRef.X + minOffset), ocsDimRef.Y);
      }
      else
      {
        pto1 = Vector2.$create1((-minOffset), 0);
        pto2 = Vector2.$create1((ocsDimRef.X + minOffset), ocsDimRef.Y);
      }
      side = (-1);
    }
    pto1 = Vector2.op_Addition(ref1, Vector2.Rotate(pto1, rotation));
    pto2 = Vector2.op_Addition(ref1, Vector2.Rotate(pto2, rotation));
    let defPoints = Init(new Layer("Defpoints"), $new => { $new.Plot = false; });
    entities.Add(Init(new Point(RequireReference(dim).Origin), $new => { $new.Layer = defPoints; }));
    entities.Add(Init(new Point(RequireReference(dim).FeaturePoint), $new => { $new.Layer = defPoints; }));
    entities.Add(new Line(Vector2.Polar(ref1, MultiplyDouble(RequireReference(style).ExtLineOffset, RequireReference(style).DimScaleOverall), rotation), pto1));
    entities.Add(new Line(pto1, pto2));
    entities.Add(new Line(pto2, ref2));
    let midText = Vector2.Polar(ref2, MultiplyDouble(MultiplyDouble(side, RequireReference(style).TextOffset), RequireReference(style).DimScaleOverall), rotation);
    let texts = DimensionBlock.FormatDimensionText(measure, RequireReference(dim).DimensionType, RequireReference(dim).UserText, style, RequireReference(dim).Owner);
    let dimText = null;
    if ((texts.length > 1))
    {
      dimText = ((GetElement(texts, 0) + "\\P") + GetElement(texts, 1));
    }
    else
    {
      dimText = GetElement(texts, 0);
    }
    let attachmentPoint = ((side < 0) ? 6 : 4);
    let mText = DimensionBlock.DimensionText(midText, attachmentPoint, rotation, dimText, style);
    if ((mText !== null))
    {
      entities.Add(mText);
    }
    RequireReference(dim).TextReferencePoint = Copy(midText);
    RequireReference(dim).TextPositionManuallySet = false;
    return Init(Block.CreateOverload("string,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.EntityObject\u003E,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.AttributeDefinition\u003E,bool", name, entities, null, false), $new => { $new.Flags = 1; });
  }
  static $Build9(dim, name) {
    let ext1, ext2;
    let offset = (MathHelper.$IsZero0(RequireReference(dim).Offset) ? MathHelper.Epsilon : DotNetMath.Abs(RequireReference(dim).Offset));
    let arcAngle = RequireReference(dim).ArcAngle;
    let measure = RequireReference(dim).Measurement;
    let style = DimensionBlock.BuildDimensionStyleOverride(dim);
    let entities = new List();
    let refCenter = RequireReference(dim).CenterPoint;
    let ref1 = Vector2.Polar(RequireReference(dim).CenterPoint, RequireReference(dim).Radius, (RequireReference(dim).StartAngle * 0.017453292519943295));
    let ref2 = Vector2.Polar(RequireReference(dim).CenterPoint, RequireReference(dim).Radius, (RequireReference(dim).EndAngle * 0.017453292519943295));
    if ((RequireReference(dim).Offset < 0))
    {
      ([ref1, ref2] = [Copy(ref2), Copy(ref1)]);
    }
    let startAngle = Vector2.$Angle1(refCenter, ref1);
    let endAngle = Vector2.$Angle1(refCenter, ref2);
    let midRot = (startAngle + ((0.5 * arcAngle) * 0.017453292519943295));
    let dimRef1 = Vector2.Polar(refCenter, offset, startAngle);
    let dimRef2 = Vector2.Polar(refCenter, offset, endAngle);
    let midDim = RequireReference(dim).ArcDefinitionPoint;
    let defPoints = Init(new Layer("Defpoints"), $new => { $new.Plot = false; });
    entities.Add(Init(new Point(ref1), $new => { $new.Layer = defPoints; }));
    entities.Add(Init(new Point(ref2), $new => { $new.Layer = defPoints; }));
    entities.Add(Init(new Point(refCenter), $new => { $new.Layer = defPoints; }));
    if (((!RequireReference(style).DimLine1Off) && (!RequireReference(style).DimLine2Off)))
    {
      let arc = DimensionBlock.DimensionArc(refCenter, dimRef1, dimRef2, startAngle, endAngle, offset, style, { get value() { return ext1; }, set value(v) { ext1 = v; } }, { get value() { return ext2; }, set value(v) { ext2 = v; } });
      if ((!MathHelper.$IsZero0(arcAngle)))
      {
        entities.Add(arc);
      }
      let angle1 = DotNetMath.Asin(((ext1 * 0.5) / offset));
      let angle2 = DotNetMath.Asin(((ext2 * 0.5) / offset));
      entities.Add(DimensionBlock.StartArrowHead(dimRef1, ((angle1 + startAngle) - 1.5707963267948966), style));
      entities.Add(DimensionBlock.EndArrowHead(dimRef2, ((angle2 + endAngle) + 1.5707963267948966), style));
    }
    let refAngle = 0;
    if ((Vector2.Distance(refCenter, ref1) > Vector2.Distance(refCenter, dimRef1)))
    {
      refAngle = 3.141592653589793;
    }
    let dimexo = MultiplyDouble(RequireReference(style).ExtLineOffset, RequireReference(style).DimScaleOverall);
    let dimexe = MultiplyDouble(RequireReference(style).ExtLineExtend, RequireReference(style).DimScaleOverall);
    if ((!RequireReference(style).ExtLine1Off))
    {
      entities.Add(DimensionBlock.ExtensionLine(Vector2.Polar(ref1, dimexo, (startAngle + refAngle)), Vector2.Polar(dimRef1, dimexe, (startAngle + refAngle)), style, RequireReference(style).ExtLine1Linetype));
    }
    if ((!RequireReference(style).ExtLine2Off))
    {
      entities.Add(DimensionBlock.ExtensionLine(Vector2.Polar(ref2, dimexo, (endAngle + refAngle)), Vector2.Polar(dimRef2, dimexe, (endAngle + refAngle)), style, RequireReference(style).ExtLine1Linetype));
    }
    let textRot = (midRot - 1.5707963267948966);
    let gap = MultiplyDouble(RequireReference(style).TextOffset, RequireReference(style).DimScaleOverall);
    if (((textRot > 1.5707963267948966) && (textRot <= 4.71238898038469)))
    {
      textRot += 3.141592653589793;
      gap *= (-1);
    }
    let texts = DimensionBlock.FormatDimensionText(measure, RequireReference(dim).DimensionType, RequireReference(dim).UserText, style, RequireReference(dim).Owner);
    let dimText = null;
    let position = new Vector2();
    let attachmentPoint = 0;
    if ((texts.length > 1))
    {
      position = Copy(midDim);
      dimText = ((GetElement(texts, 0) + "\\P") + GetElement(texts, 1));
      attachmentPoint = 5;
    }
    else
    {
      position = Vector2.Polar(midDim, gap, midRot);
      dimText = GetElement(texts, 0);
      attachmentPoint = 8;
    }
    let mText = DimensionBlock.DimensionText(position, attachmentPoint, textRot, dimText, style);
    if ((mText !== null))
    {
      entities.Add(mText);
    }
    RequireReference(dim).TextReferencePoint = Copy(position);
    RequireReference(dim).TextPositionManuallySet = false;
    return Init(Block.CreateOverload("string,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.EntityObject\u003E,System.Collections.Generic.IEnumerable\u003CnetDxf.Entities.AttributeDefinition\u003E,bool", name, entities, null, false), $new => { $new.Flags = 1; });
  }
  static Build(...args) {
    if (args.length === 2 && (args[0] === null || args[0] instanceof Dimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build1(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof AlignedDimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build2(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof LinearDimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build3(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof Angular2LineDimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build4(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof Angular3PointDimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build5(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof DiametricDimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build6(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof RadialDimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build7(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof OrdinateDimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build8(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof ArcLengthDimension) && (args[1] === null || typeof args[1] === 'string')) return DimensionBlock.$Build9(...args);
    if (args.length === 1 && (args[0] === null || args[0] instanceof Dimension)) return DimensionBlock.$Build0(...args);
    throw new ArgumentException("No matching DimensionBlock.Build overload. Consult native-port-manifest.json.");
  }
}
