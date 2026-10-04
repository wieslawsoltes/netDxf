// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { DatabaseRegistry } from '../../runtime/RegisteredDatabaseState.js';
import { ReferenceMap } from '../../runtime/DatabaseReferenceMap.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { ValidateDeclaredOwnership } from './DxfDeclaredOwnership.js';
import { ErasureHandle, ErasureReference } from './DxfObjectDatabase.Erase.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const is = (item, name) => typeof api[name] === 'function' && item instanceof api[name];
const nullHandle = value => /^[0]+$/.test(value);
const handleTarget = (database, handle) => {
  const target = database.Document.GetObjectByHandle(handle);
  if (target === null) throw new InvalidOperationException('Cannot clone an unresolved section metadata handle: ' + handle);
  return target;
};
/** Original SECTION ownership APIs. Planning runs against detached copies before registration. */
export function InstallDatabaseSection(Type) {
  Type.prototype.SetSectionSettings = function(section, settings) {
    if (section == null) throw new ArgumentNullException('section');
    if (settings == null) throw new ArgumentNullException('settings');
    this.CheckRegistered(section); section.Validate(this.Document);
    if (section.GeometrySettings !== null) throw new InvalidOperationException('The section already owns geometry settings.');
    if (settings.Database !== null || settings.Owner !== null) throw new ArgumentException('Section settings must be detached and unowned.', 'settings');
    settings.ValidateValues(); this.PrepareTarget(settings);
    settings.Owner = section; section.GeometrySettings = settings; section.HasSettingsField = true;
  };
  Type.prototype.CloneSection = function(source, destination, externalReferences = null) {
    if (source == null) throw new ArgumentNullException('source');
    if (destination == null) throw new ArgumentNullException('destination');
    const external = ReferenceMap(externalReferences);
    this.CheckRegistered(destination.Record);
    if ((destination.Flags & api.BlockTypeFlags.ExternallyDependent) !== 0) throw new ArgumentException('An externally dependent block cannot receive a section.', 'destination');
    if (this.Document.DrawingVariables.AcadVer < api.DxfVersion.AutoCad2007) throw new NotSupportedException('SECTION requires R2007 or later.');
    const sourceDatabase = source.GeometrySettings?.Database ?? source.Owner?.Record.Owner?.Owner.Objects;
    if (sourceDatabase == null) throw new ArgumentException('The source section must be registered.', 'source');
    sourceDatabase.CheckRegistered(source); source.Validate(sourceDatabase.Document);
    const sourceErrors = sourceDatabase.Validate();
    if (sourceErrors.Count !== 0) throw new InvalidOperationException('Cannot clone an invalid section database: ' + Array.from(sourceErrors).join('; '));
    const originals = Array.from(DatabaseRegistry(sourceDatabase).Values).filter(item => Type.IsAncestor(source,item));
    const copy = source.CloneValues(false), map = new Map([[source,copy]]);
    for (const original of originals) map.set(original,original.CloneShell());
    if (source.Owner !== null) { map.set(source.Owner,destination); map.set(source.Owner.Record,destination.Record); }
    for (const [key,value] of external) if (map.has(key) && map.get(key) !== value) throw new ArgumentException('An explicit reference mapping conflicts with the cloned ownership graph.', 'externalReferences');
    const resolve = value => {
      if (value === null) return null;
      if (map.has(value)) return map.get(value);
      if (external.has(value)) { this.CheckRegistered(external.get(value)); return external.get(value); }
      if (sourceDatabase === this) { this.CheckRegistered(value); return value; }
      throw new InvalidOperationException('An external section reference needs an explicit destination mapping: ' + value.Handle);
    };
    const layer = resolve(source.Layer);
    if (!(layer instanceof api.Layer)) throw new ArgumentException('The section layer mapping must target a layer.');
    copy.Layer = layer;
    const linetype = resolve(source.Linetype);
    if (!(linetype instanceof api.Linetype)) throw new ArgumentException('The section linetype mapping must target a linetype.');
    copy.Linetype = linetype; copy.GeometrySettings = resolve(source.GeometrySettings);
    const carriers = [source,...originals];
    for (const original of carriers) {
      const clone = map.get(original);
      if (original instanceof api.DxfDatabaseObject) {
        clone.Owner = resolve(original.Owner); original.CopyDatabaseReferencesTo(clone,resolve);
        if (original instanceof api.DxfDictionary) for (const entry of original.Entries) clone.AddLoaded(entry.Name,resolve(entry.Target),entry.IsHardOwner);
        if (original instanceof api.DxfDictionaryWithDefault) clone.Default = resolve(original.Default);
        for (const data of original.XData.Values) clone.XData.Add(data.CopyStoredGraph());
      }
      clone.ExtensionDictionary = resolve(original.ExtensionDictionary);
      for (const reactor of original.PersistentReactors) clone.PersistentReactors.Add(resolve(reactor));
      if (original instanceof api.EntityObject) for (const reactor of original.Reactors) clone.AddReactor(resolve(reactor));
      for (const data of original.XData.Values) for (const tag of data.XDataRecord)
        if (tag.Code === api.XDataCode.DatabaseHandle && !nullHandle(tag.Value)) resolve(handleTarget(sourceDatabase,tag.Value));
      if (original instanceof api.DxfXRecord) for (const tag of original.Data)
        if (Type.IsReference(tag) && !nullHandle(tag.Value)) resolve(handleTarget(sourceDatabase,tag.Value));
    }
    const errors = new ReferenceList(), clonedObjects = originals.map(item => map.get(item));
    for (const clone of clonedObjects) { clone.ValidateDatabaseSchema(this,errors); ValidateDeclaredOwnership(this,clone,clonedObjects,errors,false); }
    if (errors.Count !== 0) throw new InvalidOperationException('Invalid cloned section graph: ' + Array.from(errors).join('; '));
    let candidate = this.Document.NumHandles; const registries = new Set();
    for (const original of carriers) {
      const clone = map.get(original);
      if (clone instanceof api.DxfDatabaseObject) for (const tag of clone.AllocationReservations) candidate = this.GetReservedSeed(tag,candidate);
      if (clone instanceof api.DxfXRecord) for (const tag of clone.Data) candidate = this.GetReservedSeed(tag,candidate);
      for (const data of clone.XData.Values) {
        if (!this.Document.ApplicationRegistries.Contains(data.ApplicationRegistry.Name)) registries.add(OrdinalIgnoreCaseKey(data.ApplicationRegistry.Name));
        for (const tag of data.XDataRecord) if (tag.Code === api.XDataCode.DatabaseHandle) candidate = this.GetReservedSeed(new api.DxfTag(1005,tag.Value),candidate);
      }
    }
    if (candidate <= 0n || candidate > 9223372036854775807n - BigInt(carriers.length + registries.size + 1)) throw new InvalidOperationException('The section graph exceeds the available handle range.');
    for (const original of carriers) map.get(original).Handle = (candidate++).toString(16).toUpperCase();
    for (const original of carriers) {
      const clone = map.get(original);
      for (const data of original.XData.Values) for (let i=0;i<data.XDataRecord.Count;i++) {
        const tag = data.XDataRecord.get_Item(i);
        if (tag.Code === api.XDataCode.DatabaseHandle && !nullHandle(tag.Value)) clone.XData.get_Item(data.ApplicationRegistry.Name).XDataRecord.set_Item(i,new api.XDataRecord(api.XDataCode.DatabaseHandle,resolve(handleTarget(sourceDatabase,tag.Value)).Handle));
      }
      if (original instanceof api.DxfXRecord) for (let i=0;i<original.Data.Count;i++) {
        const tag = original.Data.get_Item(i);
        if (Type.IsReference(tag) && !nullHandle(tag.Value)) clone.ReplaceLoadedData(i,new api.DxfTag(tag.Code,resolve(handleTarget(sourceDatabase,tag.Value)).Handle));
      }
    }
    this.Document.NumHandles = candidate; copy.PendingInputReferences = true;
    this.Document.AddEntityToDocument(copy,false);
    for (const clone of clonedObjects) this.Register(clone,true);
    for (const clone of clonedObjects) clone.MaterializeOwnedObjectReferences();
    destination.AddPreparedSection(copy); copy.PendingInputReferences = false; return copy;
  };
  Type.prototype.EraseSection = function(section) {
    if (section == null) throw new ArgumentNullException('section');
    this.CheckRegistered(section); section.Validate(this.Document);
    const carriers = this.ErasureCarriers(), deleted = new Set([section]);
    const tree = Array.from(DatabaseRegistry(this).Values).filter(item => Type.IsAncestor(section,item));
    for (const item of tree) {
      if (['DxfStoredTableContent','DxfStoredSunStudy','DxfStoredTableGeometry','DxfStoredCellStyleMap','DxfStoredField','DxfStoredDimAssoc','DxfStoredSectionManager'].some(name => is(item,name))) throw new NotSupportedException('A stored section-owned object requires its complete application lifecycle.');
      if (item instanceof api.DxfOpaqueObject) throw new NotSupportedException('An opaque section-owned object requires its application schema before erasure.');
      this.CheckRegistered(item); deleted.add(item);
    }
    for (const item of carriers) if (Type.IsAncestor(section,item) && !deleted.has(item)) throw new NotSupportedException('The section owns an unsupported managed object.');
    const handles = new Set(Array.from(deleted,item => ErasureHandle(item.Handle)));
    for (const item of carriers) {
      if (deleted.has(item)) continue;
      const reference = (target,field) => { if (target !== null && deleted.has(target)) throw ErasureReference(item,field,target.Handle); };
      const handle = (target,field) => { if (handles.has(ErasureHandle(target))) throw ErasureReference(item,field,target); };
      reference(item.Owner,'owner'); reference(item.ExtensionDictionary,'extension dictionary');
      for (const reactor of item.PersistentReactors) reference(reactor,'persistent reactor');
      if (item instanceof api.EntityObject) for (const reactor of item.Reactors) reference(reactor,'entity reactor');
      for (const data of item.XData.Values) for (const tag of data.XDataRecord) if (tag.Code === api.XDataCode.DatabaseHandle) handle(tag.Value,'XData 1005');
      if (item instanceof api.DxfDatabaseObject) for (const target of item.DatabaseReferences) reference(target,'typed reference');
      if (item instanceof api.DxfDictionary) for (const entry of item.Entries) reference(entry.Target,'dictionary entry');
      if (item instanceof api.DxfDictionaryWithDefault) reference(item.Default,'dictionary default');
      if (item instanceof api.DxfXRecord) for (const tag of item.Data) if (Type.IsReference(tag)) handle(tag.Value,'XRECORD reference');
      if (item instanceof api.DxfOpaqueObject) for (const tag of item.Tags) if (tag.ValueType === api.DxfTagValueType.Handle) handle(tag.Value,'opaque handle');
      if (item instanceof api.Section) reference(item.GeometrySettings,'section settings');
      if (item instanceof api.View) reference(item.LiveSection,'VIEW live section 334');
      for (const [name,prefix] of [['Polyline3DRecord','polyline'],['PolygonMeshRecord','polygon mesh'],['PolyfaceMeshRecord','polyface'],['Polyline2DRecord','legacy 2D']])
        if (is(item,name)) { for (const target of item.References) reference(target,prefix+' record reference'); for (const tag of item.OpaqueHandleTags) handle(tag.Value,prefix+' record handle'); }
      if (item instanceof api.PolyfaceMesh) for (const tag of item.StoredHeaderReferences) handle(tag.Value,'polyface header handle');
      if (is(item,'DxfOpaqueEntity')) for (const target of item.References) reference(target,'unknown entity reference');
      if (item instanceof api.Polyline2D) for (const tag of item.StoredHeaderReferences) handle(tag.Value,'legacy 2D header handle');
      if (is(item,'StoredTable')) for (const target of item.References) reference(target,'ACAD_TABLE reference');
      if (item instanceof api.MultiLeader) for (const data of item.Data) for (const target of data.References) reference(target,'MULTILEADER reference');
      if (item instanceof api.Layout) reference(item.PlotSettings?.ShadePlotObject ?? null,'layout shade plot');
    }
    for (const variable of this.Document.DrawingVariables.CustomValues()) {
      const kind = api.DxfGroupCode.GetHandleKind(variable.GroupCode), value = variable.Value instanceof api.BoxedString ? variable.Value.Value : variable.Value;
      if (kind !== api.DxfHandleKind.None && kind !== api.DxfHandleKind.Arbitrary && variable.Name !== '$HANDSEED' && typeof value === 'string' && handles.has(ErasureHandle(value))) throw ErasureReference(this.Document,'header '+variable.Name,value);
    }
    for (const item of deleted) for (const data of item.XData.Values)
      if (!this.Document.ApplicationRegistries.References.ContainsKey(data.ApplicationRegistry.Name)) throw new InvalidOperationException('Section erasure found invalid APPID bookkeeping.');
    const owner = section.Owner, originalHandle = section.Handle;
    if (owner === null || !owner.Entities.Contains(section)) throw new InvalidOperationException('The section has inconsistent block membership.');
    for (const item of tree) { this.Document.AddedObjects.Remove(item.Handle); DatabaseRegistry(this).Remove(item.Handle); item.Database = null; item.IsErased = true; }
    owner.RemovePreparedSection(section); this.Document.RemoveEntityFromDocument(section);
    section.Handle = originalHandle; section.IsErased = true;
  };
}
