// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { SupportFileSystem, PathExtension } from '../../runtime/SupportFileSystem.js';
import { InvariantIgnoreCaseEquals } from '../../runtime/InvariantFloat.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, EndOfStreamException, IndexOutOfRangeException, OverflowException } from '../../runtime/Errors.js';
import { InstallShapeStyleFidelity } from './ShapeStyle.Fidelity.js';
class ShapeReader {
  constructor(bytes, parameter) {
    if (bytes == null) throw new ArgumentNullException('bytes');
    this.bytes = bytes; this.at = 0; const sentinel = this.ReadBytes(24);
    if (sentinel.length < 21) throw new IndexOutOfRangeException();
    if (String.fromCharCode(...sentinel.subarray(0, 21)) !== 'AutoCAD-86 shapes 1.0') throw new ArgumentException('Not a valid Shape binary file .SHX.', parameter);
    this.ReadInt16(); this.ReadInt16(); const count = this.ReadInt16();
    if (count < 0) throw new OverflowException();
    this.numbers = []; this.sizes = [];
    for (let i = 0; i < count; i++) { this.numbers.push(this.ReadInt16()); this.sizes.push(this.ReadInt16()); }
  }
  ReadByte() { if (this.at >= this.bytes.length) throw new EndOfStreamException(); return this.bytes[this.at++]; }
  ReadInt16() { return ((this.ReadByte() | this.ReadByte() << 8) << 16) >> 16; }
  ReadBytes(count) {
    if (count < 0) throw new ArgumentOutOfRangeException('count', count);
    const end = Math.min(this.at + count, this.bytes.length), value = this.bytes.subarray(this.at, end); this.at = end; return value;
  }
  ReadName() { let name = ''; for (let b; (b = this.ReadByte()) !== 0;) name += b <= 127 ? String.fromCharCode(b) : '?'; return name; }
}
export class ShapeStyle extends TableObject {
  $file; $size; $widthFactor; $obliqueAngle; $flags = 1; $generation = 0; $lastHeight = null;
  static get DefaultShapeFile() { return 'ltypeshp.shx'; }
  static get Default() { return new ShapeStyle('ltypeshp', ShapeStyle.DefaultShapeFile); }
  constructor(name, file, size = 0, widthFactor = 1, obliqueAngle = 0) {
    super(name, DxfObjectCode.TextStyle, true); ShapeStyle.$validatePath(file, 'file');
    this.$file = file; this.$size = size; this.$widthFactor = widthFactor; this.$obliqueAngle = obliqueAngle;
  }
  static $validatePath(file, parameter) {
    if (file == null || file === '') throw new ArgumentNullException(parameter);
    // Preserve the pinned body's IndexOfAny(...) == 0 guard rather than silently broadening it.
    if (SupportFileSystem.InvalidPathChars.includes(file[0])) throw new ArgumentException('File path contains invalid characters.', parameter);
  }
  get File() { return this.$file; }
  set File(value) { ShapeStyle.$validatePath(value, 'value'); this.$file = value; }
  get Size() { return this.$size; }
  get WidthFactor() { return this.$widthFactor; }
  get ObliqueAngle() { return this.$obliqueAngle; }
  static NamesFromFile(file) {
    if (file == null || file === '') throw new ArgumentNullException('file');
    if (!InvariantIgnoreCaseEquals(PathExtension(file), '.SHX')) throw new ArgumentException('The shape file must have the extension SHX.', 'file');
    return ShapeStyle.NamesFromBytes(SupportFileSystem.ReadAllBytes(file));
  }
  static NamesFromBytes(bytes) {
    const reader = new ShapeReader(bytes, 'file'), names = new ReferenceList();
    for (let i = 0; i < reader.numbers.length; i++) { const name = reader.ReadName(); names.Add(name); reader.ReadBytes(reader.sizes[i] - name.length - 1); }
    return names;
  }
  static ContainsShapeName(file, shapeName) { return Array.from(ShapeStyle.NamesFromFile(file)).some(name => InvariantIgnoreCaseEquals(name, shapeName)); }
  $resolvedFile() { return this.Owner !== null ? this.Owner.Owner.SupportFolders.FindFile(this.$file) : this.$file; }
  NamesFromShapeStyle() { const file = this.$resolvedFile(); return !file || !SupportFileSystem.Exists(file) ? new ReferenceList() : ShapeStyle.NamesFromFile(file); }
  ContainsShapeName(name) { return Array.from(this.NamesFromShapeStyle()).some(value => InvariantIgnoreCaseEquals(value, name)); }
  ShapeNumber(name) {
    if (name == null || name === '') return 0;
    const file = this.$resolvedFile(); if (!file || (this.Owner === null && !SupportFileSystem.Exists(file))) return 0;
    const reader = new ShapeReader(SupportFileSystem.ReadAllBytes(file), 'f');
    for (let i = 0; i < reader.numbers.length; i++) {
      const found = reader.ReadName(); if (InvariantIgnoreCaseEquals(name, found)) return reader.numbers[i];
      reader.ReadBytes(reader.sizes[i] - found.length - 1);
    }
    return 0;
  }
  ShapeName(number) {
    const file = this.$resolvedFile(); if (!file || (this.Owner === null && !SupportFileSystem.Exists(file))) return '';
    const reader = new ShapeReader(SupportFileSystem.ReadAllBytes(file), 'f'), index = reader.numbers.lastIndexOf(number);
    for (let i = 0; i < reader.numbers.length; i++) { const name = reader.ReadName(); if (i === index) return name; reader.ReadBytes(reader.sizes[i] - name.length - 1); }
    return '';
  }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new ShapeStyle(newName, this.$file, this.$size, this.$widthFactor, this.$obliqueAngle);
    Object.assign(copy, { Flags: this.Flags, TextGenerationFlags: this.TextGenerationFlags, LastHeight: this.LastHeight });
    for (const data of this.XData.Values) copy.XData.Add(data.Clone()); return copy;
  }
}
InstallShapeStyleFidelity(ShapeStyle);
