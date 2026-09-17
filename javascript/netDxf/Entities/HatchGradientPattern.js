// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native mirror of the pinned netDxf/Entities/HatchGradientPattern.cs.
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
import { AciColor } from '../AciColor.js';
import { HatchPattern } from './HatchPattern.js';
import { HatchGradientPatternType } from './HatchGradientPatternType.js';
function unitInterval(value, name, kind) {
  if (!Number.isFinite(value) || value < 0 || value > 1)
    throw new ArgumentOutOfRangeException(name, value, `The gradient ${kind} must be finite and between zero and one.`);
}
function color(value, name) { if (value == null) throw new ArgumentNullException(name); return value; }
function short(value) { return value === null ? null : RequireInteger(value, -32768, 32767); }

export class HatchGradientPattern extends HatchPattern {
  $color1; $color2; $singleColor = false; $tint = 1; $shift = 0;
  $gradientType = HatchGradientPatternType.Linear;
  $color1AciIndex = null; $color2AciIndex = null;
  $color1AciIndexAutomatic = true; $color2AciIndexAutomatic = true;
  constructor(...args) {
    const defaults = args.length <= 1;
    const stored = args.length === 5 && typeof args[2] === 'boolean';
    if (!defaults && !stored && args.length !== 3 && args.length !== 4) throw new ArgumentException('No matching HatchGradientPattern constructor.');
    super('SOLID', defaults ? args[0] : stored ? '' : args[3]);
    if (defaults) { this.$color1 = AciColor.Blue; this.$color2 = AciColor.Yellow; return; }
    if (!stored && typeof args[1] === 'number') {
      this.$color1 = color(args[0], 'color'); unitInterval(args[1], 'tint', 'tint');
      this.$color2 = this.$color2FromTint(args[1]); this.$singleColor = true; this.$tint = args[1]; this.$gradientType = args[2];
    } else {
      this.$color1 = color(args[0], 'color1'); this.$color2 = color(args[1], 'color2');
      this.$gradientType = stored ? args[4] : args[2];
      if (stored) { unitInterval(args[3], 'tint', 'tint'); this.$singleColor = args[2]; this.$tint = args[3]; }
    }
  }
  get GradientType() { return this.$gradientType; }
  set GradientType(value) { this.$gradientType = value; }
  get Color1() { return this.$color1; }
  set Color1(value) { this.$color1 = color(value, 'value'); }
  get Color2() { return this.$color2; }
  set Color2(value) { this.$color2 = color(value, 'value'); this.$singleColor = false; }
  get Color1AciIndex() { return this.$color1AciIndexAutomatic ? this.$color1.Index : this.$color1AciIndex; }
  set Color1AciIndex(value) { this.$color1AciIndex = short(value); this.$color1AciIndexAutomatic = false; }
  get Color2AciIndex() { return this.$color2AciIndexAutomatic ? this.$color2.Index : this.$color2AciIndex; }
  set Color2AciIndex(value) { this.$color2AciIndex = short(value); this.$color2AciIndexAutomatic = false; }
  get IsColor1AciIndexAutomatic() { return this.$color1AciIndexAutomatic; }
  get IsColor2AciIndexAutomatic() { return this.$color2AciIndexAutomatic; }
  ResetColor1AciIndex() { this.$color1AciIndex = null; this.$color1AciIndexAutomatic = true; }
  ResetColor2AciIndex() { this.$color2AciIndex = null; this.$color2AciIndexAutomatic = true; }
  get SingleColor() { return this.$singleColor; }
  set SingleColor(value) { if (value) this.$color2 = this.$color2FromTint(this.$tint); this.$singleColor = value; }
  get Tint() { return this.$tint; }
  set Tint(value) {
    unitInterval(value, 'value', 'tint');
    if (this.$singleColor) this.$color2 = this.$color2FromTint(value);
    this.$tint = value;
  }
  get Shift() { return this.$shift; }
  set Shift(value) { unitInterval(value, 'value', 'shift'); this.$shift = value; }
  get Centered() { return this.$shift === 0; }
  set Centered(value) { this.$shift = value ? 0 : 1; }
  $color2FromTint(value) {
    const hsl = AciColor.ToHsl(this.$color1);
    return AciColor.FromHsl(hsl.X, hsl.Y, value);
  }
  Clone() {
    // The internal five-argument constructor restores authored RGB stops without
    // running editing setters. Copy backing metadata, not its automatic getters.
    const copy = new HatchGradientPattern(this.Color1.Clone(), this.Color2.Clone(), this.SingleColor, this.Tint, this.GradientType);
    copy.Description = this.Description; this.$copyPatternTo(copy); copy.Shift = this.Shift;
    copy.$color1AciIndex = this.$color1AciIndex; copy.$color2AciIndex = this.$color2AciIndex;
    copy.$color1AciIndexAutomatic = this.$color1AciIndexAutomatic; copy.$color2AciIndexAutomatic = this.$color2AciIndexAutomatic;
    return copy;
  }
}
