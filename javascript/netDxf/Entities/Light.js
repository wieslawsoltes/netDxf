// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { Copy, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
export const LightType = Object.freeze({ Distant: 1, Point: 2, Spot: 3 });
export const LightAttenuationType = Object.freeze({ None: 0, InverseLinear: 1, InverseSquare: 2 });
export const LightShadowType = Object.freeze({ RayTraced: 0, ShadowMap: 1 });
function nonnegative(value, name = 'value') {
  if (!Number.isFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
}
function finitePoint(value, name = 'value') {
  if (![value.X, value.Y, value.Z].every(Number.isFinite)) throw new ArgumentOutOfRangeException(name);
}
const divide = (v, scale) => new Vector3(v.X / scale, v.Y / scale, v.Z / scale);
function stableLength(v) {
  const m = Math.max(Math.abs(v.X), Math.max(Math.abs(v.Y), Math.abs(v.Z)));
  if (m === 0) return 0;
  v = divide(v, m);
  return mul(m, Math.sqrt(mul(v.X, v.X) + mul(v.Y, v.Y) + mul(v.Z, v.Z)));
}
export class Light extends EntityObject {
  #name = ''; #version = 0; #kind = LightType.Distant; #position = Vector3.Zero; #target = Vector3.UnitZ;
  #intensity = 1; #attenuation = LightAttenuationType.InverseSquare; #start = 0; #end = 0;
  #hotspot = 45; #falloff = 90; #shadow = LightShadowType.RayTraced; #mapSize = 512; #softness = 1;
  IsOn = true; PlotGlyph = false; UseAttenuationLimits = false; CastShadows = true;
  constructor() { super(EntityType.Light, DxfObjectCode.Light); }
  get VersionNumber() { return this.#version; } set VersionNumber(value) { this.#version = RequireInteger(value, 0, 2147483647); }
  get Name() { return this.#name; }
  set Name(value) {
    if (value == null) throw new ArgumentNullException('value');
    if (typeof value !== 'string' || /[\r\n\0]/.test(value)) throw new ArgumentException('A LIGHT name cannot contain CR, LF or NUL transport delimiters.', 'value');
    this.#name = value;
  }
  get LightType() { return this.#kind; } set LightType(value) { this.#kind = RequireInteger(value, 1, 3); }
  get Intensity() { return this.#intensity; } set Intensity(value) { nonnegative(value); this.#intensity = value; }
  get Position() { return Copy(this.#position); } set Position(value) { finitePoint(value); this.#position = Copy(value); }
  get Target() { return Copy(this.#target); } set Target(value) { finitePoint(value); this.#target = Copy(value); }
  get AttenuationType() { return this.#attenuation; } set AttenuationType(value) { this.#attenuation = RequireInteger(value, 0, 2); }
  get AttenuationStartLimit() { return this.#start; } set AttenuationStartLimit(value) { nonnegative(value); this.#start = value; }
  get AttenuationEndLimit() { return this.#end; } set AttenuationEndLimit(value) { nonnegative(value); this.#end = value; }
  get HotspotAngle() { return this.#hotspot; } set HotspotAngle(value) { nonnegative(value); this.#hotspot = value; }
  get FalloffAngle() { return this.#falloff; } set FalloffAngle(value) { nonnegative(value); this.#falloff = value; }
  get ShadowType() { return this.#shadow; } set ShadowType(value) { this.#shadow = RequireInteger(value, 0, 1); }
  get ShadowMapSize() { return this.#mapSize; } set ShadowMapSize(value) { this.#mapSize = RequireInteger(value, 0, 2147483647); }
  get ShadowMapSoftness() { return this.#softness; } set ShadowMapSoftness(value) { this.#softness = RequireInteger(value, 0, 32767); }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    finitePoint(translation, 'translation');
    let x = Matrix3.Multiply(transformation, Vector3.UnitX), y = Matrix3.Multiply(transformation, Vector3.UnitY), z = Matrix3.Multiply(transformation, Vector3.UnitZ);
    const scale = stableLength(x), sy = stableLength(y), sz = stableLength(z);
    if (!Number.isFinite(scale) || scale === 0 || !Number.isFinite(sy) || !Number.isFinite(sz))
      throw new ArgumentException('LIGHT requires a finite nonsingular similarity transform.', 'transformation');
    x = divide(x, scale); y = divide(y, scale); z = divide(z, scale);
    if (Math.abs(sy / scale - 1) > 1e-10 || Math.abs(sz / scale - 1) > 1e-10 ||
        Math.abs(Vector3.DotProduct(x, y)) > 1e-10 || Math.abs(Vector3.DotProduct(x, z)) > 1e-10 || Math.abs(Vector3.DotProduct(y, z)) > 1e-10)
      throw new ArgumentException('LIGHT does not support shear or nonuniform scale.', 'transformation');
    const p = Vector3.Add(Matrix3.Multiply(transformation, this.#position), translation);
    const t = Vector3.Add(Matrix3.Multiply(transformation, this.#target), translation);
    const start = mul(this.#start, scale), end = mul(this.#end, scale);
    finitePoint(p, 'transformation'); finitePoint(t, 'transformation'); nonnegative(start, 'transformation'); nonnegative(end, 'transformation');
    this.#position = p; this.#target = t; this.#start = start; this.#end = end;
  }
  Clone() {
    const copy = this.$copyEntityAttributes(new Light());
    for (const key of ['VersionNumber','Name','LightType','IsOn','PlotGlyph','Intensity','Position','Target','AttenuationType',
      'UseAttenuationLimits','AttenuationStartLimit','AttenuationEndLimit','HotspotAngle','FalloffAngle','CastShadows','ShadowType','ShadowMapSize','ShadowMapSoftness']) copy[key] = this[key];
    return this.$finishEntityClone(copy);
  }
}
