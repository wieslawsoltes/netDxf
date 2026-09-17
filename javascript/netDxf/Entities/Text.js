// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { TextAlignment } from './TextAligment.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { TextStyle } from '../Tables/TextStyle.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy, DotNetMath as M, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { EventHook } from '../../runtime/EventHook.js';
import { EntityVector3, TransformedNormal } from '../../runtime/EntityGeometry.js';
import { TransformTextAxes } from '../../runtime/PlanarTextGeometry.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
const swap = (value, pairs) => {
  for (const [a, b] of pairs) { if (value === a) return b; if (value === b) return a; }
  return value;
};
export class Text extends EntityObject {
  #position; #height; #width = 1; #widthFactor; #obliqueAngle; #rotation = 0; #style;
  Value; Alignment = TextAlignment.BaselineLeft; IsBackward = false; IsUpsideDown = false;
  static DefaultMirrText = false;
  constructor(text = '', position = Vector3.Zero, height = 1, style = TextStyle.Default) {
    super(EntityType.Text, DxfObjectCode.Text);
    if (![0, 1, 3, 4].includes(arguments.length)) throw new ArgumentException('No matching Text constructor.');
    this.Value = text; this.#position = EntityVector3(position); this.Normal = Vector3.UnitZ;
    if (style == null) throw new ArgumentNullException('style');
    this.#style = style;
    if (height <= 0) throw new ArgumentOutOfRangeException('height', text, 'The Text height must be greater than zero.');
    this.#height = height; this.#widthFactor = style.WidthFactor; this.#obliqueAngle = style.ObliqueAngle;
    Object.defineProperty(this, 'TextStyleChanged', { value: new EventHook(), enumerable: true });
  }
  get Position() { return Copy(this.#position); } set Position(value) { this.#position = Copy(value); }
  get Rotation() { return this.#rotation; } set Rotation(value) { this.#rotation = MathHelper.NormalizeAngle(value); }
  get Height() { return this.#height; } set Height(value) {
    if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#height = value;
  }
  get Width() { return this.#width; } set Width(value) {
    if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#width = value;
  }
  get WidthFactor() { return this.#widthFactor; } set WidthFactor(value) {
    if (value < 0.01 || value > 100) throw new ArgumentOutOfRangeException('value', value); this.#widthFactor = value;
  }
  get ObliqueAngle() { return this.#obliqueAngle; } set ObliqueAngle(value) {
    if (value < -85 || value > 85) throw new ArgumentOutOfRangeException('value', value); this.#obliqueAngle = value;
  }
  get Style() { return this.#style; } set Style(value) {
    if (value == null) throw new ArgumentNullException('value'); this.#style = this.OnTextStyleChangedEvent(this.#style, value);
  }
  OnTextStyleChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.TextStyleChanged.Invoke(this, e); return e.NewValue;
  }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    const mirror = this.Owner === null ? Text.DefaultMirrText : this.Owner.Record.Owner.Owner.DrawingVariables.MirrText;
    const position = Vector3.Add(Matrix3.Multiply(transformation, this.Position), translation);
    const normal = TransformedNormal(transformation, this.Normal);
    const { uv, u, v } = TransformTextAxes(this.Normal, normal, mul(this.Rotation, MathHelper.DegToRad),
      Vector2.Multiply(mul(this.WidthFactor, this.Height), Vector2.UnitX),
      new Vector2(mul(this.Height, M.Tan(mul(this.ObliqueAngle, MathHelper.DegToRad))), this.Height), transformation);
    let rotation = mul(Vector2.Angle(u), MathHelper.RadToDeg), oblique = mul(Vector2.Angle(v), MathHelper.RadToDeg);
    if (Vector2.CrossProduct(u, v) < 0) {
      oblique = 90 - (rotation - oblique);
      if (mirror) {
        if (this.Alignment !== TextAlignment.Fit && this.Alignment !== TextAlignment.Aligned) rotation += 180;
        this.IsBackward = !this.IsBackward;
      } else if (Vector2.DotProduct(u, uv[0]) < 0) {
        rotation += 180;
        this.Alignment = swap(this.Alignment, [[0, 2], [3, 5], [9, 11], [6, 8]]);
      } else this.Alignment = swap(this.Alignment, [[0, 6], [1, 7], [2, 8]]);
    } else oblique = 90 + (rotation - oblique);
    oblique = MathHelper.NormalizeAngle(oblique);
    if (oblique > 180) oblique = 180 - oblique;
    if (oblique < -85) oblique = -85; else if (oblique > 85) oblique = 85;
    let height = mul(v.Modulus(), M.Cos(mul(oblique, MathHelper.DegToRad)));
    height = MathHelper.IsZero(height) ? MathHelper.Epsilon : height;
    let widthFactor = u.Modulus() / height;
    if (widthFactor < 0.01) widthFactor = 0.01; else if (widthFactor > 100) widthFactor = 100;
    this.Position = position; this.Normal = normal; this.Rotation = rotation; this.Height = height;
    this.WidthFactor = widthFactor; this.ObliqueAngle = oblique;
  }
  Clone() {
    const copy = this.$copyEntityAttributes(new Text());
    Object.assign(copy, { Position: this.#position, Rotation: this.#rotation, Height: this.#height, Width: this.#width,
      WidthFactor: this.#widthFactor, ObliqueAngle: this.#obliqueAngle, Alignment: this.Alignment,
      IsBackward: this.IsBackward, IsUpsideDown: this.IsUpsideDown, Style: this.#style.Clone(), Value: this.Value });
    return this.$finishEntityClone(copy);
  }
}
