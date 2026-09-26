// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDictionary } from './DxfDatabaseObject.js';
import { DxfSortentsTable } from './DxfSortentsTable.js';
import { HeaderVariable } from '../Header/HeaderVariable.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { ReferenceMap } from '../../runtime/DatabaseReferenceMap.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
export function InstallDatabaseContainers(Type) {
  Type.prototype.CloneExtensionDictionary = function(sourceOwner, destinationOwner, externalReferences = null) {
    if (sourceOwner == null) throw new ArgumentNullException('sourceOwner');
    if (destinationOwner == null) throw new ArgumentNullException('destinationOwner');
    const source = sourceOwner.ExtensionDictionary;
    if (source === null || source.Database === null) throw new ArgumentException('The source has no registered extension dictionary.', 'sourceOwner');
    this.CheckRegistered(destinationOwner);
    if (destinationOwner === this.Document.Layers || destinationOwner.ExtensionDictionary !== null) throw new InvalidOperationException('The destination extension-dictionary slot is occupied or reserved.');
    const mappings = ReferenceMap(externalReferences);
    if (mappings.has(sourceOwner) && mappings.get(sourceOwner) !== destinationOwner) throw new ArgumentException('The source owner mapping conflicts with the destination owner.', 'externalReferences');
    mappings.set(sourceOwner, destinationOwner);
    return this.CloneOwnershipGraph(source, destinationOwner, null, true, mappings);
  };
  Type.prototype.CreateSortentsTable = function(blockRecord, entries, enableRegeneration = true) {
    if (blockRecord == null) throw new ArgumentNullException('blockRecord');
    if (entries == null) throw new ArgumentNullException('entries');
    this.CheckRegistered(blockRecord);
    if (this.Document.DrawingVariables.AcadVer < 14) throw new NotSupportedException('SORTENTSTABLE authoring requires AutoCAD 2004 or later.');
    const table = new DxfSortentsTable(blockRecord);
    for (const entry of entries) { if (entry !== null) this.CheckRegistered(entry.Entity); table.Entries.Add(entry); }
    let extension = blockRecord.ExtensionDictionary;
    if (extension !== null && extension.Contains('ACAD_SORTENTS')) throw new InvalidOperationException('The block already has an ACAD_SORTENTS entry.');
    let sorting = null, flags = 0, output = {};
    if (enableRegeneration && this.Document.DrawingVariables.TryGetCustomVariable('$SORTENTS', output)) {
      sorting = output.value; const value = sorting.Value;
      if (sorting.GroupCode !== 280 || !(value instanceof BoxedScalar) || value.Type !== 'Int16' || value.Value < 0 || value.Value > 255) throw new InvalidOperationException('The existing SORTENTS header variable is malformed.');
      flags = value.Value;
    }
    if (extension === null) { extension = new DxfDictionary(); extension.Add('ACAD_SORTENTS', table); this.SetExtensionDictionary(blockRecord, extension); }
    else extension.Add('ACAD_SORTENTS', table);
    if (enableRegeneration) { const value = new BoxedScalar('Int16', flags | 16); if (sorting === null) this.Document.DrawingVariables.AddCustomVariable(new HeaderVariable('$SORTENTS', 280, value)); else sorting.Value = value; }
    return table;
  };
  Type.prototype.SetSpatialFilter = function(insert, filter) {
    if (insert == null) throw new ArgumentNullException('insert'); if (filter == null) throw new ArgumentNullException('filter'); this.CheckRegistered(insert);
    if (filter.Database !== null || filter.Owner !== null) throw new ArgumentException('The filter must be detached and unowned.', 'filter');
    let extension = insert.ExtensionDictionary, filters = null;
    if (extension !== null && extension.Contains('ACAD_FILTER')) { filters = extension.get_Item('ACAD_FILTER'); if (!(filters instanceof DxfDictionary)) throw new InvalidOperationException('ACAD_FILTER is not a dictionary.'); if (filters.Contains('SPATIAL')) throw new InvalidOperationException('The INSERT already has a SPATIAL filter.'); }
    const createdFilters = filters === null; if (createdFilters) filters = new DxfDictionary();
    const addedOwnerReactor = !filter.PersistentReactors.Contains(filters);
    try {
      if (addedOwnerReactor) filter.PersistentReactors.Add(filters); filters.Add('SPATIAL', filter);
      if (createdFilters) { if (extension === null) { extension = new DxfDictionary(); extension.Add('ACAD_FILTER', filters); this.SetExtensionDictionary(insert, extension); } else extension.Add('ACAD_FILTER', filters); }
    } catch (error) { if (filter.Database === null) { filter.Owner = null; if (addedOwnerReactor) filter.PersistentReactors.Remove(filters); if (createdFilters && filters.Database === null) filters.Remove('SPATIAL'); } throw error; }
  };
}
