// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { EntityType } from './EntityType.js';
import { AciColor } from '../AciColor.js';
import { Transparency } from '../Transparency.js';
import { Lineweight } from '../Lineweight.js';
import { Layer } from '../Tables/Layer.js';
import { Linetype } from '../Tables/Linetype.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { Matrix4 } from '../Matrix4.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NotSupportedException } from '../../runtime/Errors.js';
import { CommonEntityData, InstallEntityCommonData } from './EntityObject.CommonData.js';
export class EntityObject extends DxfObject {
  #type; #color = AciColor.ByLayer; #layer = Layer.Default; #linetype = Linetype.ByLayer;
  #transparency = Transparency.ByLayer; #linetypeScale = 1; #normal = Vector3.UnitZ;
  #reactors = new ReferenceList(); #reactorView;
  Lineweight = Lineweight.ByLayer; IsVisible = true;
  constructor(type, dxfCode) {
    super(dxfCode);
    if (new.target === EntityObject) throw new NotSupportedException('EntityObject is abstract.');
    this.#type = type;
    const list = this.#reactors;
    this.#reactorView = Object.freeze({ get Count() { return list.Count; }, get length() { return list.Count; },
      get_Item(index) { return list.get_Item(index); }, GetEnumerator() { return list.GetEnumerator(); }, [Symbol.iterator]() { return list[Symbol.iterator](); } });
    Object.defineProperties(this, { CommonData: { value: new CommonEntityData() },
      LayerChanged: { value: new EventHook(), enumerable: true }, LinetypeChanged: { value: new EventHook(), enumerable: true } });
  }
  get Type() { return this.#type; }
  get Reactors() { return this.#reactorView; }
  AddReactor(value) { this.#reactors.Add(value); }
  RemoveReactor(value) { return this.#reactors.Remove(value); }
  get Color() { return this.#color; }
  set Color(value) { if (value == null) throw new ArgumentNullException('value'); this.#color = value; }
  get Layer() { return this.#layer; }
  set Layer(value) { if (value == null) throw new ArgumentNullException('value'); this.#layer = this.OnLayerChangedEvent(this.#layer, value); }
  get Linetype() { return this.#linetype; }
  set Linetype(value) { if (value == null) throw new ArgumentNullException('value'); this.#linetype = this.OnLinetypeChangedEvent(this.#linetype, value); }
  OnLayerChangedEvent(oldValue, newValue) { const e = new TableObjectChangedEventArgs(oldValue, newValue); this.LayerChanged.Invoke(this, e); return e.NewValue; }
  OnLinetypeChangedEvent(oldValue, newValue) { const e = new TableObjectChangedEventArgs(oldValue, newValue); this.LinetypeChanged.Invoke(this, e); return e.NewValue; }
  get Transparency() { return this.#transparency; }
  set Transparency(value) { if (value == null) throw new ArgumentNullException('value'); this.#transparency = value; }
  get LinetypeScale() { return this.#linetypeScale; }
  set LinetypeScale(value) { if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#linetypeScale = value; }
  get Normal() { return Copy(this.#normal); }
  set Normal(value) {
    // Keep the source's assignment-before-check order, including Normalize's NaN behavior.
    this.#normal = Vector3.Normalize(value);
    if (Vector3.IsZero(this.#normal)) throw new ArgumentException('The normal can not be the zero vector.', 'value');
  }
  TransformBy(transformation, translation) {
    if(transformation instanceof Matrix4 && translation === undefined) return this.TransformBy(...this.$transformArguments(transformation,translation));
    throw new NotSupportedException('Derived entity must implement TransformBy.');
  }
  Clone() { throw new NotSupportedException('Derived entity must implement Clone.'); }
  ToString() { return Object.keys(EntityType).find(name => EntityType[name] === this.#type) ?? String(this.#type); }
  /** Internal overload adapter: Matrix4's last row is intentionally ignored by the C# base. */
  $transformArguments(transformation, translation) {
    if (transformation instanceof Matrix4 && translation === undefined) return [new Matrix3(
      transformation.M11,transformation.M12,transformation.M13,transformation.M21,transformation.M22,transformation.M23,
      transformation.M31,transformation.M32,transformation.M33), new Vector3(transformation.M14,transformation.M24,transformation.M34)];
    if (!(transformation instanceof Matrix3) || !(translation instanceof Vector3)) throw new ArgumentException('Expected Matrix4 or Matrix3 and Vector3.');
    return [transformation,translation];
  }
  /** The setter order used by the pinned entity Clone implementations. */
  $copyEntityAttributes(copy) {
    copy.Layer = this.Layer.Clone(); copy.Linetype = this.Linetype.Clone(); copy.Color = this.Color.Clone();
    copy.Lineweight = this.Lineweight; copy.Transparency = this.Transparency.Clone(); copy.LinetypeScale = this.LinetypeScale;
    copy.Normal = this.Normal; copy.IsVisible = this.IsVisible; return copy;
  }
  $finishEntityClone(copy) {
    for (const data of this.XData.Values) copy.XData.Add(data.Clone());
    this.CopyCommonDataTo(copy); return copy;
  }
}
InstallEntityCommonData(EntityObject);
