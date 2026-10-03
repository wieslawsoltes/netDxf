// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { ImageDisplayFlags } from './ImageDisplayFlags.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { ClippingBoundary } from '../ClippingBoundary.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { EventHook } from '../../runtime/EventHook.js';
import { EntityVector3, TransformedNormal } from '../../runtime/EntityGeometry.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException, RequireInteger } from '../../runtime/Errors.js';
const internal = Symbol('internal Image constructor');
export class Image extends EntityObject {
  #definition = null; #position = Vector3.Zero; #u = Vector2.Zero; #v = Vector2.Zero;
  #size = new DataView(new ArrayBuffer(16)); #brightness = 0; #contrast = 0; #fade = 0; #boundary = null;
  Clipping = false; DisplayOptions = 0;
  constructor(imageDefinition, position, widthOrSize, height) {
    super(EntityType.Image, DxfObjectCode.Image);
    Object.defineProperty(this, 'ImageDefinitionChanged', { value: new EventHook(), enumerable: true });
    if (imageDefinition === internal) return;
    if (arguments.length !== 3 && arguments.length !== 4) throw new ArgumentException('No matching Image constructor.');
    let width = widthOrSize;
    if (arguments.length === 3) {
      if (!(widthOrSize instanceof Vector2)) throw new ArgumentException('Image size must be Vector2.');
      width = widthOrSize.X; height = widthOrSize.Y;
    }
    if (imageDefinition == null) throw new ArgumentNullException('imageDefinition');
    this.#definition = imageDefinition; this.#position = EntityVector3(position);
    this.#u = Vector2.UnitX; this.#v = Vector2.UnitY;
    if (width <= 0) throw new ArgumentOutOfRangeException('width', width);
    this.#size.setFloat64(0, width);
    if (height <= 0) throw new ArgumentOutOfRangeException('height', height);
    this.#size.setFloat64(8, height);
    this.#brightness = 50; this.#contrast = 50;
    this.DisplayOptions = ImageDisplayFlags.ShowImage | ImageDisplayFlags.ShowImageWhenNotAlignedWithScreen | ImageDisplayFlags.UseClippingBoundary;
    this.#boundary = new ClippingBoundary(0, 0, imageDefinition.Width, imageDefinition.Height);
  }
  get Position() { return Copy(this.#position); } set Position(value) { this.#position = Copy(value); }
  get Uvector() { return Copy(this.#u); }
  set Uvector(value) {
    if (Vector2.Equals(Vector2.Zero, value)) throw new ArgumentException('The U vector can not be the zero vector.', 'value');
    this.#u = Vector2.Normalize(value);
  }
  get Vvector() { return Copy(this.#v); }
  set Vvector(value) {
    if (Vector2.Equals(Vector2.Zero, value)) throw new ArgumentException('The V vector can not be the zero vector.', 'value');
    this.#v = Vector2.Normalize(value);
  }
  get Width() { return this.#size.getFloat64(0); }
  set Width(value) { if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#size.setFloat64(0, value); }
  get Height() { return this.#size.getFloat64(8); }
  set Height(value) { if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#size.setFloat64(8, value); }
  get Rotation() { return mul(Vector2.Angle(this.#u), MathHelper.RadToDeg); }
  set Rotation(value) {
    // Source behavior is incremental rotation of both current axes, not an absolute assignment.
    const uv = MathHelper.Transform([this.#u, this.#v], mul(MathHelper.NormalizeAngle(value), MathHelper.DegToRad), CoordinateSystem.Object, CoordinateSystem.World);
    this.#u = uv[0]; this.#v = uv[1];
  }
  get Definition() { return this.#definition; }
  set Definition(value) {
    if (value == null) throw new ArgumentNullException('value');
    this.#definition = this.OnImageDefinitionChangedEvent(this.#definition, value);
  }
  OnImageDefinitionChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.ImageDefinitionChanged.Invoke(this, e); return e.NewValue;
  }
  get Brightness() { return this.#brightness; } set Brightness(value) { this.#brightness = RequireInteger(value, 0, 100); }
  get Contrast() { return this.#contrast; } set Contrast(value) { this.#contrast = RequireInteger(value, 0, 100); }
  get Fade() { return this.#fade; } set Fade(value) { this.#fade = RequireInteger(value, 0, 100); }
  get ClippingBoundary() { return this.#boundary; }
  set ClippingBoundary(value) {
    if (value == null && this.#definition == null) throw new NullReferenceException();
    this.#boundary = value ?? new ClippingBoundary(0, 0, this.#definition.Width, this.#definition.Height);
  }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    const position = Vector3.Add(Matrix3.Multiply(transformation, this.Position), translation);
    const normal = TransformedNormal(transformation, this.Normal);
    const ow = MathHelper.ArbitraryAxis(this.Normal), wo = MathHelper.ArbitraryAxis(normal).Transpose();
    const transform = (axis, size) => {
      let v = Matrix3.Multiply(ow, new Vector3(mul(axis.X, size), mul(axis.Y, size), 0));
      v = Matrix3.Multiply(transformation, v); v = Matrix3.Multiply(wo, v);
      return new Vector2(v.X, v.Y);
    };
    let u = transform(this.Uvector, this.Width), v = transform(this.Vvector, this.Height), width, height;
    if (Vector2.Equals(Vector2.Zero, u)) { u = this.Uvector; width = MathHelper.Epsilon; } else width = u.Modulus();
    // The pinned C# singular V-axis fallback uses the old U axis, not V.
    if (Vector2.Equals(Vector2.Zero, v)) { v = this.Uvector; height = MathHelper.Epsilon; } else height = v.Modulus();
    this.Position = position; this.Normal = normal; this.Uvector = u; this.Vvector = v; this.Width = width; this.Height = height;
  }
  Clone() {
    const copy = this.$copyEntityAttributes(new Image(internal));
    copy.Position = this.#position; copy.Height = this.Height; copy.Width = this.Width; copy.Uvector = this.#u; copy.Vvector = this.#v;
    if (this.#definition === null) throw new NullReferenceException();
    copy.Definition = this.#definition.Clone(); copy.Clipping = this.Clipping;
    copy.Brightness = this.#brightness; copy.Contrast = this.#contrast; copy.Fade = this.#fade; copy.DisplayOptions = this.DisplayOptions;
    copy.ClippingBoundary = this.#boundary.Clone();
    return this.$finishEntityClone(copy);
  }
}
