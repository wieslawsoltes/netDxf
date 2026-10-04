// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDictionary } from './DxfDatabaseObject.js';
import { DxfPlotSettingsObject, DxfWipeoutVariables } from './DxfOutputSettings.js';
import { PlotSettings } from './PlotSettings.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReferenceMap } from '../../runtime/DatabaseReferenceMap.js';
import { IsReservedDictionaryName } from '../../runtime/DatabaseModel.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException } from '../../runtime/Errors.js';
export function InstallDatabaseOutputSettings(Type) {
  Type.prototype.CloneObject = function(source, destination, name, externalReferences = null) {
    if (source == null) throw new ArgumentNullException('source'); if (destination == null) throw new ArgumentNullException('destination');
    if (source.Database === null) throw new ArgumentException('The source must be registered.', 'source');
    if (destination.Database !== this) throw new ArgumentException('The destination belongs to another database.', 'destination');
    DxfDictionary.ValidateName(name);
    if (destination.Contains(name) || destination === this.Root && IsReservedDictionaryName(name)) throw new ArgumentException('The destination name exists or is reserved.', 'name');
    const mappings = ReferenceMap(externalReferences);
    if (source.Owner !== null) { if (mappings.has(source.Owner) && mappings.get(source.Owner) !== destination) throw new ArgumentException('The source owner mapping conflicts with the destination dictionary.', 'externalReferences'); mappings.set(source.Owner, destination); }
    return this.CloneOwnershipGraph(source, destination, name, false, mappings);
  };
  Type.prototype.AddPlotSettings = function(name, settings) {
    DxfDictionary.ValidateName(name);
    const errors = new ReferenceList(); PlotSettings.ValidateValues(settings, errors);
    if (errors.Count > 0) throw new ArgumentException(Array.from(errors).join('; '), 'settings');
    if (settings.ShadePlotObject !== null) this.CheckRegistered(settings.ShadePlotObject);
    let dictionary = this.Root.Contains('ACAD_PLOTSETTINGS') ? this.Root.get_Item('ACAD_PLOTSETTINGS') : null;
    if (dictionary !== null && !(dictionary instanceof DxfDictionary)) throw new InvalidOperationException('ACAD_PLOTSETTINGS is not a dictionary.');
    const result = new DxfPlotSettingsObject(settings); result.Settings.PageSetupName = name;
    errors.Clear(); PlotSettings.ValidateValues(result.Settings, errors);
    if (errors.Count > 0) throw new ArgumentException(Array.from(errors).join('; '), 'name');
    if (dictionary === null) { dictionary = new DxfDictionary(); result.PersistentReactors.Add(dictionary); dictionary.Add(name, result); this.Root.Add('ACAD_PLOTSETTINGS', dictionary); }
    else { result.PersistentReactors.Add(dictionary); dictionary.Add(name, result); }
    return result;
  };
  Type.prototype.GetWipeoutVariables = function() { const value = this.Root.Contains('ACAD_WIPEOUT_VARS') ? this.Root.get_Item('ACAD_WIPEOUT_VARS') : null; return value instanceof DxfWipeoutVariables ? value : null; };
  Type.prototype.SetWipeoutVariables = function(displayFrame) {
    let result = this.GetWipeoutVariables();
    if (result === null) { if (this.Root.Contains('ACAD_WIPEOUT_VARS')) throw new InvalidOperationException('ACAD_WIPEOUT_VARS has an incompatible object.'); result = new DxfWipeoutVariables(); result.DisplayFrame = displayFrame; result.PersistentReactors.Add(this.Root); this.Root.Add('ACAD_WIPEOUT_VARS', result); }
    else result.DisplayFrame = displayFrame;
    return result;
  };
}
