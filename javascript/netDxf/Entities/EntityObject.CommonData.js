// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
export const EntityShadowMode = Object.freeze({ CastAndReceive: 0, Cast: 1, Receive: 2, Ignore: 3 });
const maximum = 16 * 1024 * 1024;
/** Stored opaque metadata, not a graphics decoder or a cache regenerator. */
export class CommonEntityData {
  ColorName = null; ShadowMode = null; ProxyGraphics = null;
  SetShadowMode(value) {
    if (value !== null && (!Number.isInteger(value) || value < 0 || value > 3)) throw new ArgumentOutOfRangeException('value');
    this.ShadowMode = value;
  }
  SetProxyGraphics(value) {
    if (value !== null && !(value instanceof Uint8Array)) throw new ArgumentException('Expected Uint8Array or null.', 'value');
    if (value !== null && value.length > maximum) throw new ArgumentOutOfRangeException('value');
    this.ProxyGraphics = value === null ? null : new Uint8Array(value);
  }
  GetProxyGraphics() { return this.ProxyGraphics === null ? null : new Uint8Array(this.ProxyGraphics); }
  CopyTo(target) { target.ColorName = this.ColorName; target.ShadowMode = this.ShadowMode; target.SetProxyGraphics(this.ProxyGraphics); }
}
export function InstallEntityCommonData(Type) {
  Object.defineProperty(Type, 'MaximumProxyGraphicsBytes', { value: maximum, enumerable: true });
  Object.defineProperties(Type.prototype, {
    ColorName: { get() { return this.CommonData.ColorName; }, set(value) { this.CommonData.ColorName = value; } },
    ShadowMode: { get() { return this.CommonData.ShadowMode; }, set(value) { this.CommonData.SetShadowMode(value); } },
    ProxyGraphics: { get() { return this.CommonData.GetProxyGraphics(); }, set(value) { this.CommonData.SetProxyGraphics(value); } }
  });
  Type.prototype.ClearProxyGraphics = function() { this.CommonData.ProxyGraphics = null; };
  Type.prototype.CopyCommonDataTo = function(copy) {
    if (copy == null) throw new ArgumentNullException('copy');
    this.CommonData.CopyTo(copy.CommonData);
  };
}
