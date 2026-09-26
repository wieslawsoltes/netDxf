// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredSectionManager } from './DxfStoredSectionManager.js';
import { XDataCode } from '../XDataCode.js';
import { DatabaseRegistry } from '../../runtime/RegisteredDatabaseState.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const maximumHandle = 9223372036854775807n;
export function InstallDatabaseSectionManager(Type) {
  const states = new WeakMap();
  const state = db => { if (!states.has(db)) states.set(db, {creating:false, reentered:false}); return states.get(db); };
  Type.prototype.CreateSectionManager = function(sections, requiresFullUpdate) {
    const status = state(this);
    if (status.creating) { status.reentered = true; throw new InvalidOperationException('Section-manager creation cannot be reentered.'); }
    if (sections == null) throw new ArgumentNullException('sections');
    status.creating = true; status.reentered = false;
    try {
      const members = [];
      ConsumeManagedEnumerable(sections, section => {
        if (members.length === DxfStoredSectionManager.MaximumSections) throw new ArgumentException('The section-manager membership exceeds 65536 entries.', 'sections');
        members.push(section);
      });
      if (status.reentered) throw new InvalidOperationException('A recursive creation attempt invalidated section-manager creation.');
      if (![15, 16, 17, 18].includes(this.Document.DrawingVariables.AcadVer)) throw new NotSupportedException('SECTION_MANAGER creation requires an R2007 through R2018 typed profile.');
      if (this.Root.IsErased || this.Root.Database !== this || !this.IsRegistered(this.Root) || this.Root.Owner !== this.Document)
        throw new InvalidOperationException("The section manager requires this document's registered named-object root.");
      if (this.Root.Contains('ACAD_SECTION_MANAGER') || Array.from(DatabaseRegistry(this).Values).some(item =>
        OrdinalIgnoreCaseEquals(item.CodeName, 'SECTION_MANAGER') || OrdinalIgnoreCaseEquals(item.CodeName, 'SECTIONMANAGER')))
        throw new InvalidOperationException('The document already contains a section-manager object or root anchor.');
      let definition = null;
      if (this.Document.Classes.Contains('SECTION_MANAGER')) {
        definition = this.Document.Classes.get_Item('SECTION_MANAGER');
        if (definition.CppClassName !== 'AcDbSectionManager' || definition.ApplicationName !== 'ObjectDBX Classes' ||
          definition.ProxyFlags !== 1024 || definition.WasProxy || definition.IsEntity)
          throw new InvalidOperationException('The existing SECTION_MANAGER CLASS conflicts with the canonical manager definition.');
      }
      this.ValidateSectionManagerRootMetadata();
      const validated = new Set();
      for (const section of members) {
        if (!DxfStoredSectionManager.IsMember(section, this.Document)) throw new ArgumentException('Every manager member must be an actual registered section in this document.', 'sections');
        if (!validated.has(section)) { validated.add(section); section.Validate(this.Document); }
      }
      let candidate = this.Document.NumHandles;
      if (candidate <= 0n) throw new InvalidOperationException('The document handle range is exhausted.');
      while (candidate < maximumHandle && this.Document.StoredTableHandleTarget(candidate.toString(16).toUpperCase()) !== null) candidate++;
      if (candidate === maximumHandle) throw new InvalidOperationException('The document handle range is exhausted.');
      const manager = DxfStoredSectionManager.CreateCanonical(this.Document, this.Root, members, requiresFullUpdate);
      manager.Handle = candidate.toString(16).toUpperCase(); this.Register(manager, true);
      this.Root.AddLoaded('ACAD_SECTION_MANAGER', manager, false);
      if (definition !== null && definition.InstanceCount !== null) definition.InstanceCount = 1;
      return manager;
    } finally { status.creating = false; status.reentered = false; }
  };
  Type.prototype.ValidateSectionManagerRootMetadata = function() {
    if (this.Root.ExtensionDictionary !== null && (!this.IsRegistered(this.Root.ExtensionDictionary) || this.Root.ExtensionDictionary.Owner !== this.Root))
      throw new InvalidOperationException('The manager root extension dictionary must be registered with its reciprocal owner.');
    for (const reactor of this.Root.PersistentReactors) if (reactor === null || !this.IsRegistered(reactor))
      throw new InvalidOperationException('The manager root reactors must be registered in this document.');
    for (const data of this.Root.XData.Values) for (const tag of data.XDataRecord)
      if (tag.Code === XDataCode.DatabaseHandle && !/^0+$/.test(tag.Value) && this.Document.GetObjectByHandle(tag.Value) === null)
        throw new InvalidOperationException('The manager root XData references must resolve in this document.');
  };
  Type.prototype.EraseSectionManager = function(manager) {
    if (manager == null) throw new ArgumentNullException('manager');
    if (manager.IsErased) throw new InvalidOperationException('The section manager has already been erased.');
    if (manager.Database !== this) throw new ArgumentException('The manager must belong to this database.', 'manager');
    this.CheckRegistered(manager);
    const errors = new ReferenceList(); manager.ValidateDatabaseSchema(this, errors);
    if (errors.Count) throw new InvalidOperationException('Cannot erase an invalid stored section manager: ' + Array.from(errors).join('; '));
    const definition = this.Document.Classes.Contains(manager.CodeName) ? this.Document.Classes.get_Item(manager.CodeName) : null;
    const remaining = Array.from(DatabaseRegistry(this).Values).filter(item => item.CodeName === manager.CodeName).length - 1;
    this.EraseOwnedTreeCore(manager, manager);
    if (definition !== null && definition.InstanceCount !== null) definition.InstanceCount = remaining;
  };
}
