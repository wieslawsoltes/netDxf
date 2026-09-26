// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { VPort } from '../Tables/VPort.js';
import { View } from '../Tables/View.js';
import { Viewport } from '../Entities/Viewport.js';
import { DxfSun } from './DxfSun.js';
import { DxfOpaqueObject } from './DxfOpaqueObject.js';
import { DxfVersion } from '../Header/DxfVersion.js';
import { ArgumentException, NotSupportedException } from '../../runtime/Errors.js';
/** Internal host adapter. It observes caller-supplied registration, not a fabricated database. */
export class SunReferences {
  static IsHost(host) { return host instanceof VPort || host instanceof View || host instanceof Viewport; }
  static Get(host) { return this.IsHost(host) ? host.Sun : null; }
  static IsPresent(host) { return this.IsHost(host) && host.SunHandlePresent; }
  static Set(host,sun,present=true) {
    if(!this.IsHost(host)) throw new ArgumentException('SUN requires a VIEW, VPORT or VIEWPORT owner.','host');
    host.Sun=sun;host.SunHandlePresent=present;
  }
  static CheckProfile(host,version) {
    if(!this.IsHost(host)) throw new ArgumentException('SUN requires a VIEW, VPORT or VIEWPORT owner.','host');
    if(version < (host instanceof View ? DxfVersion.AutoCad2010 : DxfVersion.AutoCad2007))
      throw new NotSupportedException('SUN requires R2007 or later; named VIEW ownership requires R2010 or later.');
  }
  static CheckClone(host) {
    if(this.Get(host) !== null) throw new NotSupportedException('Clone the SUN ownership subtree explicitly with DxfObjectDatabase.CloneSun into a registered destination host.');
  }
  static Validate(host,database,errors) {
    if(!this.IsHost(host) || !this.IsPresent(host)) return;
    try { this.CheckProfile(host,database.Document.DrawingVariables.AcadVer); }
    catch(error) { if(!(error instanceof NotSupportedException)) throw error;errors.Add(error.message); }
    const sun=this.Get(host);
    if(sun !== null && (!(sun instanceof DxfSun || sun instanceof DxfOpaqueObject && sun.CodeName === 'SUN') || !database.IsRegistered(sun) || sun.Owner !== host))
      errors.Add('Invalid reciprocal SUN ownership: ' + (host.Handle ?? ''));
  }
}
