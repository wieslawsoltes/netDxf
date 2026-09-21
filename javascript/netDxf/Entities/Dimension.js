// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector2 } from '../Vector2.js';
import { MathHelper } from '../MathHelper.js';
import { DimensionStyle } from '../Tables/DimensionStyle.js';
import { DimensionStyleOverrideDictionary } from '../Collections/DimensionStyleOverrideDictionary.js';
import { DimensionStyleOverrideChangeEventArgs } from '../Tables/DimensionStyleOverrideChangeEventArgs.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { MTextAttachmentPoint } from './MTextAttachmentPoint.js';
import { MTextLineSpacingStyle } from './MTextLineSpacingStyle.js';
import { EventHook } from '../../runtime/EventHook.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { BoxedString } from '../../runtime/BoxedString.js';
import { ArgumentNullException, ArgumentOutOfRangeException, NotSupportedException } from '../../runtime/Errors.js';
/** Base DIMENSION entity. Derived source mirrors implement measurement and block generation. */
export class Dimension extends EntityObject {
  $defPoint = Vector2.Zero; $textRefPoint = Vector2.Zero;
  #style = DimensionStyle.Default; #dimensionType; #block = null;
  #overrides = new DimensionStyleOverrideDictionary();
  #textRotation = 0; #userText = ''; #lineSpacing = 1;
  AttachmentPoint = MTextAttachmentPoint.MiddleCenter;
  LineSpacingStyle = MTextLineSpacingStyle.AtLeast;
  Elevation = 0; TextPositionManuallySet = false;
  constructor(type) {
    super(EntityType.Dimension, DxfObjectCode.Dimension);
    if (new.target === Dimension) throw new NotSupportedException('Dimension is abstract.');
    this.#dimensionType = type;
    for (const name of ['DimensionStyleChanged', 'DimensionBlockChanged', 'DimensionStyleOverrideAdded', 'DimensionStyleOverrideRemoved'])
      Object.defineProperty(this, name, { value: new EventHook(), enumerable: true });
    this.#overrides.BeforeAddItem.Add((sender, e) => {
      const old = {};
      if (sender.TryGetValue(e.Item.Type, old) && Dimension.SameOverrideValue(old.value, e.Item)) e.Cancel = true;
    });
    this.#overrides.AddItem.Add((_, e) => this.OnDimensionStyleOverrideAddedEvent(e.Item));
    this.#overrides.BeforeRemoveItem.Add(() => {});
    this.#overrides.RemoveItem.Add((_, e) => this.OnDimensionStyleOverrideRemovedEvent(e.Item));
  }
  static SameOverrideValue(old, item) {
    if (old === item) return true;
    const a = old.Value, b = item.Value;
    if (a === '' || a instanceof BoxedString && a.Value === '') return b === '' || b instanceof BoxedString && b.Value === '';
    return a === null ? b === null : typeof a === 'object' && a === b;
  }
  get DefinitionPoint() { return Copy(this.$defPoint); }
  set DefinitionPoint(value) { this.$defPoint = Copy(value); }
  get TextReferencePoint() { return Copy(this.$textRefPoint); }
  set TextReferencePoint(value) { this.TextPositionManuallySet = true; this.$textRefPoint = Copy(value); }
  get Style() { return this.#style; }
  set Style(value) { if (value == null) throw new ArgumentNullException('value'); this.#style = this.OnDimensionStyleChangedEvent(this.#style, value); }
  get StyleOverrides() { return this.#overrides; }
  get DimensionType() { return this.#dimensionType; }
  get LineSpacingFactor() { return this.#lineSpacing; }
  set LineSpacingFactor(value) { if (value < 0.25 || value > 4) throw new ArgumentOutOfRangeException('value', value); this.#lineSpacing = value; }
  get Block() { return this.#block; }
  set Block(value) { this.#block = this.OnDimensionBlockChangedEvent(this.#block, value); }
  get TextRotation() { return this.#textRotation; }
  set TextRotation(value) { this.#textRotation = MathHelper.NormalizeAngle(value); }
  get UserText() { return this.#userText; }
  set UserText(value) { this.#userText = value == null || value === '' ? '' : value; }
  OnDimensionStyleChangedEvent(oldValue, newValue) { const e = new TableObjectChangedEventArgs(oldValue, newValue); this.DimensionStyleChanged.Invoke(this, e); return e.NewValue; }
  OnDimensionBlockChangedEvent(oldValue, newValue) { const e = new TableObjectChangedEventArgs(oldValue, newValue); this.DimensionBlockChanged.Invoke(this, e); return e.NewValue; }
  OnDimensionStyleOverrideAddedEvent(item) { this.DimensionStyleOverrideAdded.Invoke(this, new DimensionStyleOverrideChangeEventArgs(item)); }
  OnDimensionStyleOverrideRemovedEvent(item) { this.DimensionStyleOverrideRemoved.Invoke(this, new DimensionStyleOverrideChangeEventArgs(item)); }
  Update() {
    this.CalculateReferencePoints();
    if (this.#block !== null) {
      const replacement = this.BuildBlock(this.#block.Name);
      this.#block = this.OnDimensionBlockChangedEvent(this.#block, replacement);
    }
  }
}
