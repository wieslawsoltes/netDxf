// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { LinetypeSegment } from './LinetypeSegment.js';
import { LinetypeSegmentType } from './LinetypeSegmentType.js';
import { LinetypeSegmentRotationType } from './LinetypeSegmentRotationType.js';
import { ShapeStyle } from './ShapeStyle.js';
import { TableObjectChangedEventArgs } from './TableObjectChangedEventArgs.js';
import { Vector2 } from '../Vector2.js';
import { MathHelper } from '../MathHelper.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentNullException } from '../../runtime/Errors.js';
export class LinetypeShapeSegment extends LinetypeSegment {
  $name; $style; $offset; $rotation;
  constructor(name, style, length = 1, offset = Vector2.Zero, rotationType = LinetypeSegmentRotationType.Relative, rotation = 0, scale = 1) {
    super(LinetypeSegmentType.Shape, length); if (name == null || name === '') throw new ArgumentNullException('name'); this.$name = name;
    if (style == null) throw new ArgumentNullException('style');
    this.$style = style; this.$offset = Copy(offset); this.RotationType = rotationType; this.$rotation = MathHelper.NormalizeAngle(rotation); this.Scale = scale;
    Object.defineProperty(this, 'ShapeStyleChanged', { value: new EventHook(), enumerable: true });
  }
  get Name() { return this.$name; }
  set Name(value) { if (value == null || value === '') throw new ArgumentNullException('value'); this.$name = value; }
  get Style() { return this.$style; }
  set Style(value) {
    if (value == null) throw new ArgumentNullException('value'); this.$style = this.OnShapeStyleChangedEvent(this.$style, value);
  }
  OnShapeStyleChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.ShapeStyleChanged.Invoke(this, e); return e.NewValue;
  }
  get Offset() { return Copy(this.$offset); }
  set Offset(value) { this.$offset = Copy(value); }
  get Rotation() { return this.$rotation; }
  set Rotation(value) { this.$rotation = MathHelper.NormalizeAngle(value); }
  Clone() { return new LinetypeShapeSegment(this.$name, this.$style.Clone(), this.Length, this.$offset, this.RotationType, this.$rotation, this.Scale); }
}
