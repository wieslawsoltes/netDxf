// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Vector3 } from '../Vector3.js';
import { TableObject } from './TableObject.js';
import { UcsReferenceHost } from '../../runtime/UcsReferenceHost.js';
import { Copy, NativeString } from '../../runtime/GeometryRuntime.js';
import { FiniteView, NonzeroView, ViewRange } from '../../runtime/ViewFields.js';
import { ArgumentException, InvalidOperationException } from '../../runtime/Errors.js';
export class ViewUcs {
  #origin = Vector3.Zero; #x = Vector3.UnitX; #y = Vector3.UnitY; #elevation = 0; #orthographic = 0; #named = null; #base = null;
  View = null; // Internal owner in C#; callers assign through View.Ucs.
  get Origin() { return Copy(this.#origin); } set Origin(value) { this.#origin = FiniteView(value); }
  get XAxis() { return Copy(this.#x); } set XAxis(value) { this.#x = NonzeroView(value); }
  get YAxis() { return Copy(this.#y); } set YAxis(value) { this.#y = NonzeroView(value); }
  get OrthographicType() { return this.#orthographic; } set OrthographicType(value) { this.#orthographic = ViewRange(0,6)(value); }
  get Elevation() { return this.#elevation; } set Elevation(value) { this.#elevation = FiniteView(value); }
  get NamedUcs() { return this.#named; } set NamedUcs(value) { UcsReferenceHost.Replace(this.View,this.#named,value); this.#named = value; }
  get BaseUcs() { return this.#base; } set BaseUcs(value) { UcsReferenceHost.Replace(this.View,this.#base,value); this.#base = value; }
  Validate() { if (this.#base !== null && this.#orthographic === 0) throw new InvalidOperationException('A VIEW base UCS requires a nonzero orthographic type.'); }
  Clone() {
    return Object.assign(new ViewUcs(),{Origin:this.Origin,XAxis:this.XAxis,YAxis:this.YAxis,OrthographicType:this.OrthographicType,
      Elevation:this.Elevation,NamedUcs:this.NamedUcs,BaseUcs:this.BaseUcs});
  }
}
export function InstallViewUcs(Type) {
  const records = new WeakMap();
  Object.defineProperty(Type.prototype,'Ucs',{
    get() { return records.get(this) ?? null; }, set(value) {
      const previous = this.Ucs; if (previous === value) return;
      if (value !== null) {
        if (value.View !== null && value.View !== this) throw new ArgumentException('The UCS bundle already belongs to another view.','value');
        UcsReferenceHost.Check(this,value.NamedUcs); UcsReferenceHost.Check(this,value.BaseUcs);
      }
      UcsReferenceHost.Unregister(this); if (previous !== null) previous.View = null;
      records.set(this,value); if (value !== null) value.View = this; UcsReferenceHost.Register(this);
    }
  });
  Type.prototype.OnNameChangedEvent = function(oldName,newName) {
    if (NativeString.IsNullOrWhiteSpace(newName)) throw new ArgumentException('A view name cannot be blank.','newName');
    if (this.Owner !== null) this.Owner.ValidateRecordRename(this,newName);
    TableObject.prototype.OnNameChangedEvent.call(this,oldName,newName);
    if (this.Owner !== null) { this.Owner.ValidateRecordRename(this,newName); this.Owner.CommitRecordRename(this,newName); }
  };
}
