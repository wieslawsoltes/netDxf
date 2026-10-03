// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { TextStyleFontData } from './TextStyleFontData.js';
import { TextStyleFlags } from './TextStyleFlags.js';
import { FontStyle } from './FontStyle.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { PathExtension } from '../../runtime/SupportFileSystem.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException } from '../../runtime/Errors.js';
import { InstallTextStyleFidelity } from './TextStyle.Fidelity.js';
import { InstallTextStyleReferenceRename } from './TextStyle.ReferenceRename.js';
export class TextStyle extends TableObject {
  $file = ''; $bigFont = ''; $height = 0; $obliqueAngle = 0; $widthFactor = 1;
  $flags = 0; $generation = 0; $lastHeight = null;
  static get DefaultName() { return 'Standard'; }
  static get DefaultFont() { return 'simplex.shx'; }
  static get Default() { return new TextStyle(TextStyle.DefaultName, TextStyle.DefaultFont); }
  constructor(name, font, fontStyleOrCheckName = true, checkName = true) {
    const family = typeof fontStyleOrCheckName === 'number';
    super(name, DxfObjectCode.TextStyle, family ? checkName : fontStyleOrCheckName);
    if (family) {
      if (font == null || font === '') throw new ArgumentNullException('fontFamily');
      this.ExtendedFontData = new TextStyleFontData(font, fontStyleOrCheckName << 24);
    } else {
      if (name == null || name === '') throw new ArgumentNullException('name');
      if (font == null || font === '') throw new ArgumentNullException('font');
      TextStyle.$fontExtension(font);
      this.IsReserved = OrdinalIgnoreCaseEquals(name, TextStyle.DefaultName); this.$file = font;
    }
  }
  static $fontExtension(value) {
    const extension = PathExtension(value);
    if (!OrdinalIgnoreCaseEquals(extension, '.TTF') && !OrdinalIgnoreCaseEquals(extension, '.SHX')) throw new ArgumentException('Only true type TTF fonts and ACAD compiled shape SHX fonts are allowed.');
  }
  get FontFile() { return this.$file; }
  set FontFile(value) {
    if (value == null || value === '') throw new ArgumentNullException('value');
    TextStyle.$fontExtension(value); this.ExtendedFontData = null; this.$bigFont = ''; this.$file = value;
  }
  get BigFont() { return this.$bigFont; }
  set BigFont(value) {
    if (value == null || value === '') { this.$bigFont = ''; return; }
    if (!this.$file || !OrdinalIgnoreCaseEquals(PathExtension(this.$file), '.SHX')) throw new NullReferenceException('The Big Font is only applicable for SHX Asian fonts.');
    if (!OrdinalIgnoreCaseEquals(PathExtension(value), '.SHX')) throw new ArgumentException('The Big Font is only applicable for SHX Asian fonts.', 'value');
    this.$bigFont = value;
  }
  get FontFamilyName() { return this.ExtendedFontData?.FamilyName ?? ''; }
  set FontFamilyName(value) {
    if (value == null || value === '') throw new ArgumentNullException('value');
    this.ExtendedFontData = new TextStyleFontData(value, 0); this.$file = ''; this.$bigFont = '';
  }
  get FontStyle() { return this.ExtendedFontData?.FontStyle ?? FontStyle.Regular; }
  set FontStyle(value) {
    const data = this.ExtendedFontData;
    if (data !== null) this.ExtendedFontData = new TextStyleFontData(data.FamilyName, (data.Flags & ~0x03000000) | ((value & 3) << 24));
  }
  get Height() { return this.$height; }
  set Height(value) { if (value < 0) throw new ArgumentOutOfRangeException('value', value); this.$height = value; }
  get WidthFactor() { return this.$widthFactor; }
  set WidthFactor(value) { if (value < 0.01 || value > 100) throw new ArgumentOutOfRangeException('value', value); this.$widthFactor = value; }
  get ObliqueAngle() { return this.$obliqueAngle; }
  set ObliqueAngle(value) { if (value < -85 || value > 85) throw new ArgumentOutOfRangeException('value', value); this.$obliqueAngle = value; }
  get IsVertical() { return (this.$flags & TextStyleFlags.Vertical) !== 0; }
  set IsVertical(value) { this.$flags = value ? this.$flags | TextStyleFlags.Vertical : this.$flags & ~TextStyleFlags.Vertical; }
  get IsBackward() { return (this.TextGenerationFlags & 2) !== 0; }
  set IsBackward(value) { this.TextGenerationFlags = value ? this.TextGenerationFlags | 2 : this.TextGenerationFlags & ~2; }
  get IsUpsideDown() { return (this.TextGenerationFlags & 4) !== 0; }
  set IsUpsideDown(value) { this.TextGenerationFlags = value ? this.TextGenerationFlags | 4 : this.TextGenerationFlags & ~4; }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new TextStyle(newName, TextStyle.DefaultFont);
    copy.$file = this.$file; copy.$bigFont = this.$bigFont;
    Object.assign(copy, { Height: this.$height, Flags: this.Flags, TextGenerationFlags: this.TextGenerationFlags,
      LastHeight: this.LastHeight, ObliqueAngle: this.$obliqueAngle, WidthFactor: this.$widthFactor });
    for (const data of this.XData.Values) copy.XData.Add(data.Clone());
    return copy;
  }
}
InstallTextStyleFidelity(TextStyle);
InstallTextStyleReferenceRename(TextStyle);
