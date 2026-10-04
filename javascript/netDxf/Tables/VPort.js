import { RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { NativeString } from '../../runtime/GeometryRuntime.js';
import { TrimDotNet } from '../../runtime/InvariantFloat.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { InstallViewFields, FiniteView, PositiveView, NonzeroView, ViewRange } from '../../runtime/ViewFields.js';
import { ArgumentException, ArgumentNullException, NotSupportedException } from '../../runtime/Errors.js';
import { InstallVPortUcsRelationships } from './UcsRelationships.js';
const fields = {
  ViewCenter:[()=>Vector2.Zero,FiniteView], SnapBasePoint:[()=>Vector2.Zero,FiniteView], SnapSpacing:[()=>new Vector2(.5),FiniteView],
  GridSpacing:[()=>new Vector2(10),FiniteView], ViewDirection:[()=>Vector3.UnitZ,NonzeroView], ViewTarget:[()=>Vector3.Zero,FiniteView],
  ViewHeight:[10,PositiveView], ViewAspectRatio:[1,PositiveView], ShowGrid:[true], SnapMode:[false],
  LowerLeftCorner:[()=>Vector2.Zero,FiniteView], UpperRightCorner:[()=>new Vector2(1),FiniteView], Flags:[0], LensLength:[50,PositiveView],
  FrontClippingPlane:[0,FiniteView], BackClippingPlane:[0,FiniteView], SnapRotation:[0,FiniteView], ViewTwist:[0,FiniteView],
  ViewMode:[0,ViewRange(0,32767)], CircleSides:[1000,ViewRange(1,32767)], FastZoom:[true], UcsIcon:[3,ViewRange(0,3)],
  SnapStyle:[0,ViewRange(0,1)], SnapIsopair:[0,ViewRange(0,2)], RenderMode:[0,ViewRange(0,6)], UcsPerViewport:[false],
  UcsOrigin:[()=>Vector3.Zero,FiniteView], UcsXAxis:[()=>Vector3.UnitX,NonzeroView], UcsYAxis:[()=>Vector3.UnitY,NonzeroView],
  UcsOrthographicType:[0,ViewRange(0,6)], UcsElevation:[0,FiniteView]
};
const identities = new WeakMap(); let nextIdentity = 1;
export class VPort extends TableObject {
  Sun = null; SunHandlePresent = false;
  static get DefaultName() { return '*Active'; }
  static get Active() { return new VPort(VPort.DefaultName,false); }
  static IsActiveName(name) { return name != null && OrdinalIgnoreCaseEquals(TrimDotNet(name),VPort.DefaultName); }
  constructor(name,checkName = true) {
    if (NativeString.IsNullOrWhiteSpace(name)) throw new ArgumentNullException('name');
    super(name,DxfObjectCode.VPort,checkName && !VPort.IsActiveName(name)); this.IsReserved = VPort.IsActiveName(this.Name);
  }
  OnNameChangedEvent(oldName,newName) {
    if (NativeString.IsNullOrWhiteSpace(newName)) throw new ArgumentException('A viewport configuration name cannot be blank.','newName');
    let table = this.Owner; if (table !== null) table.ValidateRecordRename(this);
    super.OnNameChangedEvent(oldName,newName);
    table = this.Owner; if (table !== null) { table.ValidateRecordRename(this); table.CommitRecordRename(this,newName); }
  }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  get UsesReferenceIdentity() { return true; }
  GetHashCode() { if (!identities.has(this)) identities.set(this,nextIdentity++ | 0); return identities.get(this); }
  Clone(newName = this.Name) {
    if (this.Sun !== null) throw new NotSupportedException('Clone the SUN ownership subtree explicitly into a registered destination host.');
    const copy = new VPort(newName);
    for (const name of Object.keys(fields)) copy[name] = this[name];
    copy.NamedUcs = this.NamedUcs; copy.BaseUcs = this.BaseUcs;
    for (const data of this.XData.Values) copy.XData.Add(data.Clone()); copy.SunHandlePresent = this.SunHandlePresent; return copy;
  }
}
InstallViewFields(VPort,fields); InstallVPortUcsRelationships(VPort);

RegisterDatabaseModel('VPort',VPort);
