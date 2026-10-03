// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { Light } from '../Entities/Light.js';
import { DxfVersion } from '../Header/DxfVersion.js';
import { CheckedCollection, CheckStoredName } from '../../runtime/CheckedCollection.js';
import { ArgumentException, ArgumentNullException, NullReferenceException, RequireInteger } from '../../runtime/Errors.js';
export class DxfLightListEntry {
  constructor(light, name) {
    if (light == null) throw new ArgumentNullException('light');
    CheckStoredName(name, 'name', { allowEmpty: true, nullIsArgumentNull: true });
    Object.defineProperties(this, { Light: { value: light, enumerable: true }, Name: { value: name, enumerable: true } });
    Object.freeze(this);
  }
}
export class DxfLightList extends DxfDatabaseObject {
  #version; #entries;
  constructor(storedVersion) {
    super('LIGHTLIST'); this.StoredVersion = storedVersion;
    this.#entries = new CheckedCollection(entry => {
      if (entry == null) throw new ArgumentNullException('entry');
      if (this.Database != null) this.Database.CheckRegistered(entry.Light);
    });
  }
  get StoredVersion() { return this.#version; } set StoredVersion(value) { this.#version = RequireInteger(value, -2147483648, 2147483647); }
  get Entries() { return this.#entries; }
  get DatabaseReferences() { return Array.from(this.#entries, entry => entry.Light); }
  CloneShell() { return new DxfLightList(this.StoredVersion); }
  CopyDatabaseReferencesTo(clone, resolve) {
    for (const entry of this.#entries) {
      const light = resolve(entry.Light);
      if (!(light instanceof Light)) throw new ArgumentException('A LIGHTLIST clone mapping must identify a LIGHT.');
      clone.Entries.Add(new DxfLightListEntry(light, entry.Name));
    }
  }
  ValidateDatabaseSchema(database, errors) {
    if (database == null) throw new NullReferenceException();
    if (database.Document.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) errors.Add('Typed LIGHTLIST storage requires DXF 2007 or later.');
  }
}
