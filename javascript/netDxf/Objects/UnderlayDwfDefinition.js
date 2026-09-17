// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { UnderlayDefinition } from './UnderlayDefinition.js';
import { UnderlayType } from './UnderlayType.js';
import { PathFileNameWithoutExtension } from '../../runtime/SupportFileSystem.js';
import { ArgumentException } from '../../runtime/Errors.js';
export class UnderlayDwfDefinition extends UnderlayDefinition {
  constructor(nameOrFile, file) {
    if (arguments.length !== 1 && arguments.length !== 2) throw new ArgumentException('No matching UnderlayDwfDefinition constructor.');
    const implicitName = arguments.length === 1;
    super(implicitName ? (nameOrFile == null ? null : PathFileNameWithoutExtension(nameOrFile)) : nameOrFile, implicitName ? nameOrFile : file, UnderlayType.DWF);
  }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new UnderlayDwfDefinition(newName, this.File);
    for (const data of this.XData.Values) copy.XData.Add(data.Clone());
    return copy;
  }
}
