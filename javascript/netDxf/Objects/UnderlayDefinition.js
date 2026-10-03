// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from '../Tables/TableObject.js';
import { UnderlayType } from './UnderlayType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { SupportFileSystem, PathExtension } from '../../runtime/SupportFileSystem.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, NotSupportedException } from '../../runtime/Errors.js';
const kinds = new Map([
  [UnderlayType.DGN, ['.DGN', DxfObjectCode.UnderlayDgnDefinition]],
  [UnderlayType.DWF, ['.DWF', DxfObjectCode.UnderlayDwfDefinition]],
  [UnderlayType.PDF, ['.PDF', DxfObjectCode.UnderlayPdfDefinition]],
]);
export class UnderlayDefinition extends TableObject {
  #type; #file;
  constructor(name, file, type) {
    super(name, DxfObjectCode.UnderlayDefinition, false);
    if (new.target === UnderlayDefinition) throw new NotSupportedException('UnderlayDefinition is abstract.');
    this.#type = type; this.#setFile(file, 'file');
  }
  #setFile(file, parameter) {
    if (file == null || file === '') throw new ArgumentNullException(parameter);
    // The pinned source checks IndexOfAny(...) == 0, not every position.
    if (SupportFileSystem.InvalidPathChars.includes(file[0])) throw new ArgumentException('File path contains invalid characters.', parameter);
    const kind = kinds.get(this.#type);
    if (kind) {
      if (!OrdinalIgnoreCaseEquals(PathExtension(file), kind[0])) throw new ArgumentException('The underlay type and the file extension do not match.', parameter);
      this.CodeName = kind[1];
    }
    this.#file = file;
  }
  get Type() { return this.#type; }
  get File() { return this.#file; }
  set File(value) { this.#setFile(value, 'value'); }
}
