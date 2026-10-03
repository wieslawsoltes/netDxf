// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { UnderlayDefinition } from './UnderlayDefinition.js';
import { UnderlayType } from './UnderlayType.js';
import { PathFileNameWithoutExtension } from '../../runtime/SupportFileSystem.js';
import { ArgumentException } from '../../runtime/Errors.js';
export class UnderlayDgnDefinition extends UnderlayDefinition {
  #value = 'Model';
  constructor(nameOrFile, file) {
    if (arguments.length !== 1 && arguments.length !== 2) throw new ArgumentException('No matching UnderlayDgnDefinition constructor.');
    const implicitName = arguments.length === 1;
    super(implicitName ? (nameOrFile == null ? null : PathFileNameWithoutExtension(nameOrFile)) : nameOrFile, implicitName ? nameOrFile : file, UnderlayType.DGN);
  }
  get Layout() { return this.#value; }
  set Layout(value) { this.#value = value; }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new UnderlayDgnDefinition(newName, this.File);
    copy.Layout = this.#value;
    for (const data of this.XData.Values) copy.XData.Add(data.Clone());
    return copy;
  }
}
