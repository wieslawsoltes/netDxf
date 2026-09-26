// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { UnderlayDisplayFlags } from './UnderlayDisplayFlags.js';
import { UnderlayType } from '../Objects/UnderlayType.js';
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
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException, RequireInteger } from '../../runtime/Errors.js';
const internal = Symbol('internal Underlay constructor');
const codes = new Map([[UnderlayType.DGN, DxfObjectCode.UnderlayDgn], [UnderlayType.DWF, DxfObjectCode.UnderlayDwf], [UnderlayType.PDF, DxfObjectCode.UnderlayPdf]]);
export class Underlay extends EntityObject {
  #definition = null; #position = Vector3.Zero; #scale = Vector2.Zero;
  #rotation = new DataView(new ArrayBuffer(8)); #contrast = 0; #fade = 0;
  DisplayOptions = 0; ClippingBoundary = null;
  constructor(definition, position = Vector3.Zero, scale = 1) {
    super(EntityType.Underlay, DxfObjectCode.Underlay);
    Object.defineProperty(this, 'UnderlayDefinitionChanged', { value: new EventHook(), enumerable: true });
    if (definition === internal) return;
    if (arguments.length < 1 || arguments.length > 3) throw new ArgumentException('No matching Underlay constructor.');
    if (definition == null) throw new ArgumentNullException('definition');
    this.#definition = definition; this.#position = Copy(position);
    if (scale <= 0) throw new ArgumentOutOfRangeException('scale', scale);
    this.#scale = new Vector2(scale); this.#contrast = 100; this.DisplayOptions = UnderlayDisplayFlags.ShowUnderlay;
    if (codes.has(definition.Type)) this.CodeName = codes.get(definition.Type);
  }
  get Definition() { return this.#definition; }
  set Definition(value) {
    if (value == null) throw new ArgumentNullException('value');
    this.#definition = this.OnUnderlayDefinitionChangedEvent(this.#definition, value);
    // The source projects the proposed type, even if a callback substitutes another definition.
    if (codes.has(value.Type)) this.CodeName = codes.get(value.Type);
  }
  OnUnderlayDefinitionChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.UnderlayDefinitionChanged.Invoke(this, e); return e.NewValue;
  }
  get Position() { return Copy(this.#position); } set Position(value) { this.#position = Copy(value); }
  get Scale() { return Copy(this.#scale); }
  set Scale(value) {
    if (MathHelper.IsZero(value.X) || MathHelper.IsZero(value.Y)) throw new ArgumentOutOfRangeException('value', value);
    this.#scale = Copy(value);
  }
  get Rotation() { return this.#rotation.getFloat64(0); }
  set Rotation(value) { this.#rotation.setFloat64(0, MathHelper.NormalizeAngle(value)); }
  get Contrast() { return this.#contrast; }
  set Contrast(value) { RequireInteger(value, 20, 100); this.#contrast = value; }
  get Fade() { return this.#fade; }
  set Fade(value) { RequireInteger(value, 0, 80); this.#fade = value; }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    const position = Vector3.Add(Matrix3.Multiply(transformation, this.Position), translation);
    const normal = TransformedNormal(transformation, this.Normal);
    const { u, v } = TransformTextAxes(this.Normal, normal, mul(this.Rotation, MathHelper.DegToRad),
      Vector2.Multiply(this.Scale.X, Vector2.UnitX), Vector2.Multiply(this.Scale.Y, Vector2.UnitY), transformation);
    const sign = M.Sign(mul(mul(transformation.M11, transformation.M22), transformation.M33)) < 0 ? -1 : 1;
    let x = mul(sign, u.Modulus()), y = v.Modulus();
    x = MathHelper.IsZero(x) ? MathHelper.Epsilon : x; y = MathHelper.IsZero(y) ? MathHelper.Epsilon : y;
    const rotation = mul(Vector2.Angle(Vector2.Multiply(sign, u)), MathHelper.RadToDeg);
    this.Position = position; this.Normal = normal; this.Rotation = rotation; this.Scale = new Vector2(x, y);
  }
  Clone() {
    const copy = this.$copyEntityAttributes(new Underlay(internal));
    if (this.#definition === null) throw new NullReferenceException();
    Object.assign(copy, { Definition: this.#definition.Clone(), Position: this.#position, Scale: this.#scale, Rotation: this.Rotation,
      Contrast: this.#contrast, Fade: this.#fade, DisplayOptions: this.DisplayOptions,
      ClippingBoundary: this.ClippingBoundary === null ? null : this.ClippingBoundary.Clone() });
    return this.$finishEntityClone(copy);
  }
}
