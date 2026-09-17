// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy, DotNetMath as M, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { EventHook } from '../../runtime/EventHook.js';
import { TransformedNormal } from '../../runtime/EntityGeometry.js';
import { TransformTextAxes } from '../../runtime/PlanarTextGeometry.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
export class Shape extends EntityObject {
  #name; #style; #position; #size; #angles = new DataView(new ArrayBuffer(16)); #widthFactor = 1; Thickness = 0;
  constructor(name, style, position = Vector3.Zero, size = 1, rotation = 0) {
    super(EntityType.Shape, DxfObjectCode.Shape);
    if (![2, 5].includes(arguments.length)) throw new ArgumentException('No matching Shape constructor.');
    if (name == null || name === '') throw new ArgumentNullException('name');
    if (style == null) throw new ArgumentNullException('style');
    if (!(position instanceof Vector3)) throw new ArgumentException('Shape position must be Vector3.');
    this.#name = name; this.#style = style; this.#position = Copy(position);
    if (size <= 0) throw new ArgumentOutOfRangeException('size', size); this.#size = size;
    // The constructor does not normalize the angle; the public setter does.
    this.#angles.setFloat64(0, rotation);
    Object.defineProperty(this, 'StyleChanged', { value: new EventHook(), enumerable: true });
  }
  get Name() { return this.#name; } set Name(value) { if (value == null || value === '') throw new ArgumentNullException('value'); this.#name = value; }
  get Style() { return this.#style; } set Style(value) {
    if (value == null) throw new ArgumentNullException('value'); this.#style = this.OnStyleChangedEvent(this.#style, value);
  }
  OnStyleChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.StyleChanged.Invoke(this, e); return e.NewValue;
  }
  get Position() { return Copy(this.#position); } set Position(value) { this.#position = Copy(value); }
  get Size() { return this.#size; } set Size(value) { if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#size = value; }
  // Keep constructor/raw rotation distinct from normalized editing, including signed NaNs.
  get Rotation() { return this.#angles.getFloat64(0); }
  set Rotation(value) { this.#angles.setFloat64(0, MathHelper.NormalizeAngle(value)); }
  get ObliqueAngle() { return this.#angles.getFloat64(8); }
  set ObliqueAngle(value) { this.#angles.setFloat64(8, MathHelper.NormalizeAngle(value)); }
  get WidthFactor() { return this.#widthFactor; } set WidthFactor(value) {
    if (MathHelper.IsZero(value)) throw new ArgumentOutOfRangeException('value', value); this.#widthFactor = value;
  }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    const position = Vector3.Add(Matrix3.Multiply(transformation, this.Position), translation);
    const normal = TransformedNormal(transformation, this.Normal);
    const { u, v } = TransformTextAxes(this.Normal, normal, mul(this.Rotation, MathHelper.DegToRad),
      Vector2.Multiply(Vector2.Multiply(Vector2.UnitX, this.WidthFactor), this.Size),
      new Vector2(mul(this.Size, M.Tan(mul(this.ObliqueAngle, MathHelper.DegToRad))), this.Size), transformation);
    let rotation = mul(Vector2.Angle(u), MathHelper.RadToDeg), oblique = mul(Vector2.Angle(v), MathHelper.RadToDeg);
    const mirror = Vector2.CrossProduct(u, v) < 0;
    if (mirror) { rotation += 180; oblique = 270 - (rotation - oblique); if (oblique >= 360) oblique -= 360; }
    else oblique = 90 + (rotation - oblique);
    if (oblique > 180) oblique = 180 - oblique;
    if (oblique < -85) oblique = -85; else if (oblique > 85) oblique = 85;
    let height = mul(v.Modulus(), M.Cos(mul(oblique, MathHelper.DegToRad)));
    height = MathHelper.IsZero(height) ? MathHelper.Epsilon : height;
    let widthFactor = u.Modulus() / height;
    if (widthFactor < 0.01) widthFactor = 0.01; else if (widthFactor > 100) widthFactor = 100;
    this.Position = position; this.Normal = normal; this.Rotation = rotation; this.Size = height;
    this.WidthFactor = mirror ? -widthFactor : widthFactor; this.ObliqueAngle = mirror ? -oblique : oblique;
  }
  Clone() {
    const copy = this.$copyEntityAttributes(new Shape(this.#name, this.#style.Clone()));
    // WidthFactor is deliberately omitted by the pinned C# Clone body.
    Object.assign(copy, { Position: this.#position, Size: this.#size, Rotation: this.Rotation,
      ObliqueAngle: this.ObliqueAngle, Thickness: this.Thickness });
    return this.$finishEntityClone(copy);
  }
}
