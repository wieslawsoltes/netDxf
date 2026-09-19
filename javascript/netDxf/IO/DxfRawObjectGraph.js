// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
import { DxfObjectText as Text, DxfObjectSchema as Schema } from './DxfRawObjectStore.js';
import * as M from './DxfRawObjectModel.js';
import { DxfRawHandleIndex } from './DxfRawHandleIndex.js';
import { DxfRawHandleRole as R } from './DxfRawHandleModel.js';
import { DxfTag } from './DxfTag.js';
import { Get, Require, SameTags, Stage, Replace, EnsureRoot, AddObject, Collect, Reserve,
  LinkNew, Allocate, CheckNewKey } from './DxfRawObjectTransaction.js';
import { Build } from './DxfRawObjectCommit.js';
import * as E from '../../runtime/Errors.js';
import { ReadOnlyList } from '../../runtime/Collections.js';
const T = (code, value) => new DxfTag(code, value);

export function Record(s, handle) {
  const canonical = Text.Handle(handle, false), key = Text.Number(canonical), change = s.changes.get(key);
  if (change) {
    if (change.Deleted) throw new E.KeyNotFoundException('Record was deleted: ' + handle);
    return change;
  }
  const definitions = s.index.FindDefinitions(canonical);
  if (definitions.length !== 1 || definitions[0].Record === null) throw new E.ArgumentException('A unique record identity is required: ' + handle);
  const source = definitions[0].Record;
  return { Source: source, Tags: Array.from(source.Tags), Object: source.SectionName === 'OBJECTS' ? Get(s, canonical) : null };
}
export function CommonEnd(tags) {
  let depth = 0;
  for (let i = 1; i < tags.length; i++) {
    const t = tags[i];
    if (t.Code === 102) {
      const text = t.Value;
      if (text.startsWith('{')) depth++;
      else if (text === '}' && depth > 0) depth--;
      else throw new E.InvalidDataException('Malformed common control group.');
    } else if (depth === 0 && [100, 101, 1001].includes(t.Code)) return i;
  }
  if (depth !== 0) throw new E.InvalidDataException('Unterminated common control group.');
  return tags.length;
}
export function Extension(tags) {
  const end = CommonEnd(tags); let depth = 0, start = -1, target = null, result = null;
  for (let i = 1; i < end; i++) {
    const t = tags[i];
    if (t.Code === 102) {
      const text = t.Value;
      if (text.startsWith('{')) {
        if (depth === 0 && text === '{ACAD_XDICTIONARY') { start = i; target = null; }
        else if (start >= 0) throw new E.NotSupportedException('Nested extension dictionary controls are not inferred.');
        depth++;
      } else {
        depth--;
        if (depth === 0 && start >= 0) {
          if (result !== null || target === null) throw new E.InvalidDataException('An extension dictionary requires one group-360 reference.');
          result = [start, i + 1, target]; start = -1;
        }
      }
    } else if (start >= 0 && t.Code !== 999) {
      if (t.Code !== 360 || target !== null) throw new E.NotSupportedException('Unsupported extension dictionary slot.');
      target = Text.Handle(t.Value, false);
    }
  }
  return result;
}
export function ReplaceRecord(s, handle, original, tags) {
  if (SameTags(original.Tags, tags)) return;
  if (original.Object !== null) { Replace(s, original.Object, tags); return; }
  Stage(s, Text.Number(handle), { Source: original.Source, Tags: tags, Object: null });
}
export function EnsureExtension(s, ownerHandle) {
  const owner = Text.Handle(ownerHandle, false); let original = Record(s, owner);
  if (original.Source !== null && !['OBJECTS', 'ENTITIES', 'BLOCKS', 'TABLES'].includes(original.Source.SectionName))
    throw new E.NotSupportedException('Extension dictionaries require an object, table record or entity.');
  const existing = Extension(original.Tags);
  if (existing !== null) {
    const dictionary = Require(s, M.DxfRawDictionary, existing[2]);
    if (dictionary.OwnerHandle !== owner) throw new E.InvalidDataException('Extension dictionary owner mismatch.');
    return dictionary.Handle;
  }
  EnsureRoot(s);
  const child = AddObject(s, 'DICTIONARY', owner, [T(100, 'AcDbDictionary'), T(280, 1), T(281, 1)]);
  original = Record(s, owner);
  const tags = Array.from(original.Tags);
  tags.splice(CommonEnd(tags), 0, T(102, '{ACAD_XDICTIONARY'), T(360, child.Handle), T(102, '}'));
  ReplaceRecord(s, owner, original, tags); return child.Handle;
}
export function RemoveExtensionDictionary(s, ownerHandle, deleteOwnedTree) {
  const owner = Text.Handle(ownerHandle, false), original = Record(s, owner), extension = Extension(original.Tags);
  if (extension === null) return false;
  const dictionary = Require(s, M.DxfRawDictionary, extension[2]);
  if (dictionary.OwnerHandle !== owner) throw new E.InvalidDataException('Extension dictionary owner mismatch.');
  const tags = Array.from(original.Tags); tags.splice(extension[0], extension[1] - extension[0]);
  ReplaceRecord(s, owner, original, tags);
  if (deleteOwnedTree) DeleteTree(s, extension[2]); return true;
}
export function SetDrawOrder(s, blockRecordHandle, entries, enableRegeneration) {
  if (s.store.Document.Version < 14) throw new E.NotSupportedException('SORTENTSTABLE authoring requires the conservative AutoCAD 2004+ profile.');
  const block = Text.Handle(blockRecordHandle, false), blockRecord = Record(s, block);
  if (blockRecord.Tags[0].Value !== 'BLOCK_RECORD') throw new E.ArgumentException('Draw order requires a BLOCK_RECORD handle.');
  const seen = new Set();
  const order = Collect(s, entries, e => {
    if (!(e instanceof M.DxfRawSortOrderEntry)) throw new E.ArgumentException('A redraw entry cannot be null.');
    if (seen.has(e.EntityHandle)) throw new E.ArgumentException('Duplicate redraw entity handle.');
    seen.add(e.EntityHandle); Reserve(s, e.SortHandle);
    const entity = Record(s, e.EntityHandle);
    if (entity.Source === null || !['ENTITIES', 'BLOCKS'].includes(entity.Source.SectionName))
      throw new E.ArgumentException('A redraw entry must identify a graphic entity.');
    const owners = s.index.GetOccurrences(entity.Source).filter(o => o.Role === R.Owner);
    if (owners.length !== 1 || owners[0].CanonicalHandle !== block) throw new E.ArgumentException('A redraw entity belongs to another block.');
  });
  const ext = EnsureExtension(s, block), dictionary = Require(s, M.DxfRawDictionary, ext);
  const body = [T(100, 'AcDbSortentsTable'), T(330, block)];
  for (const entry of order) body.push(T(331, entry.EntityHandle), T(5, entry.SortHandle));
  const link = dictionary.Find('ACAD_SORTENTS'); let result;
  if (link === null) {
    const child = AddObject(s, 'SORTENTSTABLE', ext, body); LinkNew(s, dictionary, 'ACAD_SORTENTS', child); result = child.Handle;
  } else {
    const value = Require(s, M.DxfRawSortentsTable, link.Handle);
    if (value.OwnerHandle !== ext) throw new E.InvalidDataException('SORTENTSTABLE owner mismatch.');
    Replace(s, value, Schema.Wrap(value, body)); result = value.Handle;
  }
  if (enableRegeneration) s.enableSortents = true;
  return result;
}
export function CurrentObjects(s) {
  const all = [];
  for (const value of s.store.Objects) {
    E.ThrowIfCancellationRequested(s.cancellation);
    const changed = value.Handle !== null ? s.changes.get(Text.Number(value.Handle)) : null;
    if (changed) { if (!changed.Deleted) all.push(changed.Object); } else all.push(value);
  }
  for (const key of s.added) {
    const value = s.changes.get(key);
    if (value && !value.Deleted) all.push(value.Object);
  }
  return all;
}
export function OwnedTree(s, handle) {
  const children = new Map();
  for (const value of CurrentObjects(s)) if (value.OwnerHandle !== null && value.Handle !== null) {
    let list = children.get(value.OwnerHandle);
    if (!list) children.set(value.OwnerHandle, list = []);
    list.push(value);
  }
  const first = Get(s, handle);
  if (!first) throw new E.ArgumentException('Unknown stored object.');
  const queue = [first], seen = new Set(), result = [];
  for (let at = 0; at < queue.length; at++) {
    E.ThrowIfCancellationRequested(s.cancellation); const current = queue[at];
    if (seen.has(current.Handle)) throw new E.InvalidDataException('Ownership cycle or duplicate object in subtree.');
    seen.add(current.Handle);
    if (current instanceof M.DxfRawOpaqueStoredObject) throw new E.NotSupportedException('Owned subtree contains an unsupported schema: ' + current.Reason);
    result.push(current);
    if (result.length > s.options.MaximumChanges) throw new E.InvalidDataException('Owned subtree exceeds the change budget.');
    for (const child of children.get(current.Handle) ?? []) queue.push(child);
  }
  return result;
}
export function CloneDictionaryTree(s, sourceHandle, parentHandle, name) {
  const source = Require(s, M.DxfRawDictionary, sourceHandle), parent = Require(s, M.DxfRawDictionary, parentHandle);
  CheckNewKey(parent, name);
  const tree = OwnedTree(s, source.Handle), mapping = new Map();
  for (const value of tree) {
    const end = CommonEnd(value.Tags);
    for (const tag of value.Tags.slice(0, end))
      if (tag.Code === 102 && tag.Value.startsWith('{') && !['{ACAD_REACTORS', '{ACAD_XDICTIONARY'].includes(tag.Value))
        throw new E.NotSupportedException('Private common controls require an application-specific cloning policy.');
    mapping.set(value.Handle, Allocate(s));
  }
  for (const value of tree) {
    const commonEnd = CommonEnd(value.Tags), tags = []; let skipDepth = 0;
    for (let i = 0; i < value.Tags.length; i++) {
      const tag = value.Tags[i];
      if (i < commonEnd && tag.Code === 102) {
        const text = tag.Value;
        if (text === '{ACAD_REACTORS' || skipDepth !== 0) {
          if (text.startsWith('{')) skipDepth++; else if (text === '}') skipDepth--;
          continue;
        }
      }
      if (skipDepth !== 0) continue;
      if (i < commonEnd && tag.Code === 5) tags.push(T(5, mapping.get(value.Handle)));
      else if (i < commonEnd && tag.Code === 330 && value.Handle === source.Handle) tags.push(T(330, parent.Handle));
      else if ((tag.Code >= 330 && tag.Code <= 369 || tag.Code === 1005) && mapping.has(Text.Handle(tag.Value, true)))
        tags.push(T(tag.Code, mapping.get(Text.Handle(tag.Value, true))));
      else tags.push(tag);
    }
    if (value.OwnerHandle === null) tags.splice(2, 0, T(330, value.Handle === source.Handle ? parent.Handle : mapping.get(value.OwnerHandle)));
    const clone = Schema.Read(ReadOnlyList(tags), null, s.options.MaximumPayloadTags);
    if (clone instanceof M.DxfRawOpaqueStoredObject) throw new E.InvalidDataException(clone.Reason);
    if (s.store.Objects.length + s.added.length >= s.options.MaximumObjects) throw new E.InvalidDataException('OBJECTS budget exceeded.');
    const key = Text.Number(clone.Handle);
    Stage(s, key, { Source: null, Tags: tags, Object: clone, Deleted: false }); s.added.push(key);
  }
  const copiedRoot = Get(s, mapping.get(source.Handle));
  LinkNew(s, Require(s, M.DxfRawDictionary, parent.Handle), name, copiedRoot); return copiedRoot.Handle;
}
export function DeleteTree(s, handle) {
  const canonical = Text.Handle(handle, false);
  if (canonical === s.root) throw new E.InvalidOperationException('The root dictionary cannot be deleted.');
  const tree = OwnedTree(s, canonical), deleted = new Set(tree.map(o => Text.Number(o.Handle)));
  const current = Build(s, false), index = DxfRawHandleIndex.Create(current, null, s.cancellation);
  const sourceIds = new Map(index.Occurrences.filter(o => o.Role === R.Identity && o.Record !== null).map(o => [o.Record, o.NumericHandle]));
  for (const item of index.Occurrences) {
    E.ThrowIfCancellationRequested(s.cancellation);
    if (!deleted.has(item.NumericHandle) || [R.Identity, R.HeaderSeed, R.Arbitrary].includes(item.Role)) continue;
    if (item.Record !== null && sourceIds.has(item.Record) && deleted.has(sourceIds.get(item.Record))) continue;
    throw new E.InvalidOperationException('Deleting the subtree would invalidate an external or opaque exposed reference to ' + item.Handle + '.');
  }
  for (const value of tree) Stage(s, Text.Number(value.Handle), { Source: value.SourceRecord, Deleted: true, Object: value });
}
