// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
import { PlotSettings } from './PlotSettings.js';
import { ArgumentNullException } from '../../runtime/Errors.js';
export class DxfPlotSettingsObject extends DxfDatabaseObject {
  #settings;
  constructor(settings = new PlotSettings()) {
    super('PLOTSETTINGS');
    if (settings === null) throw new ArgumentNullException('settings');
    this.#settings = settings.Clone();
  }
  get Settings() { return this.#settings; }
  get DatabaseReferences() {
    const owner = this;
    return { *[Symbol.iterator]() { if (owner.#settings.ShadePlotObject !== null) yield owner.#settings.ShadePlotObject; } };
  }
  CopyDatabaseReferencesTo(clone, resolve) { clone.Settings.ShadePlotObject = resolve(this.#settings.ShadePlotObject); }
  CloneShell() { return new DxfPlotSettingsObject(this.#settings); }
  ValidateDatabaseSchema(database, errors) {
    PlotSettings.ValidateValues(this.#settings, errors);
    if (!(this.Owner instanceof DxfDictionary)) errors.Add('PLOTSETTINGS requires a dictionary owner.');
  }
}
export class DxfWipeoutVariables extends DxfDatabaseObject {
  DisplayFrame = false;
  constructor() { super('WIPEOUTVARIABLES'); }
  CloneShell() { const copy = new DxfWipeoutVariables(); copy.DisplayFrame = this.DisplayFrame; return copy; }
  ValidateDatabaseSchema(database, errors) {
    if (!(this.Owner instanceof DxfDictionary)) errors.Add('WIPEOUTVARIABLES requires a dictionary owner.');
  }
}
