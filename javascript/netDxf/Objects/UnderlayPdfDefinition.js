// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { UnderlayDefinition } from './UnderlayDefinition.js';
import { UnderlayType } from './UnderlayType.js';
import { PathFileNameWithoutExtension } from '../../runtime/SupportFileSystem.js';
import { ArgumentException } from '../../runtime/Errors.js';
export class UnderlayPdfDefinition extends UnderlayDefinition {
  #value = '1';
  constructor(nameOrFile, file) {
    if (arguments.length !== 1 && arguments.length !== 2) throw new ArgumentException('No matching UnderlayPdfDefinition constructor.');
    const implicitName = arguments.length === 1;
    super(implicitName ? (nameOrFile == null ? null : PathFileNameWithoutExtension(nameOrFile)) : nameOrFile, implicitName ? nameOrFile : file, UnderlayType.PDF);
  }
  get Page() { return this.#value; }
  set Page(value) { this.#value = value == null || value === '' ? '' : value; }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new UnderlayPdfDefinition(newName, this.File);
    copy.Page = this.#value;
    for (const data of this.XData.Values) copy.XData.Add(data.Clone());
    return copy;
  }
}
