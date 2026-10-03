// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { Section } from '../Entities/Section.js';
import { DxfTag } from '../IO/DxfTag.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceView, RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ValueEquals } from '../../runtime/GenericDictionary.js';
import { ArgumentException, ArgumentNullException, FormatException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const snapshot = values => ReadOnlyReferenceView(new ReferenceList(values));
const packet = (sections, update) => snapshot([new DxfTag(100, 'AcDbSectionManager'),
  new DxfTag(70, update ? 1 : 0), new DxfTag(90, sections.length), ...sections.map(section => new DxfTag(330, section.Handle))]);
/** Source-bound SECTION_MANAGER packet. The constructor adapts the internal reader
 * hook; normal authoring uses document.Objects.CreateSectionManager().
 */
export class DxfStoredSectionManager extends DxfDatabaseObject {
  static get MaximumSections() { return 65536; }
  #source; #version; #sections = new ReferenceList(); #handles; #reactors; #entryName; #hardOwner;
  #resolved = false; #editing = false; #tags; #update;
  constructor(source, codeName, tags, requiresFullUpdate, handles) {
    super(codeName); this.#source = source; this.#version = source.DrawingVariables.AcadVer;
    this.#update = requiresFullUpdate; this.#handles = Array.from(handles); this.#tags = snapshot(tags);
  }
  static CreateCanonical(source, root, members, requiresFullUpdate) {
    const values = Array.from(members), manager = new DxfStoredSectionManager(source, 'SECTION_MANAGER', packet(values, requiresFullUpdate), requiresFullUpdate, []);
    manager.Owner = root; manager.#entryName = 'ACAD_SECTION_MANAGER'; manager.#hardOwner = false;
    manager.#reactors = [root]; manager.#resolved = true; manager.#sections.AddRange(values);
    manager.PersistentReactors.Add(root); return manager;
  }
  get SourceVersion() { return this.#version; }
  get RequiresFullUpdate() { return this.#update; }
  get Tags() { return this.#tags; }
  get Sections() { return ReadOnlyReferenceView(this.#sections); }
  get DatabaseReferences() { return this.#sections; }
  get AllocationReservations() { return this.Tags; }
  CloneShell() { throw new NotSupportedException('Stored section-manager cloning requires the complete manager lifecycle.'); }
  ReplaceSections(sections, requiresFullUpdate) {
    if (sections == null) throw new ArgumentNullException('sections');
    if (this.#editing) throw new InvalidOperationException('Section-manager membership replacement cannot be reentered.');
    this.#editing = true;
    try {
      const replacement = [];
      ConsumeManagedEnumerable(sections, section => {
        if (replacement.length === DxfStoredSectionManager.MaximumSections) throw new ArgumentException('The section-manager membership exceeds 65536 entries.', 'sections');
        replacement.push(section);
      });
      if (this.Database === null || this.Database.Document !== this.#source || this.#source.GetObjectByHandle(this.Handle) !== this)
        throw new InvalidOperationException('The section manager must remain registered in its source document.');
      const errors = new ReferenceList(); this.ValidateDatabaseSchema(this.Database, errors);
      if (errors.Count) throw new InvalidOperationException('Cannot edit an invalid stored section manager: ' + Array.from(errors).join('; '));
      for (const section of replacement) if (!DxfStoredSectionManager.IsMember(section, this.#source))
        throw new ArgumentException('Every manager member must be an actual registered section in its source document.', 'sections');
      const tags = packet(replacement, requiresFullUpdate), members = new ReferenceList(replacement);
      // No caller enumeration or validation remains after this point.
      this.#sections = members; this.#tags = tags; this.#update = requiresFullUpdate;
    } finally { this.#editing = false; }
  }
  static IsMember(section, source) {
    return section instanceof Section && !section.IsErased && section.Handle !== null && section.Owner !== null &&
      section.Owner.Record.Owner === source.Blocks && section.Owner.Entities.Contains(section) && source.GetObjectByHandle(section.Handle) === section;
  }
  Resolve(resolve) {
    for (const handle of this.#handles) {
      const section = resolve(handle);
      if (!(section instanceof Section)) throw new FormatException('A section-manager pointer requires an exact source SECTION entity: ' + handle);
      this.#sections.Add(section);
    }
    const root = this.#source.Objects.Root, entries = Array.from(root.Entries).filter(item => OrdinalIgnoreCaseEquals(item.Name, 'ACAD_SECTION_MANAGER'));
    if (entries.length > 1) throw new InvalidOperationException('Sequence contains more than one matching element.');
    const entry = entries[0];
    if (this.Owner !== root || entry === undefined || entry.Target !== this) throw new FormatException('A stored section manager requires its source root ACAD_SECTION_MANAGER entry.');
    this.#hardOwner = entry.IsHardOwner; this.#entryName = entry.Name; this.#reactors = Array.from(this.PersistentReactors); this.#resolved = true;
  }
  ValidateDatabaseSchema(database, errors) {
    if (!this.#resolved || database.Document !== this.#source) { errors.Add('A stored section manager must remain in its source document.'); return; }
    if (this.#source.DrawingVariables.AcadVer !== this.SourceVersion) errors.Add('Stored section-manager profile conversion requires complete schema regeneration.');
    const root = this.#source.Objects.Root;
    if (this.Owner !== root || !Array.from(root.Entries).some(entry => entry.Name === this.#entryName && entry.Target === this && entry.IsHardOwner === this.#hardOwner))
      errors.Add("The stored section manager's source root entry changed.");
    const reactors = Array.from(this.PersistentReactors);
    if (reactors.length !== this.#reactors.length || reactors.some((value, i) => !ValueEquals(value, this.#reactors[i])))
      errors.Add("The stored section manager's source persistent reactors changed.");
    for (const section of this.#sections) if (!DxfStoredSectionManager.IsMember(section, this.#source)) errors.Add('A stored section-manager target is no longer registered.');
  }
}
RegisterDatabaseModel('DxfStoredSectionManager', DxfStoredSectionManager);
