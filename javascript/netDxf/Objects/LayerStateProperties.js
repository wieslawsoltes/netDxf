// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { AciColor } from '../AciColor.js';
import { Transparency } from '../Transparency.js';
import { Linetype } from '../Tables/Linetype.js';
import { Layer } from '../Tables/Layer.js';
import { LayerPropertiesFlags as Flags } from './LayerPropertiesFlags.js';
import { LayerPropertiesRestoreFlags as Restore } from './LayerPropertiesRestoreFlags.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
const transferred = source => source.StoredAlphaValue === 0 ? Transparency.FromCadIndex(source.Value) : source.Clone();
const has = (bits, flag) => (bits & flag) === flag;
export class LayerStateProperties {
  #name; #linetype = Linetype.DefaultName;
  constructor(value) {
    if (value instanceof Layer) {
      this.#name = value.Name; this.Flags = 0;
      if (!value.IsVisible) this.Flags |= Flags.Hidden;
      if (value.IsFrozen) this.Flags |= Flags.Frozen;
      if (value.IsLocked) this.Flags |= Flags.Locked;
      if (value.Plot) this.Flags |= Flags.Plot;
      this.#linetype = value.Linetype.Name; this.Color = value.Color.Clone();
      this.Lineweight = value.Lineweight; this.Transparency = transferred(value.Transparency);
    } else {
      if (value == null || value === '') throw new ArgumentNullException('name');
      this.#name = value; this.Flags = Flags.Plot; this.Color = AciColor.Default;
      this.Lineweight = -3; this.Transparency = new Transparency(0);
    }
  }
  static CreateOverload(signature, value) {
    if (signature.split('.').at(-1) === 'Layer' && value == null) throw new NullReferenceException();
    return new LayerStateProperties(value);
  }
  get Name() { return this.#name; }
  get LinetypeName() { return this.#linetype; }
  set LinetypeName(value) { if (value == null || value === '') throw new ArgumentNullException('value'); this.#linetype = value; }
  CopyFrom(layer, options) {
    if (layer == null) throw new NullReferenceException();
    if (!OrdinalIgnoreCaseEquals(this.Name, layer.Name)) throw new ArgumentException('Only a layer with the same name can be copied.', 'layer');
    this.Flags = Flags.None;
    if (has(options, Restore.Hidden) && !layer.IsVisible) this.Flags |= Flags.Hidden;
    if (has(options, Restore.Frozen) && layer.IsFrozen) this.Flags |= Flags.Frozen;
    if (has(options, Restore.Locked) && layer.IsLocked) this.Flags |= Flags.Locked;
    if (has(options, Restore.Plot) && layer.Plot) this.Flags |= Flags.Plot;
    if (has(options, Restore.Linetype)) this.LinetypeName = layer.Linetype.Name;
    if (has(options, Restore.Color)) this.Color = layer.Color.Clone();
    if (has(options, Restore.Lineweight)) this.Lineweight = layer.Lineweight;
    if (has(options, Restore.Transparency)) this.Transparency = transferred(layer.Transparency);
  }
  CopyTo(layer, options) {
    if (layer == null) throw new NullReferenceException();
    if (!OrdinalIgnoreCaseEquals(this.Name, layer.Name)) throw new ArgumentException('Only a layer with the same name can be copied.', 'layer');
    if (has(options, Restore.Hidden)) layer.IsVisible = !has(this.Flags, Flags.Hidden);
    if (has(options, Restore.Frozen)) layer.IsFrozen = has(this.Flags, Flags.Frozen);
    if (has(options, Restore.Locked)) layer.IsLocked = has(this.Flags, Flags.Locked);
    if (has(options, Restore.Plot)) layer.Plot = has(this.Flags, Flags.Plot);
    if (has(options, Restore.Linetype)) layer.Linetype = layer.Owner?.Owner.Linetypes.get_Item(this.LinetypeName) ?? new Linetype(this.LinetypeName);
    if (has(options, Restore.Color)) { if (this.Color == null) throw new NullReferenceException(); layer.Color = this.Color.Clone(); }
    if (has(options, Restore.Lineweight)) layer.Lineweight = this.Lineweight;
    if (has(options, Restore.Transparency)) { if (this.Transparency == null) throw new NullReferenceException(); layer.Transparency = transferred(this.Transparency); }
  }
  CompareWith(layer) {
    if (layer == null) throw new NullReferenceException();
    const equals = (a, b) => new Intl.Collator('en-US', {sensitivity:'accent'}).compare(a,b) === 0;
    return equals(layer.Name, this.Name) && layer.IsVisible === !has(this.Flags, Flags.Hidden) &&
      layer.IsFrozen === has(this.Flags, Flags.Frozen) && layer.IsLocked === has(this.Flags, Flags.Locked) &&
      layer.Plot === has(this.Flags, Flags.Plot) && equals(layer.Linetype.Name, this.LinetypeName) &&
      layer.Color.Equals(this.Color) && layer.Lineweight === this.Lineweight && layer.Transparency.Equals(this.Transparency);
  }
  Clone() {
    if (this.Color == null || this.Transparency == null) throw new NullReferenceException();
    return Object.assign(new LayerStateProperties(this.Name), {Flags:this.Flags, LinetypeName:this.LinetypeName,
      Color:this.Color.Clone(), Lineweight:this.Lineweight, Transparency:this.Transparency.Clone()});
  }
}
