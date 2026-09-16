// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfRawDocument } from './DxfRawDocument.js';
import { DxfTagValueType, DxfHandleKind as K } from './DxfGroupCode.js';
import { DxfRawHandleRole as R, DxfRawHandleDiagnosticKind as D, DxfRawHandleOccurrence,
  DxfRawHandleDiagnostic, DxfRawHandleIndexOptions, ParseHandle } from './DxfRawHandleModel.js';
import { GetDependencyClosure, RemapHandles } from './DxfRawHandleOperations.js';
import { ReadOnlyList, OrdinalIgnoreCaseEquals as Eq } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, InvalidDataException, NotSupportedException,
  ThrowIfCancellationRequested as Cancel } from '../../runtime/Errors.js';
const construct = Symbol('DxfRawHandleIndex.Create');
const Empty = ReadOnlyList([]);
const roleForKind = [R.Opaque, R.Opaque, R.Arbitrary, R.SoftPointer, R.HardPointer, R.SoftOwner, R.HardOwner, R.Opaque];
const RoleForCode = kind => roleForKind[kind] ?? R.Opaque;
function Append(map, key, item) { let values = map.get(key); if (!values) map.set(key, values = []); values.push(item); }

/** Immutable structural index. Opaque/private payloads are never inferred as dependencies. */
export class DxfRawHandleIndex {
  #state;
  constructor(document, options, token, key) {
    if (key !== construct) throw new NotSupportedException('Use DxfRawHandleIndex.Create.');
    const s = this.#state = { document, options, identities: new Map(), incoming: new Map(),
      byRecord: new Map(), records: new Set(), occurrences: [], diagnostics: [] };
    for (const section of document.Sections) {
      Cancel(token); let cursor = section.StartTagIndex, table = null;
      for (const record of section.Records) {
        s.records.add(record); this.#readUnscoped(cursor, record.StartTagIndex, token);
        if (Eq(section.Name, 'TABLES') && Eq(record.Name, 'TABLE')) {
          table = null;
          for (const tag of record.Content) if (tag.Code === 2) { table = tag.Value; break; }
        }
        this.#readRecord(record, table, token);
        if (Eq(record.Name, 'ENDTAB')) table = null;
        cursor = record.EndTagIndex;
      }
      this.#readUnscoped(cursor, section.EndTagIndex, token);
    }
    this.#resolveDiagnostics(token); this.#findOwnerCycles(token);
    // Freeze once and reuse immutable views rather than allocating wrappers per lookup.
    for (const map of [s.identities, s.incoming, s.byRecord])
      for (const [key, values] of map) map.set(key, ReadOnlyList(values));
    this.Occurrences = s.occurrences = ReadOnlyList(s.occurrences);
    this.Diagnostics = s.diagnostics = ReadOnlyList(s.diagnostics);
    Object.freeze(this);
  }
  static Create(document, options = null, cancellationToken = null) {
    if (document == null) throw new ArgumentNullException('document');
    Cancel(cancellationToken);
    if (!(document instanceof DxfRawDocument)) throw new ArgumentException('Expected DxfRawDocument.', 'document');
    if (options != null && !(options instanceof DxfRawHandleIndexOptions)) throw new ArgumentException('Expected DxfRawHandleIndexOptions.', 'options');
    return new DxfRawHandleIndex(document, options ?? new DxfRawHandleIndexOptions(), cancellationToken, construct);
  }
  FindDefinitions(handle) { return this.#state.identities.get(ParseHandle(handle, 'handle')) ?? Empty; }
  FindReferences(handle) { return this.#state.incoming.get(ParseHandle(handle, 'handle')) ?? Empty; }
  GetOccurrences(record) {
    CheckRecord(this.#state, record); return this.#state.byRecord.get(record) ?? Empty;
  }
  GetDependencyClosure(roots, traversal = 5, cancellationToken = null) {
    return GetDependencyClosure(this.#state, roots, traversal, cancellationToken);
  }
  RemapHandles(mapping, cancellationToken = null) { return RemapHandles(this.#state, mapping, cancellationToken); }
  #readUnscoped(start, end, token) {
    for (let i = start; i < end; i++) {
      if ((i & 255) === 0) Cancel(token);
      const tag = this.#state.document.Tags[i];
      if (tag.ValueType === DxfTagValueType.Handle) this.#add(null, i, tag, R.Opaque, null, null);
    }
  }
  #readRecord(record, table, token) {
    const header = Eq(record.SectionName, 'HEADER');
    const database = ['ENTITIES', 'BLOCKS', 'TABLES', 'OBJECTS'].some(name => Eq(record.SectionName, name));
    const dimstyle = Eq(record.SectionName, 'TABLES') && Eq(table, 'DIMSTYLE') && Eq(record.Name, 'DIMSTYLE');
    const xrecord = Eq(record.SectionName, 'OBJECTS') && Eq(record.Name, 'XRECORD');
    let payload = false, embedded = false, uncertain = false, subclass = null, application = null;
    const groups = [];
    for (let i = record.StartTagIndex + 1; i < record.EndTagIndex; i++) {
      if ((i & 255) === 0) Cancel(token);
      const tag = this.#state.document.Tags[i];
      if (tag.Code === 999) continue;
      if (database && !payload && groups.length === 0 && tag.Code === 101 && Eq(tag.Value, 'Embedded Object')) {
        payload = true; embedded = true; application = null; continue;
      }
      if (!payload && tag.Code === 102) {
        const control = tag.Value;
        if (control.startsWith('{')) groups.push(control.slice(1));
        else if (control === '}' && groups.length !== 0) groups.pop();
        else {
          this.#diagnostic(D.InvalidControlGroup, record, i, null, 'Unmatched or invalid group-102 control string.');
          uncertain = true;
        }
        continue;
      }
      if (!embedded && groups.length === 0) {
        if (!payload && tag.Code === 100) {
          subclass = tag.Value;
          if (xrecord && Eq(subclass, 'AcDbXrecord')) payload = true;
        }
        if (tag.Code === 1001) application = tag.Value;
        else if (tag.Code < 1000) application = null;
      }
      if (tag.ValueType !== DxfTagValueType.Handle) continue;
      let role = R.Opaque;
      const context = embedded ? 'Embedded Object' : groups.length !== 0 ? groups[groups.length - 1] : application;
      if (!uncertain && !embedded) {
        if (groups.length !== 0) {
          if (database && groups.length === 1 && Eq(groups[0], 'ACAD_REACTORS') && tag.Code === 330) role = R.Reactor;
          else if (database && groups.length === 1 && Eq(groups[0], 'ACAD_XDICTIONARY') && tag.Code === 360) role = R.ExtensionDictionary;
        } else if (header) {
          if (Eq(record.Name, '$HANDSEED') && tag.Code === 5) role = R.HeaderSeed;
          else if (tag.HandleKind === K.Arbitrary) role = R.Arbitrary;
          else if (RoleForCode(tag.HandleKind) !== R.Opaque) role = R.HeaderReference;
        } else if (database) {
          if (application !== null) role = tag.Code === 1005 ? R.XData : R.Opaque;
          else if (!payload) {
            if (subclass === null && tag.Code === (dimstyle ? 105 : 5)) role = R.Identity;
            else if (subclass === null && tag.Code === 330) role = R.Owner;
            else role = RoleForCode(tag.HandleKind);
          }
        }
      }
      this.#add(record, i, tag, role, context, subclass);
    }
    if (groups.length !== 0) this.#diagnostic(D.InvalidControlGroup, record, record.EndTagIndex - 1, null, 'Unterminated group-102 control block.');
  }
  #add(record, index, tag, role, context, subclass) {
    const s = this.#state;
    if (s.occurrences.length >= s.options.MaximumOccurrences) throw new InvalidDataException('Raw handle occurrence budget exceeded.');
    const item = new DxfRawHandleOccurrence(record, index, tag, role, context, subclass);
    s.occurrences.push(item);
    if (record !== null) Append(s.byRecord, record, item);
    if (role === R.Identity) Append(s.identities, item.NumericHandle, item);
    else if (item.IsReference) Append(s.incoming, item.NumericHandle, item);
  }
  #diagnostic(kind, record, index, handle, message) {
    const s = this.#state;
    if (s.diagnostics.length >= s.options.MaximumDiagnostics) throw new InvalidDataException('Raw handle diagnostic budget exceeded.');
    s.diagnostics.push(new DxfRawHandleDiagnostic(kind, record, index, handle, message));
  }
  #resolveDiagnostics(token) {
    const s = this.#state, seenIdentity = new Set(), seenOwner = new Set();
    for (const item of s.occurrences) {
      Cancel(token);
      if (item.Role === R.Identity) {
        if (item.NumericHandle === 0n) this.#diagnostic(D.NullIdentity, item.Record, item.TagIndex, item.CanonicalHandle, 'An object identity cannot be the null handle.');
        if (s.identities.get(item.NumericHandle).length > 1) this.#diagnostic(D.DuplicateIdentity, item.Record, item.TagIndex, item.CanonicalHandle, 'Duplicate numeric object identity.');
        if (seenIdentity.has(item.Record)) this.#diagnostic(D.MultipleIdentities, item.Record, item.TagIndex, item.CanonicalHandle, 'More than one identity slot in the record.');
        seenIdentity.add(item.Record);
      }
      if (item.Role === R.Owner) {
        if (seenOwner.has(item.Record)) this.#diagnostic(D.MultipleOwners, item.Record, item.TagIndex, item.CanonicalHandle, 'More than one common owner slot in the record.');
        seenOwner.add(item.Record);
      }
      if (!item.IsReference || item.NumericHandle === 0n) continue;
      const matches = s.identities.get(item.NumericHandle);
      if (!matches) this.#diagnostic(D.UnresolvedReference, item.Record, item.TagIndex, item.CanonicalHandle, 'Nonzero reference has no indexed identity.');
      else if (matches.length !== 1) this.#diagnostic(D.AmbiguousReference, item.Record, item.TagIndex, item.CanonicalHandle, 'Reference has more than one indexed definition.');
    }
  }
  #findOwnerCycles(token) {
    const s = this.#state, owners = new Map(), multiple = new Set();
    for (const item of s.occurrences) if (item.Role === R.Owner) {
      if (owners.has(item.Record)) multiple.add(item.Record); else owners.set(item.Record, item);
    }
    for (const record of multiple) owners.delete(record);
    const finished = new Set();
    for (const seed of s.occurrences) {
      Cancel(token);
      if (seed.Role !== R.Identity || seed.NumericHandle === 0n || s.identities.get(seed.NumericHandle).length !== 1 || finished.has(seed.Record)) continue;
      const path = [], current = new Map(); let node = seed.Record;
      while (node !== null && !finished.has(node)) {
        Cancel(token);
        if (current.has(node)) {
          for (let j = current.get(node); j < path.length; j++) {
            const owner = owners.get(path[j]);
            this.#diagnostic(D.OwnerCycle, path[j], owner.TagIndex, owner.CanonicalHandle, 'Cycle among common owner links.');
          }
          break;
        }
        current.set(node, path.length); path.push(node);
        const next = owners.get(node), definitions = next && next.NumericHandle !== 0n ? s.identities.get(next.NumericHandle) : null;
        node = definitions?.length === 1 ? definitions[0].Record : null;
      }
      for (const done of path) finished.add(done);
    }
  }
}
export function CheckRecord(state, record) {
  if (record == null) throw new ArgumentNullException('record');
  if (!state.records.has(record)) throw new ArgumentException('The record belongs to another raw snapshot.', 'record');
}
