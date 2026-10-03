// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceView } from '../../runtime/DatabaseModel.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export class DxfOpaqueObject extends DxfDatabaseObject {
  #tags;
  constructor(code,tags) { super(code); this.#tags=ReadOnlyReferenceView(new ReferenceList(tags)); }
  get Tags() { return this.#tags; }
  CloneShell() { throw new NotSupportedException('Opaque object schemas cannot be safely cloned.'); }
}
