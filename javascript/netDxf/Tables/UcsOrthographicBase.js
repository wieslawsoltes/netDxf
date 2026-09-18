// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { UcsReferenceHost } from '../../runtime/UcsReferenceHost.js';
import { ArgumentException, InvalidOperationException, RequireInteger } from '../../runtime/Errors.js';
const values = new WeakMap();
const state = obj => { if (!values.has(obj)) values.set(obj,{type:0,base:null,present:false}); return values.get(obj); };
export function InstallOrthographicBase(Type) {
  Object.defineProperties(Type.prototype, {
    OrthographicViewType: { get() { return state(this).type; } }, BaseUcs: { get() { return state(this).base; } },
    BaseUcsHandlePresent: { get() { return state(this).present; } }
  });
  Type.prototype.SetOrthographicBase = function(viewType, baseUcs = null) { this.SetLoadedOrthographicBase(viewType,baseUcs,baseUcs !== null); };
  Type.prototype.SetLoadedOrthographicBase = function(viewType, baseUcs, present) {
    RequireInteger(viewType,0,6,'viewType');
    if (viewType === 0 && (baseUcs !== null || present)) throw new ArgumentException('UCS group 346 requires a nonzero orthographic view type.','baseUcs');
    const s = state(this); UcsReferenceHost.Replace(this,s.base,baseUcs); s.type = viewType; s.base = baseUcs; s.present = present;
  };
  Type.prototype.ValidateOrthographicBase = function() {
    const s = state(this); if (s.type < 0 || s.type > 6 || (s.type === 0 && (s.base !== null || s.present))) throw new InvalidOperationException('Invalid stored UCS orthographic base relationship.');
  };
  Type.prototype.CopyOrthographicBaseTo = function(copy) { values.set(copy,{...state(this)}); };
}
