// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ArgumentException, NotSupportedException } from '../../runtime/Errors.js';
import { DxfVersion } from '../Header/DxfVersion.js';
const data = new WeakMap();
const state = value => { if (!data.has(value)) data.set(value,{target:null,present:false}); return data.get(value); };
function validate(document,target,present) {
  if (present && document.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) throw new NotSupportedException('A VIEW live section reference requires R2007 or later.');
  if (target !== null && (target.IsErased || target.Handle === null || target.Owner === null || target.Owner.Record.Owner !== document.Blocks ||
      !target.Owner.Entities.Contains(target) || document.GetObjectByHandle(target.Handle) !== target))
    throw new ArgumentException('A live section must already be registered in the same document.','target');
}
export function InstallViewLiveSection(Type) {
  Object.defineProperties(Type.prototype,{
    LiveSection: { get() { return state(this).target; },set(value) {
      if (value !== null && value.IsErased) throw new ArgumentException('An erased section cannot be referenced.','value');
      if (this.Owner !== null) validate(this.Owner.Owner,value,true);
      const s = state(this); s.target = value; s.present = true;
    } },
    HasStoredLiveSection: { get() { return state(this).present; } }
  });
  Type.prototype.ClearLiveSectionReference = function() { data.set(this,{target:null,present:false}); };
  Type.prototype.ValidateLiveSection = function(document) { const s = state(this); validate(document,s.target,s.present); };
  Type.prototype.CopyLiveSectionTo = function(copy) { data.set(copy,{...state(this)}); };
}
