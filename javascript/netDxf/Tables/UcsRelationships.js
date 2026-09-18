// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { NativeString } from '../../runtime/GeometryRuntime.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { UcsReferenceHost } from '../../runtime/UcsReferenceHost.js';
export const UcsFlags = Object.freeze({ None:0, ExternallyDependent:16, XrefResolved:32, Referenced:64 });
export function InstallUcsRelationships(Type) {
  const flags = new WeakMap();
  Object.defineProperty(Type.prototype,'Flags',{ get() { return flags.get(this) ?? 0; },set(value) { flags.set(this,value); } });
  Type.prototype.OnNameChangedEvent = function(oldName,newName) {
    if (NativeString.IsNullOrWhiteSpace(newName)) throw new ArgumentException('A UCS name cannot be blank.','newName');
    if (this.Owner !== null) this.Owner.ValidateRecordRename(this,newName);
    TableObject.prototype.OnNameChangedEvent.call(this,oldName,newName);
    if (this.Owner !== null) { this.Owner.ValidateRecordRename(this,newName); this.Owner.CommitRecordRename(this,newName); }
  };
}
export function InstallVPortUcsRelationships(Type) {
  const records = new WeakMap();
  const state = obj => { if (!records.has(obj)) records.set(obj,{NamedUcs:null,BaseUcs:null}); return records.get(obj); };
  for (const name of ['NamedUcs','BaseUcs']) Object.defineProperty(Type.prototype,name,{
    get() { return state(this)[name]; },set(value) { const s = state(this); UcsReferenceHost.Replace(this,s[name],value); s[name] = value; }
  });
}
