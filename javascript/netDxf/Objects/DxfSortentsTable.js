// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
import { BlockRecord } from '../Blocks/BlockRecord.js';
import { EntityObject } from '../Entities/EntityObject.js';
import { DxfTag } from '../IO/DxfTag.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException } from '../../runtime/Errors.js';
export class DxfSortOrderEntry {
  constructor(entity, sortHandle) {
    if (entity == null) throw new ArgumentNullException('entity');
    new DxfTag(5, sortHandle);
    Object.defineProperties(this, {Entity:{value:entity, enumerable:true}, SortHandle:{value:sortHandle, enumerable:true}});
    Object.freeze(this);
  }
}
class OrderCollection extends ReferenceList {
  #owner; #entities = new Set();
  constructor(owner) { super(); this.#owner = owner; }
  #index(index, append = false) {
    if (!Number.isInteger(index) || index < 0 || index >= this.Count + (append ? 1 : 0)) throw new ArgumentOutOfRangeException('index', index);
  }
  #check(index, item, replacement) {
    if (item == null) throw new ArgumentNullException('item');
    if (this.#entities.has(item.Entity) && (!replacement || this.get_Item(index).Entity !== item.Entity)) throw new ArgumentException('Each redraw entity occurs at most once.', 'item');
    if (this.#owner.BlockRecord !== null && item.Entity.Owner?.Record !== this.#owner.BlockRecord) throw new ArgumentException("The entity must belong to the table's block.", 'item');
    this.#owner.Database?.CheckRegistered(item.Entity);
  }
  Add(item) { this.Insert(this.Count, item); }
  Insert(index, item) { this.#index(index, true); this.#check(index, item, false); super.Insert(index, item); this.#entities.add(item.Entity); }
  set_Item(index, item) { this.#index(index); this.#check(index, item, true); const previous = this.get_Item(index).Entity; super.set_Item(index, item); this.#entities.delete(previous); this.#entities.add(item.Entity); }
  RemoveAt(index) { const previous = this.get_Item(index).Entity; super.RemoveAt(index); this.#entities.delete(previous); }
  Clear() { super.Clear(); this.#entities.clear(); }
}
export class DxfSortentsTable extends DxfDatabaseObject {
  #entries; BlockRecord = null;
  constructor(blockRecord) {
    super('SORTENTSTABLE'); this.#entries = new OrderCollection(this);
    // The no-argument form adapts the source internal loader/clone constructor.
    if (arguments.length) { if (blockRecord == null) throw new ArgumentNullException('blockRecord'); this.BlockRecord = blockRecord; }
  }
  get Entries() { return this.#entries; }
  get DatabaseReferences() { return [this.BlockRecord, ...Array.from(this.Entries, e => e.Entity)]; }
  CloneShell() { return new DxfSortentsTable(); }
  CopyDatabaseReferencesTo(clone, resolve) {
    const record = resolve(this.BlockRecord);
    if (!(record instanceof BlockRecord)) throw new InvalidOperationException('A redraw block must map to a block record.');
    clone.BlockRecord = record;
    for (const entry of this.Entries) { const entity = resolve(entry.Entity); if (!(entity instanceof EntityObject)) throw new InvalidOperationException('A redraw entity must map to a graphical entity.'); clone.Entries.Add(new DxfSortOrderEntry(entity, entry.SortHandle)); }
  }
  ValidateDatabaseSchema(database, errors) {
    if (database.Document.DrawingVariables.AcadVer < 14) errors.Add('SORTENTSTABLE export requires AutoCAD 2004 or later.');
    if (this.BlockRecord === null) errors.Add('SORTENTSTABLE has no block record.');
    const dictionary = this.Owner;
    if (!(dictionary instanceof DxfDictionary) || dictionary.Owner !== this.BlockRecord || dictionary.Database !== null && this.BlockRecord?.ExtensionDictionary !== dictionary || !Array.from(dictionary.Entries).some(e => OrdinalIgnoreCaseEquals(e.Name, 'ACAD_SORTENTS') && e.Target === this)) errors.Add("SORTENTSTABLE must occupy ACAD_SORTENTS in its block record's extension dictionary.");
    const seen = new Set();
    for (const entry of this.Entries) { if (seen.has(entry.Entity)) errors.Add('Duplicate SORTENTSTABLE entity.'); seen.add(entry.Entity); if (entry.Entity.Owner?.Record !== this.BlockRecord) errors.Add('A SORTENTSTABLE entity belongs to another block.'); }
  }
}
