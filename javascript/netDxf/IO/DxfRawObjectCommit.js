// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
import { DxfRawObjectStore, DxfObjectText as Text, DxfObjectSchema as Schema, ObjectStoreState } from './DxfRawObjectStore.js';
import * as M from './DxfRawObjectModel.js';
import { DxfTag } from './DxfTag.js';
import { EnsureOpen, SameTags, InvalidGraphKinds } from './DxfRawObjectTransaction.js';
import { CurrentObjects } from './DxfRawObjectGraph.js';
import { OrdinalIgnoreCaseEquals as Eq } from '../../runtime/Collections.js';
import * as E from '../../runtime/Errors.js';
const T = (code, value) => new DxfTag(code, value);
const Append = (target, values) => { for (const value of values) target.push(value); };

export function Commit(s) {
  EnsureOpen(s);
  if (s.undo !== null) throw new E.InvalidOperationException('An object transaction cannot be reentered.');
  if (s.changes.size === 0 && !s.enableSortents) { s.closed = true; return s.store.Document; }
  const result = Build(s, true); ValidateCommit(s, result); E.ThrowIfCancellationRequested(s.cancellation);
  s.closed = true; return result;
}
export function Build(s, metadata) {
  const source = s.store.Document, byStart = new Map();
  for (const change of s.changes.values()) if (change.Source !== null) byStart.set(change.Source.StartTagIndex, change);
  const objects = UniqueSection(source, 'OBJECTS'), newObjectTags = [];
  for (const key of s.added) if (!s.changes.get(key).Deleted) Append(newObjectTags, s.changes.get(key).Tags);
  const insertions = new Map();
  const insert = (at, tags) => {
    let list = insertions.get(at); if (!list) insertions.set(at, list = []); Append(list, tags);
  };
  if (objects !== null) insert(objects.EndTagIndex - 1, newObjectTags);
  else if (newObjectTags.length !== 0) {
    const complete = [T(0, 'SECTION'), T(2, 'OBJECTS')]; Append(complete, newObjectTags); complete.push(T(0, 'ENDSEC'));
    const thumbnail = UniqueSection(source, 'THUMBNAILIMAGE'); insert(thumbnail === null ? FindEof(source) : thumbnail.StartTagIndex, complete);
  }
  const replacements = new Map();
  if (metadata) { UpdateHeader(s, insert, replacements); UpdateClasses(s, insert, byStart); }
  const result = [];
  for (let i = 0; i < source.Tags.length;) {
    if ((i & 1023) === 0) E.ThrowIfCancellationRequested(s.cancellation);
    if (insertions.has(i)) Append(result, insertions.get(i));
    const replacement = byStart.get(i);
    if (replacement) {
      if (!replacement.Deleted) Append(result, replacement.Tags);
      i = replacement.Source.EndTagIndex;
    } else { result.push(replacements.get(i) ?? source.Tags[i]); i++; }
  }
  if (insertions.has(source.Tags.length)) Append(result, insertions.get(source.Tags.length));
  E.ThrowIfCancellationRequested(s.cancellation);
  return SameTags(source.Tags, result) ? source : source.WithTags(result);
}
export function UniqueSection(document, name) {
  let result = null;
  for (const section of document.Sections) if (Eq(section.Name, name)) {
    if (result !== null) throw new E.InvalidDataException('Multiple ' + name + ' sections are ambiguous.');
    result = section;
  }
  return result;
}
function FindEof(source) {
  for (let i = source.Tags.length - 1; i >= 0; i--) if (source.Tags[i].Code === 0 && source.Tags[i].Value === 'EOF') return i;
  throw new E.InvalidDataException('Missing EOF marker.');
}
const ValueTags = record => Array.from(record.Tags, (tag, index) => ({ tag, index })).filter(p => p.tag.Code !== 999 && p.tag.Code !== 9);
function UpdateHeader(s, insert, replacements) {
  const header = UniqueSection(s.store.Document, 'HEADER');
  if (header === null) throw new E.NotSupportedException('Object editing requires an explicit HEADER profile.');
  const seeds = header.Records.filter(r => r.Name === '$HANDSEED');
  if (seeds.length > 1) throw new E.InvalidDataException('Multiple HANDSEED records.');
  if (s.added.length !== 0) {
    if (seeds.length === 0) insert(header.EndTagIndex - 1, [T(9, '$HANDSEED'), T(5, s.next.toString(16).toUpperCase())]);
    else {
      const tags = ValueTags(seeds[0]);
      if (tags.length !== 1 || tags[0].tag.Code !== 5) throw new E.InvalidDataException('Malformed HANDSEED record.');
      if (Text.Number(tags[0].tag.Value) < s.next) replacements.set(seeds[0].StartTagIndex + tags[0].index, T(5, s.next.toString(16).toUpperCase()));
    }
  }
  if (!s.enableSortents) return;
  const sorting = header.Records.filter(r => r.Name === '$SORTENTS');
  if (sorting.length > 1) throw new E.InvalidDataException('Multiple SORTENTS variables.');
  if (sorting.length === 0) insert(header.EndTagIndex - 1, [T(9, '$SORTENTS'), T(280, 16)]);
  else {
    const flags = ValueTags(sorting[0]);
    if (flags.length !== 1 || flags[0].tag.Code !== 280) throw new E.InvalidDataException('Malformed SORTENTS variable.');
    const value = flags[0].tag.Value;
    if ((value & 16) === 0) replacements.set(sorting[0].StartTagIndex + flags[0].index, T(280, value | 16));
  }
}
function UpdateClasses(s, insert, byStart) {
  const counts = objects => {
    const result = new Map(); for (const o of objects) result.set(o.TypeName, (result.get(o.TypeName) ?? 0) + 1); return result;
  };
  const before = counts(s.store.Objects), after = counts(CurrentObjects(s));
  const touched = Object.keys(Schema.RegisteredClasses).filter(k => (before.get(k) ?? 0) !== (after.get(k) ?? 0)).sort();
  if (touched.length === 0) return;
  const classes = UniqueSection(s.store.Document, 'CLASSES'), missing = [];
  for (const type of touched) {
    E.ThrowIfCancellationRequested(s.cancellation);
    const found = classes === null ? [] : classes.Records.filter(r => r.Name === 'CLASS' && Array.from(r.Tags).some(t => t.Code === 1 && t.Value === type));
    if (found.length > 1) throw new E.InvalidDataException('Duplicate CLASS definition: ' + type);
    const count = after.get(type) ?? 0;
    if (found.length === 0) {
      if (count === 0) continue;
      missing.push(T(0, 'CLASS'), T(1, type), T(2, Schema.RegisteredClasses[type]), T(3, 'ObjectDBX Classes'), T(90, 0));
      if (s.store.Document.Version >= 14) missing.push(T(91, count));
      missing.push(T(280, 0), T(281, 0));
    } else {
      const record = found[0], tags = Array.from(record.Tags), names = tags.filter(t => t.Code === 2), entities = tags.filter(t => t.Code === 281);
      if (names.length !== 1 || names[0].Value !== Schema.RegisteredClasses[type] || entities.length !== 1 || entities[0].Value !== 0)
        throw new E.InvalidDataException('Conflicting object CLASS definition: ' + type);
      const instanceCounts = tags.map((tag, index) => ({ tag, index })).filter(p => p.tag.Code === 91);
      if (instanceCounts.length > 1) throw new E.InvalidDataException('Duplicate CLASS instance count.');
      if (s.store.Document.Version >= 14) {
        if (instanceCounts.length === 1) tags[instanceCounts[0].index] = T(91, count);
        else { const position = tags.findIndex(t => t.Code === 280); tags.splice(position >= 0 ? position : tags.length, 0, T(91, count)); }
        byStart.set(record.StartTagIndex, { Source: record, Tags: tags });
      }
    }
  }
  if (missing.length === 0) return;
  if (classes !== null) insert(classes.EndTagIndex - 1, missing);
  else {
    const header = UniqueSection(s.store.Document, 'HEADER');
    if (header === null) throw new E.InvalidDataException('Missing HEADER for CLASSES insertion.');
    insert(header.EndTagIndex, [T(0, 'SECTION'), T(2, 'CLASSES'), ...missing, T(0, 'ENDSEC')]);
  }
}
function ValidateCommit(s, document) {
  const result = DxfRawObjectStore.Open(document, s.options, s.cancellation), { index } = ObjectStoreState(result);
  for (const diagnostic of index.Diagnostics) if (InvalidGraphKinds.includes(diagnostic.Kind))
    throw new E.InvalidDataException('Committed common graph is invalid: ' + diagnostic.Message);
  const pointer = handle => {
    if (handle === null || handle === '0') return;
    if (index.FindDefinitions(handle).length !== 1) throw new E.InvalidDataException('Unresolved or ambiguous edited reference: ' + handle);
  };
  for (const change of s.changes.values()) {
    E.ThrowIfCancellationRequested(s.cancellation);
    if (change.Deleted || change.Object === null) continue;
    const value = result.Get(change.Object.Handle);
    if (value === null || value instanceof M.DxfRawOpaqueStoredObject) throw new E.InvalidDataException('Edited schema could not be read back.');
    pointer(value.OwnerHandle);
    for (const occurrence of index.GetOccurrences(value.SourceRecord)) if (occurrence.IsReference) pointer(occurrence.CanonicalHandle);
    if (value instanceof M.DxfRawDictionary) {
      for (const entry of value.Entries) {
        const target = result.Get(entry.Handle);
        if (target === null) throw new E.InvalidDataException('Dictionary target is not an OBJECTS record: ' + entry.Handle);
        if (target.OwnerHandle !== value.Handle || target.Handle === value.Handle)
          throw new E.InvalidDataException('Dictionary entry must target another object with that dictionary as its common owner.');
      }
      if (value.HasDefault && (value.DefaultHandle === null || value.DefaultHandle === '0' || result.Get(value.DefaultHandle) === null))
        throw new E.InvalidDataException('An edited dictionary-with-default requires an existing nonzero default object.');
    } else if (value instanceof M.DxfRawXRecord) {
      for (const tag of value.Data) if (tag.Code >= 330 && tag.Code <= 369) pointer(Text.Handle(tag.Value, true));
    } else if (value instanceof M.DxfRawIdBuffer) for (const handle of value.Handles) pointer(handle);
    else if (value instanceof M.DxfRawSortentsTable) {
      pointer(value.BlockRecordHandle);
      if (index.FindDefinitions(value.BlockRecordHandle)[0].Record.Name !== 'BLOCK_RECORD')
        throw new E.InvalidDataException('Draw-order block reference does not identify a BLOCK_RECORD.');
      for (const entry of value.Entries) pointer(entry.EntityHandle);
    }
  }
}
