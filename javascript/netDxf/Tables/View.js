import { RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { ViewFlags } from './ViewFlags.js';
import { InstallViewFields, FiniteView, PositiveView, NonzeroView, ViewRange } from '../../runtime/ViewFields.js';
import { ArgumentNullException, NotSupportedException } from '../../runtime/Errors.js';
import { InstallViewUcs } from './ViewUcs.js';
import { InstallViewLiveSection } from './View.LiveSection.js';
const fields = {
  Target:[()=>Vector3.Zero,v=>FiniteView(v,'Target')], ViewDirection:[()=>Vector3.UnitZ,v=>NonzeroView(v,'ViewDirection')],
  ViewCenter:[()=>Vector2.Zero,v=>FiniteView(v,'ViewCenter')], Height:[1,v=>PositiveView(v,'Height')], Width:[1,v=>PositiveView(v,'Width')],
  Rotation:[0,v=>FiniteView(v,'Rotation')], ViewMode:[0,ViewRange(-32768,32767)], LensLength:[40,v=>PositiveView(v,'LensLength')],
  FrontClippingPlane:[0,v=>FiniteView(v,'FrontClippingPlane')], BackClippingPlane:[0,v=>FiniteView(v,'BackClippingPlane')],
  Flags:[0], RenderMode:[0,ViewRange(0,6)], IsCameraPlottable:[false]
};
export class View extends TableObject {
  Sun = null; SunHandlePresent = false; // Internal ownership metadata in C#.
  constructor(name,checkName = true) {
    if (arguments.length < 2 && name == null) throw new ArgumentNullException('name');
    super(name,DxfObjectCode.View,checkName);
    if (name == null || name === '') throw new ArgumentNullException('name');
  }
  get Camera() { return this.ViewDirection; } set Camera(value) { this.ViewDirection = value; }
  get Fov() { return this.LensLength; } set Fov(value) { this.LensLength = value; }
  get Viewmode() { return this.ViewMode; } set Viewmode(value) { this.ViewMode = value; }
  get IsPaperSpace() { return (this.Flags & ViewFlags.PaperSpace) !== 0; }
  set IsPaperSpace(value) { this.Flags = value ? this.Flags | ViewFlags.PaperSpace : this.Flags & ~ViewFlags.PaperSpace; }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    if (this.Sun !== null) throw new NotSupportedException('Clone the SUN ownership subtree explicitly into a registered destination host.');
    const copy = new View(newName);
    for (const name of Object.keys(fields)) copy[name] = this[name];
    copy.Ucs = this.Ucs === null ? null : this.Ucs.Clone(); this.CopyLiveSectionTo(copy);
    for (const data of this.XData.Values) copy.XData.Add(data.Clone()); copy.SunHandlePresent = this.SunHandlePresent; return copy;
  }
}
InstallViewFields(View,fields); InstallViewUcs(View); InstallViewLiveSection(View);

RegisterDatabaseModel('View',View);
