// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { Linetype } from './Linetype.js';
import { AciColor } from '../AciColor.js';
import { Transparency } from '../Transparency.js';
import { Lineweight } from '../Lineweight.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { TableObjectChangedEventArgs } from './TableObjectChangedEventArgs.js';
import { EventHook } from '../../runtime/EventHook.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException } from '../../runtime/Errors.js';
export class Layer extends TableObject {
  $description = ''; $color; $linetype; $lineweight; $transparency; $assigned = false;
  IsVisible = true; IsFrozen = false; IsLocked = false; Plot = true;
  static get DefaultName() { return '0'; }
  static get Default() { return new Layer(Layer.DefaultName); }
  constructor(name, checkName = true) {
    super(name, DxfObjectCode.Layer, checkName);
    if (name == null || name === '') throw new ArgumentNullException('name');
    this.IsReserved = OrdinalIgnoreCaseEquals(name, Layer.DefaultName);
    this.$color = AciColor.Default; this.$linetype = Linetype.Continuous; this.$lineweight = Lineweight.Default; this.$transparency = new Transparency(0);
    Object.defineProperty(this, 'LinetypeChanged', { value: new EventHook(), enumerable: true });
  }
  get Description() { return this.$description; }
  set Description(value) { this.$description = value == null || value === '' ? '' : value; }
  get Linetype() { return this.$linetype; }
  set Linetype(value) { if (value == null) throw new ArgumentNullException('value'); this.$linetype = this.OnLinetypeChangedEvent(this.$linetype, value); }
  OnLinetypeChangedEvent(oldValue, newValue) { const e = new TableObjectChangedEventArgs(oldValue, newValue); this.LinetypeChanged.Invoke(this, e); return e.NewValue; }
  get Color() { return this.$color; }
  set Color(value) {
    if (value == null) throw new ArgumentNullException('value');
    if (value.IsByLayer || value.IsByBlock) throw new ArgumentException('The layer color cannot be ByLayer or ByBlock', 'value'); this.$color = value;
  }
  get Lineweight() { return this.$lineweight; }
  set Lineweight(value) {
    if (value === Lineweight.ByLayer || value === Lineweight.ByBlock) throw new ArgumentException('The lineweight of a layer cannot be set to ByLayer or ByBlock.', 'value'); this.$lineweight = value;
  }
  get Transparency() { return this.$transparency; }
  set Transparency(value) { if (value == null) throw new ArgumentNullException('value'); this.$transparency = value; this.$assigned = true; }
  get HasTransparencyAssignment() { return this.$assigned; }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new Layer(newName); Object.assign(copy, { Color: this.Color.Clone(), IsVisible: this.IsVisible, IsFrozen: this.IsFrozen, IsLocked: this.IsLocked,
      Plot: this.Plot, Linetype: this.Linetype.Clone(), Lineweight: this.Lineweight });
    // The pinned clone omits Description and preserves whether transparency was assigned.
    copy.$transparency = this.Transparency.Clone(); copy.$assigned = this.$assigned;
    for (const data of this.XData.Values) copy.XData.Add(data.Clone()); return copy;
  }
}
