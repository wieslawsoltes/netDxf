// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { TextStyle } from '../Tables/TextStyle.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { MTextAttachmentPoint } from './MTextAttachmentPoint.js';
import { MTextLineSpacingStyle } from './MTextLineSpacingStyle.js';
import { MTextDrawingDirection } from './MTextDrawingDirection.js';
import { FractionFormatType } from '../Units/FractionFormatType.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy, Culture, NumberText, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { EventHook } from '../../runtime/EventHook.js';
import { EntityVector3, TransformedNormal } from '../../runtime/EntityGeometry.js';
import { TransformTextAxes } from '../../runtime/PlanarTextGeometry.js';
import { InstallMTextColumns } from './MTextColumns.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
const num = value => NumberText(value, Culture.Invariant);
const swap = (value, pairs) => { for (const [a,b] of pairs) { if (value === a) return b; if (value === b) return a; } return value; };
export class MText extends EntityObject {
  #position; #height; #rotation = 0; #rectangleWidth; #lineSpacing = 1; #lineSpacingStyle = MTextLineSpacingStyle.AtLeast; #style;
  Value; AttachmentPoint = MTextAttachmentPoint.TopLeft; DrawingDirection = MTextDrawingDirection.ByStyle; BackgroundFill = null;
  Columns = null; $definedColumnHeight = null;
  static DefaultMirrText = false;
  constructor(...args) {
    super(EntityType.MText, DxfObjectCode.MText);
    let text = '', position = Vector3.Zero, height = 1, width = 0, style = TextStyle.Default;
    if (args[0] instanceof Vector2 || args[0] instanceof Vector3) {
      if (args.length < 2 || args.length > 4) throw new ArgumentException('No matching MText constructor.');
      [position,height,width=0,style=TextStyle.Default] = args;
    } else if (args.length === 1) text = args[0];
    else if (args.length >= 3 && args.length <= 5) [text,position,height,width=0,style=TextStyle.Default] = args;
    else if (args.length !== 0) throw new ArgumentException('No matching MText constructor.');
    this.Value = text; this.#position = EntityVector3(position);
    if (style == null) throw new ArgumentNullException('style'); this.#style = style;
    // Rectangle width bypasses its setter in the original constructor.
    this.#rectangleWidth = width;
    if (height <= 0) throw new ArgumentOutOfRangeException('height', text, 'The MText height must be greater than zero.');
    this.#height = height;
    Object.defineProperty(this, 'TextStyleChanged', { value: new EventHook(), enumerable: true });
  }
  get Position() { return Copy(this.#position); } set Position(value) { this.#position = Copy(value); }
  get Rotation() { return this.#rotation; } set Rotation(value) { this.#rotation = MathHelper.NormalizeAngle(value); }
  get Height() { return this.#height; } set Height(value) { if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#height = value; }
  get RectangleWidth() { return this.#rectangleWidth; } set RectangleWidth(value) { if (value < 0) throw new ArgumentOutOfRangeException('value', value); this.#rectangleWidth = value; }
  get LineSpacingFactor() { return this.#lineSpacing; } set LineSpacingFactor(value) { if (value < .25 || value > 4) throw new ArgumentOutOfRangeException('value', value); this.#lineSpacing = value; }
  get LineSpacingStyle() { return this.#lineSpacingStyle; } set LineSpacingStyle(value) {
    if (value === MTextLineSpacingStyle.Default || value === MTextLineSpacingStyle.Multiple) throw new ArgumentOutOfRangeException('value', value);
    this.#lineSpacingStyle = value;
  }
  get Style() { return this.#style; } set Style(value) { if (value == null) throw new ArgumentNullException('value'); this.#style = this.OnTextStyleChangedEvent(this.#style, value); }
  OnTextStyleChangedEvent(oldValue,newValue) { const e = new TableObjectChangedEventArgs(oldValue,newValue); this.TextStyleChanged.Invoke(this,e); return e.NewValue; }
  WriteFraction(numerator, denominator, fractionType, options = null) {
    let text = '';
    if (fractionType === FractionFormatType.Diagonal) text = `\\S${numerator ?? ''}#${denominator ?? ''};`;
    else if (fractionType === FractionFormatType.Horizontal) text = `\\S${numerator ?? ''}/${denominator ?? ''};`;
    else if (fractionType === FractionFormatType.NotStacked) text = `${numerator ?? ''}/${denominator ?? ''}`;
    this.Write(text,options);
  }
  Write(text,options = null) {
    if (options === null) { this.Value = (this.Value ?? '') + (text ?? ''); return; }
    let formatted = text ?? '', height = options.HeightFactor;
    if (options.Superscript) { formatted = `\\S${formatted}^ ;`; height = mul(height, options.SuperSubScriptHeightFactor); }
    if (options.Subscript) { formatted = `\\S^ ${formatted};`; height = mul(height, options.SuperSubScriptHeightFactor); }
    const font = !options.FontName ? this.Style.FontFamilyName || this.Style.FontFile : options.FontName;
    const flags = options.Bold ? (options.Italic ? 'b1|i1' : 'b1|i0') : options.Italic ? 'i1|b0' : 'b0|i0';
    formatted = `\\F${font}|${flags};${formatted}`;
    if (options.Overline) formatted = `\\O${formatted}\\o`;
    if (options.Underline) formatted = `\\L${formatted}\\l`;
    if (options.StrikeThrough) formatted = `\\K${formatted}\\k`;
    if (options.Color !== null) {
      const c = options.Color;
      // MTEXT formatting stores the little-endian R,G,B byte order, not DXF true-color order.
      formatted = `\\C${c.Index};` + (c.UseTrueColor ? `\\c${c.R | (c.G << 8) | (c.B << 16)};` : '') + formatted;
    }
    if (!MathHelper.IsOne(height)) formatted = `\\H${num(height)}x;${formatted}`;
    if (!MathHelper.IsZero(options.ObliqueAngle)) formatted = `\\Q${num(options.ObliqueAngle)};${formatted}`;
    if (!MathHelper.IsOne(options.CharacterSpaceFactor)) formatted = `\\T${num(options.CharacterSpaceFactor)};${formatted}`;
    if (!MathHelper.IsOne(options.WidthFactor)) formatted = `\\W${num(options.WidthFactor)};${formatted}`;
    this.Value = (this.Value ?? '') + `{${formatted}}`;
  }
  EndParagraph() { this.Value = (this.Value ?? '') + '\\P'; }
  StartParagraph(options = null) {
    if (options === null) { this.Value = (this.Value ?? '') + '\\A1;'; return; }
    const alignment = {1:'l',2:'c',3:'r',4:'j',5:'d'}[options.Alignment];
    let codes = alignment ? `\\pq${alignment};` : '', indent = options.FirstLineIndent;
    if (indent < 0 && Math.abs(indent) > options.LeftIndent) indent = -options.LeftIndent;
    codes = `\\pi${num(indent)},l${num(options.LeftIndent)},r${num(options.RightIndent)},b${num(options.SpacingBefore)},a${num(options.SpacingAfter)};${codes}`;
    const spacing = {0:'*',1:'a'+num(options.LineSpacingFactor),2:'e'+num(options.LineSpacingFactor),3:'m'+num(options.LineSpacingFactor)}[options.LineSpacingStyle];
    if (spacing !== undefined) codes = `\\ps${spacing};${codes}`;
    this.Value = (this.Value ?? '') + `\\A${options.VerticalAlignment};\\H${num(options.HeightFactor)}x;${codes}`;
  }
  PlainText() {
    const text = this.Value; if (!text) return '';
    let raw = '';
    for (let i = 0; i < text.length; i++) {
      let token = text[i];
      if (token === '\\') {
        if (++i === text.length) return raw; token = text[i];
        if (token === '\\' || token === '{' || token === '}') raw += token;
        else if ('LlOoKk'.includes(token)) { /* one-character command */ }
        else if (token === 'P' || token === 'X') raw += Culture.NewLine;
        else if (token === 'S') {
          if (++i === text.length) return raw; token = text[i]; let data = '';
          while (token !== ';') {
            if (token === '\\') { if (++i === text.length) return raw; data += text[i]; }
            else if (token === '^') { if (++i === text.length) return raw; if (text[i] !== ' ') data += '^' + text[i]; }
            else data += token === '#' ? '/' : token;
            if (++i === text.length) return raw; token = text[i];
          }
          raw += data;
        } else { while (token !== ';') { if (++i === text.length) return raw; token = text[i]; } }
      } else if (token !== '{' && token !== '}') raw += token;
    }
    return raw;
  }
  TransformBy(transformation, translation) {
    [transformation,translation] = this.$transformArguments(transformation,translation);
    this.$validateColumnTransform(transformation);
    const mirror = this.Owner === null ? MText.DefaultMirrText : this.Owner.Record.Owner.Owner.DrawingVariables.MirrText;
    const position = Vector3.Add(Matrix3.Multiply(transformation,this.Position),translation);
    let normal = TransformedNormal(transformation,this.Normal);
    const {uv,u,v} = TransformTextAxes(this.Normal,normal,mul(this.Rotation,MathHelper.DegToRad),Vector2.UnitX,Vector2.UnitY,transformation);
    const scale = u.Modulus(); let rotation = mul(Vector2.Angle(u),MathHelper.RadToDeg);
    if (Vector2.CrossProduct(u,v) < 0) {
      if (mirror) { rotation += 180; normal = Vector3.Negate(normal); }
      else if (Vector2.DotProduct(u,uv[0]) < 0) { rotation += 180; this.AttachmentPoint = swap(this.AttachmentPoint,[[1,3],[4,6],[7,9]]); }
      else this.AttachmentPoint = swap(this.AttachmentPoint,[[1,7],[2,8],[3,9]]);
    }
    let height = mul(this.Height,scale); height = MathHelper.IsZero(height) ? MathHelper.Epsilon : height;
    this.Position = position; this.Normal = normal; this.Rotation = rotation; this.Height = height; this.RectangleWidth = mul(this.RectangleWidth,scale);
    if (this.Columns !== null) { this.Columns.Scale(scale); this.Columns.ResetEmbeddedPlacement(); }
    if (this.DefinedHeight !== null) this.DefinedHeight = mul(this.DefinedHeight,scale);
  }
  Clone() {
    const copy = this.$copyEntityAttributes(new MText());
    Object.assign(copy,{Position:this.#position,Rotation:this.#rotation,Height:this.#height,LineSpacingFactor:this.#lineSpacing,LineSpacingStyle:this.#lineSpacingStyle,
      DrawingDirection:this.DrawingDirection,BackgroundFill:this.BackgroundFill === null ? null : this.BackgroundFill.Clone(),
      Columns:this.Columns === null ? null : this.Columns.Clone(),DefinedHeight:this.DefinedHeight,RectangleWidth:this.#rectangleWidth,
      AttachmentPoint:this.AttachmentPoint,Style:this.#style.Clone(),Value:this.Value});
    return this.$finishEntityClone(copy);
  }
}
InstallMTextColumns(MText);
