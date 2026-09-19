// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from '../Tables/TableObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ImageResolutionUnits } from '../Units/ImageResolutionUnits.js';
import { SupportFileSystem, PathFileNameWithoutExtension } from '../../runtime/SupportFileSystem.js';
import { MultiplyDouble } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
function positive(value, parameter) {
  if (value <= 0) throw new ArgumentOutOfRangeException(parameter, value);
  return value;
}
function filePath(value, parameter) {
  if (value == null || value === '') throw new ArgumentNullException(parameter);
  if (SupportFileSystem.InvalidPathChars.includes(value[0])) throw new ArgumentException('File path contains invalid characters.', parameter);
  return value;
}
export class ImageDefinition extends TableObject {
  #file; #width; #height; #resolution = new DataView(new ArrayBuffer(16)); #units;
  constructor(...args) {
    if (args.length !== 6 && args.length !== 7) throw new ArgumentException('No matching ImageDefinition constructor.');
    const implicit = args.length === 6;
    const [file, width, horizontal, height, vertical, units] = args.slice(implicit ? 0 : 1);
    super(implicit ? (file == null ? null : PathFileNameWithoutExtension(file)) : args[0], DxfObjectCode.ImageDef, false);
    this.#file = filePath(file, 'file');
    this.#width = RequireInteger(width, 1, 2147483647, 'width');
    this.#height = RequireInteger(height, 1, 2147483647, 'height');
    this.#resolution.setFloat64(0, positive(horizontal, 'horizontalResolution'));
    this.#resolution.setFloat64(8, positive(vertical, 'verticalResolution'));
    this.#units = units;
  }
  get File() { return this.#file; } set File(value) { this.#file = filePath(value, 'value'); }
  get Width() { return this.#width; } set Width(value) { this.#width = RequireInteger(value, 1, 2147483647); }
  get Height() { return this.#height; } set Height(value) { this.#height = RequireInteger(value, 1, 2147483647); }
  get HorizontalResolution() { return this.#resolution.getFloat64(0); }
  set HorizontalResolution(value) { this.#resolution.setFloat64(0, positive(value, 'value')); }
  get VerticalResolution() { return this.#resolution.getFloat64(8); }
  set VerticalResolution(value) { this.#resolution.setFloat64(8, positive(value, 'value')); }
  get ResolutionUnits() { return this.#units; }
  set ResolutionUnits(value) {
    if (this.#units !== value) {
      if (value === ImageResolutionUnits.Centimeters) {
        this.#resolution.setFloat64(0, this.HorizontalResolution / 2.54);
        this.#resolution.setFloat64(8, this.VerticalResolution / 2.54);
      } else if (value === ImageResolutionUnits.Inches) {
        this.#resolution.setFloat64(0, MultiplyDouble(this.HorizontalResolution, 2.54));
        this.#resolution.setFloat64(8, MultiplyDouble(this.VerticalResolution, 2.54));
      }
    }
    this.#units = value;
  }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new ImageDefinition(newName, this.#file, this.#width, this.HorizontalResolution, this.#height, this.VerticalResolution, this.#units);
    for (const data of this.XData.Values) copy.XData.Add(data.Clone());
    return copy;
  }
}
