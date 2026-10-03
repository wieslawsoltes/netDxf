// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { LinetypeSegment } from './LinetypeSegment.js';
import { LinetypeSegmentType } from './LinetypeSegmentType.js';
import { LinetypeSegmentRotationType } from './LinetypeSegmentRotationType.js';
import { TextStyle } from './TextStyle.js';
import { TableObjectChangedEventArgs } from './TableObjectChangedEventArgs.js';
import { Vector2 } from '../Vector2.js';
import { MathHelper } from '../MathHelper.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ArgumentNullException } from '../../runtime/Errors.js';
export class LinetypeTextSegment extends LinetypeSegment {
  $text; $style; $offset; $rotation;
  constructor(text = '', style = TextStyle.Default, length = 1, offset = Vector2.Zero, rotationType = LinetypeSegmentRotationType.Relative, rotation = 0, scale = 1) {
    super(LinetypeSegmentType.Text, length); this.$text = text == null || text === '' ? '' : text;
    if (style == null) throw new ArgumentNullException('style');
    this.$style = style; this.$offset = Copy(offset); this.RotationType = rotationType; this.$rotation = MathHelper.NormalizeAngle(rotation); this.Scale = scale;
    Object.defineProperty(this, 'TextStyleChanged', { value: new EventHook(), enumerable: true });
  }
  get Text() { return this.$text; }
  set Text(value) { this.$text = value == null || value === '' ? '' : value; }
  get Style() { return this.$style; }
  set Style(value) {
    if (value == null) throw new ArgumentNullException('value'); this.$style = this.OnTextStyleChangedEvent(this.$style, value);
  }
  OnTextStyleChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.TextStyleChanged.Invoke(this, e); return e.NewValue;
  }
  get Offset() { return Copy(this.$offset); }
  set Offset(value) { this.$offset = Copy(value); }
  get Rotation() { return this.$rotation; }
  set Rotation(value) { this.$rotation = MathHelper.NormalizeAngle(value); }
  Clone() { return new LinetypeTextSegment(this.$text, this.$style.Clone(), this.Length, this.$offset, this.RotationType, this.$rotation, this.Scale); }
}
