// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { TableSnapshot, TableHandleMap, IsTableReference } from '../../runtime/TablePayload.js';
import { DependencyView } from '../../runtime/StoredDependencyCollections.js';
import { ArgumentException, FormatException, NotSupportedException, NullReferenceException } from '../../runtime/Errors.js';

/** Qualified retained SUNSTUDY projection. Date arrays, hours and output numbers
 * are source data, not evaluated times or inferred application enumerations. */
export class DxfStoredSunStudy extends DxfDatabaseObject {
  #source; #references = new ReferenceList(); #identities = new TableHandleMap(); #owners = new Map(); #resolved = false;
  #page = null; #view = null; #visual = null; #text = null;
  constructor(source, payload, decode) {
    super('SUNSTUDY');
    if (source == null) throw new NullReferenceException();
    this.#source = source;
    const tags = TableSnapshot(payload), get = index => tags.get_Item(index).Value;
    const values = {SourceVersion: source.DrawingVariables.AcadVer, Payload: tags,
      Name: decode(get(2)), Description: decode(get(3)), SheetSetName: decode(get(5)), SheetSubsetName: decode(get(7)),
      RawHourFlags: TableSnapshot(Array.from(tags).slice(15, 15 + Math.max(0, get(14))).map(tag => tag.Value)),
      References: DependencyView(this.#references)};
    for (const [name, value] of Object.entries(values)) Object.defineProperty(this, name, {value, enumerable: true});
  }
  get Version() { return this.Payload.get_Item(1).Value; }
  get OutputType() { return this.Payload.get_Item(4).Value; }
  get UseSubset() { return this.Payload.get_Item(6).Value; }
  get SelectDates() { return this.Payload.get_Item(8).Value; }
  get DateCount() { return this.Payload.get_Item(9).Value; }
  get SelectDateRange() { return this.Payload.get_Item(10).Value; }
  get StartTime() { return this.Payload.get_Item(11).Value; }
  get EndTime() { return this.Payload.get_Item(12).Value; }
  get Interval() { return this.Payload.get_Item(13).Value; }
  get PageSetup() { return this.#page; }
  get View() { return this.#view; }
  get VisualStyle() { return this.#visual; }
  get TextStyle() { return this.#text; }
  get DatabaseReferences() { return this.#references; }
  get AllocationReservations() { return this.Payload; }
  CloneShell() { throw new NotSupportedException('Stored SUNSTUDY cloning requires its complete application lifecycle.'); }
  Resolve(resolve) {
    for (const tag of this.Payload) {
      if (!IsTableReference(tag)) continue;
      const handle = tag.Value, zero = BigInt('0x' + handle) === 0n, target = zero ? null : resolve(handle);
      if (target == null && !zero) throw new FormatException('SUNSTUDY requires an exact source reference identity: ' + handle);
      if (target != null) { this.#identities.set(handle, target); this.#references.Add(target); }
      switch (tag.Code) { case 340: this.#page = target; break; case 341: this.#view = target; break; case 342: this.#visual = target; break; case 343: this.#text = target; break; }
    }
    if (!(this.Owner instanceof DxfDictionary)) throw new FormatException('SUNSTUDY requires its registered source owner dictionary.');
    const visited = new Set();
    for (let item = this; item !== null && item !== this.#source; item = item.Owner) {
      if (visited.has(item)) throw new FormatException('SUNSTUDY source ownership contains a cycle.');
      visited.add(item);
      if (resolve(item.Handle) !== item) throw new FormatException('SUNSTUDY source ancestry contains an unregistered source object.');
      if (this.#owners.has(item)) throw new ArgumentException('An item with the same key has already been added.');
      this.#owners.set(item, item.Owner);
    }
    this.#resolved = true;
  }
  ValidateDatabaseSchema(database, errors) {
    if (!this.#resolved || database == null || database.Document !== this.#source) { errors.Add('Stored SUNSTUDY requires its registered source document.'); return; }
    if (this.#source.DrawingVariables.AcadVer !== this.SourceVersion) errors.Add('Stored SUNSTUDY profile conversion requires the complete application schema.');
    for (const [item, owner] of this.#owners) if (item.Owner !== owner || this.#source.GetObjectByHandle(item.Handle) !== item)
      errors.Add('Stored SUNSTUDY source ownership changed.');
    for (const [handle, target] of this.#identities) if (this.#source.GetObjectByHandle(handle) !== target)
      errors.Add('Stored SUNSTUDY dependency identity changed: ' + handle);
  }
}
RegisterDatabaseModel('DxfStoredSunStudy', DxfStoredSunStudy);
