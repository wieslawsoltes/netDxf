// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
export class DxfObjectPointer extends DxfDatabaseObject {
  constructor() { super('OBJECT_PTR'); }
  CloneShell() { return new DxfObjectPointer(); }
  ValidateDatabaseSchema(database,errors) { if(!(this.Owner instanceof DxfDictionary))errors.Add('OBJECT_PTR requires a dictionary owner.'); }
}
