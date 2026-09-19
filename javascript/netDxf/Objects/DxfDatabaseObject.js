// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { EntityObject } from '../Entities/EntityObject.js';
import { DxfTag } from '../IO/DxfTag.js';
import { XDataCode } from '../XDataCode.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { IsAncestor, IsDatabaseModel, IsReservedDictionaryName, ReadOnlyReferenceView } from '../../runtime/DatabaseModel.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, FormatException, InvalidOperationException, KeyNotFoundException, NotSupportedException, RequireInteger } from '../../runtime/Errors.js';
import { InstallDeclaredOwnership } from './DxfDeclaredOwnership.js';
import { InstallTableComposite } from './DxfXRecord.TableComposite.js';

export class DxfDatabaseObject extends DxfObject {
  Database = null; IsErased = false;
  constructor(codeName) { super(codeName); if (new.target === DxfDatabaseObject) throw new NotSupportedException('DxfDatabaseObject is abstract.'); }
  get DeclaredOwnedObjects() { return []; }
  get DatabaseReferences() { return []; }
  get AllocationReservations() { return []; }
  MaterializeOwnedObjectReferences() {}
  CopyDatabaseReferencesTo(clone, resolve) {}
  ValidateDatabaseSchema(database, errors) {}
  OnXDataAddAppRegEvent(item) {
    if (this.Database !== null) {
      this.XData.ReplaceForBinding(item.Name, this.XData.get_Item(item.Name).CopyStoredGraph());
      for (const tag of this.XData.get_Item(item.Name).XDataRecord)
        if (tag.Code === XDataCode.DatabaseHandle) this.Database.ReserveUnresolvedReference(new DxfTag(1005, tag.Value));
    }
    super.OnXDataAddAppRegEvent(item);
  }
}
export class DxfDictionaryEntry {
  constructor(name, target, hardOwner) {
    Object.defineProperties(this, {Name:{value:name,enumerable:true},Target:{value:target,enumerable:true},IsHardOwner:{value:hardOwner,enumerable:true}});
    Object.freeze(this);
  }
}
export class DxfDictionary extends DxfDatabaseObject {
  #entries = new ReferenceList(); #index = new Map(); #cloning = 1; IsHardOwner = true;
  constructor(code = 'DICTIONARY') { super(code); }
  get Entries() { return ReadOnlyReferenceView(this.#entries); }
  get Count() { return this.#entries.Count; }
  get Cloning() { return this.#cloning; }
  set Cloning(value) { this.#cloning = RequireInteger(value, 0, 5); }
  get_Item(name) { const box = {}; if (this.TryGetValue(name, box)) return box.value; throw new KeyNotFoundException(name); }
  TryGetValue(name, output) {
    if (name == null) throw new ArgumentNullException('name');
    const key = OrdinalIgnoreCaseKey(name);
    if (this.#index.has(key)) { output.value = this.#index.get(key).Target; return true; }
    output.value = this instanceof DxfDictionaryWithDefault ? this.Default : null; return output.value !== null;
  }
  Contains(name) { if (name == null) throw new ArgumentNullException('key'); return this.#index.has(OrdinalIgnoreCaseKey(name)); }
  Add(name, target, hardOwner = true) {
    if (this.IsErased) throw new InvalidOperationException('An erased dictionary cannot adopt objects.');
    DxfDictionary.ValidateName(name);
    if (target == null) throw new ArgumentNullException('target');
    if (target instanceof DxfDatabaseObject && target.IsErased) throw new InvalidOperationException('An erased object cannot be attached again.');
    if (IsDatabaseModel(target, 'DxfSun')) throw new ArgumentException('SUN must be attached through SetSun to a view or viewport owner.', 'target');
    if (target instanceof EntityObject) throw new ArgumentException('Graphical entities cannot be dictionary entries; use XRECORD pointer data.', 'target');
    if (this.Contains(name)) throw new ArgumentException('The dictionary already contains this name.', 'name');
    if (this.Database !== null && this === this.Database.Root && IsReservedDictionaryName(name)) throw new ArgumentException('This root entry is managed by an existing document collection.', 'name');
    if (IsAncestor(target, this)) throw new ArgumentException('Dictionary ownership cannot form a cycle.', 'target');
    if (target.Owner !== null && target.Owner !== this) throw new ArgumentException('Dictionary aliases must retain one common owner.', 'target');
    if (!(target instanceof DxfDatabaseObject) && (hardOwner || this.IsHardOwner)) throw new ArgumentException('Existing document objects can only be referenced by soft aliases.', 'target');
    if (this.Database !== null) this.Database.PrepareTarget(target);
    else if (target instanceof DxfDatabaseObject && target.Database !== null) throw new ArgumentException('A detached dictionary cannot adopt a registered object.', 'target');
    if (target instanceof DxfDatabaseObject && target.Owner === null && target !== this) target.Owner = this;
    this.AddLoaded(name, target, hardOwner);
  }
  Remove(name) {
    if (name == null) throw new ArgumentNullException('key');
    const key = OrdinalIgnoreCaseKey(name), entry = this.#index.get(key);
    if (!entry) return false;
    this.#index.delete(key); this.#entries.Remove(entry); return true;
  }
  AddLoaded(name, target, hardOwner) {
    DxfDictionary.ValidateName(name); const key = OrdinalIgnoreCaseKey(name);
    if (this.#index.has(key)) throw new FormatException('Duplicate dictionary name: ' + name);
    const entry = new DxfDictionaryEntry(name, target, hardOwner); this.#index.set(key, entry); this.#entries.Add(entry);
  }
  static ValidateName(name) {
    if (name == null || name === '' || /[\0\r\n]/.test(name)) throw new ArgumentException('Dictionary names must be nonempty single-line strings.', 'name');
  }
  CloneShell() { return Object.assign(new DxfDictionary(), {IsHardOwner:this.IsHardOwner,Cloning:this.Cloning}); }
}
export class DxfDictionaryWithDefault extends DxfDictionary {
  #default = null;
  constructor() { super('ACDBDICTIONARYWDFLT'); }
  get Default() { return this.#default; }
  set Default(value) {
    if (value instanceof EntityObject) throw new ArgumentException('A dictionary default must be a nongraphical object.', 'value');
    if (value !== null && this.Database !== null) this.Database.CheckRegistered(value);
    this.#default = value;
  }
  CloneShell() { return Object.assign(new DxfDictionaryWithDefault(), {IsHardOwner:this.IsHardOwner,Cloning:this.Cloning}); }
}
class DxfXRecordData extends ReferenceList {
  #owner;
  constructor(owner) { super(); this.#owner = owner; }
  #check(tag, preserved) {
    if (tag == null) throw new ArgumentNullException('tag');
    if (!preserved && tag.Value instanceof Uint8Array && tag.Value.length > 127) throw new ArgumentException('An authored XRECORD binary chunk cannot exceed 127 bytes.', 'tag');
    if (tag.Code <= 0 || tag.Code >= 1000 || tag.Code === 5 || tag.Code === 105 || tag.Code === 999 || (!preserved && tag.Code > 369)) throw new ArgumentException('Authored XRECORD data uses group codes 1 through 369, excluding 5 and 105.', 'tag');
  }
  #index(index, append = false) { if (!Number.isInteger(index) || index < 0 || index >= this.Count + (append ? 1 : 0)) throw new ArgumentOutOfRangeException('index', index); }
  Add(item) { this.Insert(this.Count, item); }
  Insert(index, item) { this.#index(index, true); this.#owner.CheckPayloadEditable(); this.#check(item, false); this.#owner.Database?.ReserveUnresolvedReference(item); super.Insert(index, item); }
  set_Item(index, item) { this.#index(index); this.#owner.CheckPayloadEditable(); this.#check(item, false); this.#owner.Database?.ReserveUnresolvedReference(item); super.set_Item(index, item); }
  RemoveAt(index) { this.#index(index); this.#owner.CheckPayloadEditable(); super.RemoveAt(index); }
  Clear() { this.#owner.CheckPayloadEditable(); super.Clear(); }
  AddPreserved(item) { this.#check(item, true); super.Insert(this.Count, item); }
  SetPreserved(index, item) { this.#check(item, true); super.set_Item(index, item); }
}
export class DxfXRecord extends DxfDatabaseObject {
  #data; #cloning = 1;
  constructor() { super('XRECORD'); this.#data = new DxfXRecordData(this); }
  get Data() { return this.#data; }
  get Cloning() { return this.#cloning; } set Cloning(value) { this.#cloning = RequireInteger(value, 0, 5); }
  CloneShell() { const copy = new DxfXRecord(); copy.Cloning = this.Cloning; for (const tag of this.Data) copy.AddLoadedData(tag); return copy; }
  AddLoadedData(tag) { this.#data.AddPreserved(tag); }
  ReplaceLoadedData(index, tag) { this.#data.SetPreserved(index, tag); }
}
export class DxfDictionaryVariable extends DxfDatabaseObject {
  #value = ''; #schema = 0;
  constructor() { super('DICTIONARYVAR'); }
  get Schema() { return this.#schema; } set Schema(value) { this.#schema = RequireInteger(value, 0, 255); }
  get Value() { return this.#value; }
  set Value(value) { if (value == null) throw new ArgumentNullException('value'); if (value.includes('\0')) throw new ArgumentException('NUL is not permitted.', 'value'); this.#value = value; }
  CloneShell() { return Object.assign(new DxfDictionaryVariable(), {Schema:this.Schema,Value:this.Value}); }
}
export class DxfPlaceholder extends DxfDatabaseObject {
  constructor() { super('ACDBPLACEHOLDER'); }
  CloneShell() { return new DxfPlaceholder(); }
}
InstallDeclaredOwnership(DxfXRecord);
InstallTableComposite(DxfXRecord);
