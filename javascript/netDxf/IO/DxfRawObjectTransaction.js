// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
import { ObjectStoreState, DxfObjectText as Text, DxfObjectSchema as Schema } from './DxfRawObjectStore.js';
import * as M from './DxfRawObjectModel.js';
import { DxfRawHandleRole as R, DxfRawHandleDiagnosticKind as D } from './DxfRawHandleModel.js';
import { DxfTag } from './DxfTag.js';
import { ReadOnlyList, OrdinalIgnoreCaseEquals as Eq } from '../../runtime/Collections.js';
import * as E from '../../runtime/Errors.js';
import { EnsureExtension, RemoveExtensionDictionary, SetDrawOrder, CloneDictionaryTree, DeleteTree } from './DxfRawObjectGraph.js';
import { Commit } from './DxfRawObjectCommit.js';
const T = (code, value) => new DxfTag(code, value);
export const InvalidGraphKinds = Object.freeze([D.DuplicateIdentity, D.MultipleIdentities, D.NullIdentity,
  D.MultipleOwners, D.InvalidControlGroup, D.OwnerCycle]);

export class DxfRawObjectTransaction {
  #state;
  constructor(store, cancellation = null) {
    const { options, index } = ObjectStoreState(store);
    E.ThrowIfCancellationRequested(cancellation);
    if (store.Document.Version < 13)
      throw new E.NotSupportedException('Schema-aware object editing currently requires AutoCAD 2000 or later; raw historical preservation remains separate.');
    for (const diagnostic of index.Diagnostics) if (InvalidGraphKinds.includes(diagnostic.Kind))
      throw new E.InvalidDataException('Object editing requires unambiguous common identities and ownership: ' + diagnostic.Message);
    const reserved = new Set(index.Occurrences.filter(o => o.Role !== R.HeaderSeed).map(o => o.NumericHandle));
    const seeds = index.Occurrences.filter(o => o.Role === R.HeaderSeed);
    if (seeds.length > 1) throw new E.InvalidDataException('Multiple HANDSEED values are ambiguous.');
    this.#state = { store, options, index, cancellation, reserved, changes: new Map(), added: [],
      next: !seeds.length || seeds[0].NumericHandle === 0n ? 1n : seeds[0].NumericHandle,
      root: store.RootDictionary?.Handle ?? null, enableSortents: false, closed: false, undo: null, allocations: null };
    Object.freeze(this);
  }
  Get(handle) { return Get(this.#state, handle); }
  EnsureRootDictionary() { return Apply(this.#state, () => EnsureRoot(this.#state)); }
  CreateDictionary(parentHandle, name, hardOwner = true, withDefault = false) {
    const s = this.#state;
    return Apply(s, () => {
      const parent = Require(s, M.DxfRawDictionary, parentHandle); CheckNewKey(parent, name);
      const body = [T(100, 'AcDbDictionary'), T(280, hardOwner ? 1 : 0), T(281, 1)];
      if (withDefault) body.push(T(100, 'AcDbDictionaryWithDefault'));
      const child = AddObject(s, withDefault ? 'ACDBDICTIONARYWDFLT' : 'DICTIONARY', parent.Handle, body);
      if (withDefault) {
        const placeholder = AddObject(s, 'ACDBPLACEHOLDER', child.Handle, []);
        WriteDictionary(s, child, [new M.DxfRawDictionaryEntry('Default', placeholder.Handle)], child.HardOwnerFlag, child.CloningFlag, placeholder.Handle);
      }
      LinkNew(s, parent, name, child); return child.Handle;
    });
  }
  CreateXRecord(parentHandle, name, data, cloning = M.DxfDuplicateRecordCloning.KeepExisting) {
    const s = this.#state;
    return Apply(s, () => {
      const parent = Require(s, M.DxfRawDictionary, parentHandle); CheckNewKey(parent, name);
      Text.Cloning(cloning); const payload = Collect(s, data, Schema.ValidateXRecordTag); ReserveTags(s, payload);
      const child = AddObject(s, 'XRECORD', parent.Handle, [T(100, 'AcDbXrecord'), T(280, cloning), ...payload]);
      LinkNew(s, parent, name, child); return child.Handle;
    });
  }
  CreateVariable(parentHandle, name, value, schema = 0) {
    const s = this.#state;
    return Apply(s, () => {
      const parent = Require(s, M.DxfRawDictionary, parentHandle); CheckNewKey(parent, name);
      const text = Text.Encode(value);
      const child = AddObject(s, 'DICTIONARYVAR', parent.Handle, [T(100, 'DictionaryVariables'), T(280, schema), T(1, text)]);
      LinkNew(s, parent, name, child); return child.Handle;
    });
  }
  CreatePlaceholder(parentHandle, name) {
    const s = this.#state;
    return Apply(s, () => {
      const parent = Require(s, M.DxfRawDictionary, parentHandle); CheckNewKey(parent, name);
      const child = AddObject(s, 'ACDBPLACEHOLDER', parent.Handle, []); LinkNew(s, parent, name, child); return child.Handle;
    });
  }
  CreateIdBuffer(parentHandle, name, handles) {
    const s = this.#state;
    return Apply(s, () => {
      const parent = Require(s, M.DxfRawDictionary, parentHandle); CheckNewKey(parent, name);
      const data = Collect(s, handles, h => Text.Handle(h, true));
      for (const item of data) Reserve(s, item);
      const child = AddObject(s, 'IDBUFFER', parent.Handle, [T(100, 'AcDbIdBuffer'), ...data.map(h => T(330, Text.Handle(h, true)))]);
      LinkNew(s, parent, name, child); return child.Handle;
    });
  }
  SetXRecord(handle, data, cloning = M.DxfDuplicateRecordCloning.KeepExisting) {
    const s = this.#state;
    Apply(s, () => {
      const value = Require(s, M.DxfRawXRecord, handle); Text.Cloning(cloning);
      const payload = Collect(s, data, Schema.ValidateXRecordTag);
      if (value.CloningFlag === cloning && SameTags(value.Data, payload)) return;
      ReserveTags(s, payload);
      Replace(s, value, Schema.Wrap(value, [T(100, 'AcDbXrecord'), T(280, cloning), ...payload]));
    });
  }
  SetVariable(handle, value, schema = 0) {
    const s = this.#state;
    Apply(s, () => {
      const original = Require(s, M.DxfRawDictionaryVariable, handle);
      if (original.Value === value && original.SchemaNumber === schema) return;
      const body = [T(100, 'DictionaryVariables')];
      if (schema !== null) body.push(T(280, schema));
      if (value !== null) body.push(T(1, Text.Encode(value)));
      Replace(s, original, Schema.Wrap(original, body));
    });
  }
  SetIdBuffer(handle, handles) {
    const s = this.#state;
    Apply(s, () => {
      const original = Require(s, M.DxfRawIdBuffer, handle), data = Collect(s, handles, h => Text.Handle(h, true));
      if (original.Handles.length === data.length && original.Handles.every((h, i) => h === Text.Handle(data[i], true))) return;
      for (const item of data) Reserve(s, item);
      Replace(s, original, Schema.Wrap(original, [T(100, 'AcDbIdBuffer'), ...data.map(h => T(330, Text.Handle(h, true)))]));
    });
  }
  SetDictionaryEntry(dictionaryHandle, name, targetHandle, hardOwner = false) {
    const s = this.#state;
    Apply(s, () => {
      const dictionary = Require(s, M.DxfRawDictionary, dictionaryHandle), entry = new M.DxfRawDictionaryEntry(name, targetHandle, hardOwner);
      const target = Get(s, entry.Handle);
      if (!target) throw new E.ArgumentException('Dictionary target must be an existing OBJECTS record.');
      if (target.OwnerHandle !== dictionary.Handle || target.Handle === dictionary.Handle)
        throw new E.InvalidOperationException('A dictionary entry must target another object already owned by that dictionary.');
      const entries = Array.from(dictionary.Entries), index = entries.findIndex(e => Eq(e.Name, name));
      if (index >= 0 && entries[index].Name === entry.Name && entries[index].Handle === entry.Handle && entries[index].IsHardOwner === entry.IsHardOwner) return;
      if (index < 0) entries.push(entry); else entries[index] = entry;
      WriteDictionary(s, dictionary, entries, dictionary.HardOwnerFlag, dictionary.CloningFlag, dictionary.DefaultHandle);
    });
  }
  RenameEntry(dictionaryHandle, name, newName) {
    const s = this.#state;
    Apply(s, () => {
      const value = Require(s, M.DxfRawDictionary, dictionaryHandle); Text.ValidateName(newName);
      const entry = value.Find(name);
      if (!entry) throw new E.KeyNotFoundException('Dictionary name not found: ' + name);
      if (entry.Name === newName) return;
      const conflict = value.Find(newName);
      if (conflict && conflict !== entry) throw new E.ArgumentException('The new dictionary name already exists.');
      const entries = value.Entries.map(e => e === entry ? new M.DxfRawDictionaryEntry(newName, e.Handle, e.IsHardOwner) : e);
      WriteDictionary(s, value, entries, value.HardOwnerFlag, value.CloningFlag, value.DefaultHandle);
    });
  }
  SetDictionaryFlags(handle, hardOwner, cloning) {
    const s = this.#state;
    Apply(s, () => {
      const value = Require(s, M.DxfRawDictionary, handle);
      if (cloning !== null) Text.Cloning(cloning);
      if (value.HardOwnerFlag === hardOwner && value.CloningFlag === cloning) return;
      WriteDictionary(s, value, value.Entries, hardOwner, cloning, value.DefaultHandle);
    });
  }
  SetDefault(dictionaryHandle, targetHandle) {
    const s = this.#state;
    Apply(s, () => {
      const value = Require(s, M.DxfRawDictionary, dictionaryHandle);
      if (!value.HasDefault) throw new E.ArgumentException('The object is not a dictionary with default.');
      const target = Text.Handle(targetHandle, false);
      if (Get(s, target) === null) throw new E.ArgumentException('Default target must be an existing OBJECTS record.');
      if (value.DefaultHandle === target) return;
      WriteDictionary(s, value, value.Entries, value.HardOwnerFlag, value.CloningFlag, target);
    });
  }
  RemoveEntry(dictionaryHandle, name, deleteOwnedTree = false) {
    const s = this.#state;
    return Apply(s, () => {
      const value = Require(s, M.DxfRawDictionary, dictionaryHandle), entry = value.Find(name);
      if (!entry) return false;
      const target = Get(s, entry.Handle);
      if (deleteOwnedTree && (!target || target.OwnerHandle !== value.Handle))
        throw new E.InvalidOperationException('The unlinked target is not owned by this dictionary.');
      WriteDictionary(s, value, value.Entries.filter(e => e !== entry), value.HardOwnerFlag, value.CloningFlag, value.DefaultHandle);
      if (deleteOwnedTree) DeleteTree(s, entry.Handle); return true;
    });
  }
  EnsureExtensionDictionary(ownerHandle) { return Apply(this.#state, () => EnsureExtension(this.#state, ownerHandle)); }
  RemoveExtensionDictionary(ownerHandle, deleteOwnedTree = false) { return Apply(this.#state, () => RemoveExtensionDictionary(this.#state, ownerHandle, deleteOwnedTree)); }
  SetDrawOrder(blockRecordHandle, entries, enableRegeneration = true) { return Apply(this.#state, () => SetDrawOrder(this.#state, blockRecordHandle, entries, enableRegeneration)); }
  CloneDictionaryTree(sourceHandle, parentHandle, name) { return Apply(this.#state, () => CloneDictionaryTree(this.#state, sourceHandle, parentHandle, name)); }
  DeleteOwnedTree(handle) { Apply(this.#state, () => DeleteTree(this.#state, handle)); }
  Commit() { return Commit(this.#state); }
  Dispose() {
    const s = this.#state;
    if (s.undo !== null) throw new E.InvalidOperationException('An object transaction cannot be disposed inside an operation.');
    s.closed = true; s.changes.clear(); s.added.length = 0;
  }
}

// Internal partial-class helpers. The state object is private and never returned to callers.
export function EnsureOpen(s) {
  if (s.closed) throw new E.ObjectDisposedException('DxfRawObjectTransaction');
  E.ThrowIfCancellationRequested(s.cancellation);
}
export function Get(s, handle) {
  EnsureOpen(s); const canonical = Text.Handle(handle, false), change = s.changes.get(Text.Number(canonical));
  return change ? change.Deleted ? null : change.Object : s.store.Get(canonical);
}
export function Require(s, Type, handle) {
  const value = Get(s, handle);
  if (value instanceof M.DxfRawOpaqueStoredObject) throw new E.NotSupportedException(value.Reason);
  if (!(value instanceof Type)) throw new E.ArgumentException('Handle is not an editable ' + Type.name + ': ' + handle, 'handle');
  return value;
}
export function Apply(s, action) {
  EnsureOpen(s);
  if (s.undo !== null) throw new E.InvalidOperationException('An object transaction cannot be reentered.');
  s.undo = new Map(); s.allocations = [];
  const oldNext = s.next, count = s.added.length, oldRoot = s.root, oldSortents = s.enableSortents;
  try { const result = action(); E.ThrowIfCancellationRequested(s.cancellation); return result; }
  catch (error) {
    for (const [key, value] of s.undo) { if (value === null) s.changes.delete(key); else s.changes.set(key, value); }
    for (const handle of s.allocations) s.reserved.delete(handle);
    s.added.length = count; s.next = oldNext; s.root = oldRoot; s.enableSortents = oldSortents; throw error;
  } finally { s.undo = null; s.allocations = null; }
}
export function Stage(s, key, value) {
  if (!s.changes.has(key) && s.changes.size === s.options.MaximumChanges) throw new E.InvalidDataException('Object transaction change budget exceeded.');
  if (!s.undo.has(key)) s.undo.set(key, s.changes.get(key) ?? null);
  s.changes.set(key, value);
}
export function Reserve(s, handle) {
  const value = Text.Number(Text.Handle(handle, true));
  if (!s.reserved.has(value)) { s.reserved.add(value); s.allocations.push(value); }
}
export function ReserveTags(s, data) { for (const tag of data) if (tag.Code >= 320 && tag.Code <= 369) Reserve(s, tag.Value); }
export function Allocate(s) {
  while (s.reserved.has(s.next)) {
    E.ThrowIfCancellationRequested(s.cancellation);
    if (s.next === 0xffffffffffffffffn) throw new E.InvalidOperationException('No representable handle/next-handle pair remains.');
    s.next++;
  }
  if (s.next === 0xffffffffffffffffn) throw new E.InvalidOperationException('A new object must leave a representable HANDSEED.');
  const value = s.next++; s.reserved.add(value); s.allocations.push(value); return value.toString(16).toUpperCase();
}
export function AddObject(s, type, owner, body) {
  if (s.store.Objects.length + s.added.length >= s.options.MaximumObjects) throw new E.InvalidDataException('OBJECTS record budget exceeded.');
  const handle = Allocate(s), tags = [T(0, type), T(5, handle), T(330, owner), ...body];
  const value = Schema.Read(ReadOnlyList(tags), null, s.options.MaximumPayloadTags);
  if (value instanceof M.DxfRawOpaqueStoredObject) throw new E.InvalidDataException(value.Reason);
  const key = Text.Number(handle); Stage(s, key, { Source: null, Tags: tags, Object: value, Deleted: false }); s.added.push(key); return value;
}
export function Replace(s, original, tags) {
  if (SameTags(original.Tags, tags)) return;
  const value = Schema.Read(ReadOnlyList(tags), original.SourceRecord, s.options.MaximumPayloadTags);
  if (value instanceof M.DxfRawOpaqueStoredObject) throw new E.InvalidDataException(value.Reason);
  Stage(s, Text.Number(original.Handle), { Source: original.SourceRecord, Tags: tags, Object: value, Deleted: false });
}
export function SameTags(left, right) {
  const a = Array.isArray(left) ? left : Array.from(left), b = Array.isArray(right) ? right : Array.from(right);
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) {
    if (a[i].Code !== b[i].Code) return false;
    const av = a[i].Value, bv = b[i].Value;
    if (av instanceof Uint8Array) {
      if (!(bv instanceof Uint8Array) || av.length !== bv.length || !av.every((v, j) => v === bv[j])) return false;
    } else if (!Object.is(av, bv)) return false;
  }
  return true;
}
export function Collect(s, input, validate) {
  if (input == null) throw new E.ArgumentNullException('input');
  const items = [];
  for (const item of input) {
    E.ThrowIfCancellationRequested(s.cancellation);
    if (items.length === s.options.MaximumPayloadTags) throw new E.InvalidDataException('Object payload budget exceeded.');
    validate(item); items.push(item);
  }
  return items;
}
export function CheckNewKey(parent, name) {
  Text.ValidateName(name);
  if (parent.Find(name) !== null) throw new E.ArgumentException('A dictionary entry already exists: ' + name, 'name');
}
export function WriteDictionary(s, value, entries, hard, cloning, defaultHandle) {
  Replace(s, value, Schema.Wrap(value, Schema.DictionaryBody(value, entries, hard, cloning, defaultHandle)));
}
export function LinkNew(s, parent, name, child) {
  WriteDictionary(s, parent, [...parent.Entries, new M.DxfRawDictionaryEntry(name, child.Handle)], parent.HardOwnerFlag, parent.CloningFlag, parent.DefaultHandle);
}
export function EnsureRoot(s) {
  if (s.root === null) s.root = AddObject(s, 'DICTIONARY', '0', [T(100, 'AcDbDictionary'), T(281, 1)]).Handle;
  return s.root;
}
