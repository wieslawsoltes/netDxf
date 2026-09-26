// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfTag } from './DxfTag.js';
import { DxfRawHandleRole as R, DxfRawHandleDiagnosticKind as D, ParseHandle } from './DxfRawHandleModel.js';
import { CheckRecord } from './DxfRawHandleIndex.js';
import { ReadOnlyList } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidDataException,
  InvalidOperationException, OverflowException, ThrowIfCancellationRequested as Cancel } from '../../runtime/Errors.js';
export const DxfRawReferenceTraversal = Object.freeze({ None: 0, HardPointers: 1, SoftPointers: 2,
  HardOwnership: 4, SoftOwnership: 8, ParentOwners: 16, Reactors: 32, XData: 64, HeaderReferences: 128, All: 255 });
const traversalFor = new Uint16Array([0, 16, 2, 1, 8, 4, 32, 4, 64, 0, 0, 128, 0]);
export class DxfRawDependencyClosure {
  constructor(records, unresolved, ambiguous, opaque) {
    this.Records = ReadOnlyList(records.sort((a, b) => a.StartTagIndex - b.StartTagIndex));
    this.UnresolvedReferences = ReadOnlyList(unresolved.sort((a, b) => a.TagIndex - b.TagIndex));
    this.AmbiguousReferences = ReadOnlyList(ambiguous.sort((a, b) => a.TagIndex - b.TagIndex));
    this.UninterpretedHandles = ReadOnlyList(opaque.sort((a, b) => a.TagIndex - b.TagIndex));
    Object.freeze(this);
  }
  get AreSelectedReferencesResolved() { return this.UnresolvedReferences.length === 0 && this.AmbiguousReferences.length === 0; }
}
/** Partial-class implementation. State is private to DxfRawHandleIndex and never returned. */
export function GetDependencyClosure(s, roots, traversal, token) {
  if (roots == null) throw new ArgumentNullException('roots');
  if (!Number.isInteger(traversal) || traversal < 0 || traversal > 255) throw new ArgumentOutOfRangeException('traversal', traversal);
  Cancel(token);
  const selected = new Set(), pending = [], result = []; let count = 0;
  for (const root of roots) {
    Cancel(token);
    if (count++ >= s.options.MaximumOccurrences) throw new InvalidDataException('Raw dependency root enumeration budget exceeded.');
    CheckRecord(s, root);
    if (!selected.has(root)) { selected.add(root); pending.push(root); result.push(root); }
  }
  const unresolved = [], ambiguous = [], opaque = [];
  // Index-based queue avoids Array.shift's repeated compaction on large dependency graphs.
  for (let at = 0; at < pending.length; at++) {
    Cancel(token); const items = s.byRecord.get(pending[at]); if (!items) continue;
    for (const item of items) {
      Cancel(token);
      if (item.Role === R.Opaque) opaque.push(item);
      if (item.NumericHandle === 0n || (traversalFor[item.Role] & traversal) === 0) continue;
      const targets = s.identities.get(item.NumericHandle);
      if (!targets) { unresolved.push(item); continue; }
      if (targets.length !== 1) { ambiguous.push(item); continue; }
      const next = targets[0].Record;
      if (!selected.has(next)) { selected.add(next); pending.push(next); result.push(next); }
    }
  }
  return new DxfRawDependencyClosure(result, unresolved, ambiguous, opaque);
}
export function RemapHandles(s, mapping, token) {
  if (mapping == null) throw new ArgumentNullException('mapping');
  Cancel(token);
  const entries = mapping instanceof Map ? mapping :
    (typeof mapping === 'object' && (Object.getPrototypeOf(mapping) === Object.prototype || Object.getPrototypeOf(mapping) === null)) ? Object.entries(mapping) : null;
  if (!entries) throw new ArgumentException('Expected a Map or plain string-keyed dictionary.', 'mapping');
  const changes = new Map(), supplied = new Set(); let count = 0;
  for (const [key, value] of entries) {
    Cancel(token);
    if (count++ >= s.options.MaximumOccurrences) throw new InvalidDataException('Raw handle mapping budget exceeded.');
    const source = ParseHandle(key, 'mapping'), target = ParseHandle(value, 'mapping');
    if (source === 0n || target === 0n) throw new ArgumentException('Identity remapping cannot use the null handle.', 'mapping');
    if (supplied.has(source)) throw new ArgumentException('Mapping keys alias the same numeric source handle.', 'mapping');
    supplied.add(source);
    if (s.identities.get(source)?.length !== 1) throw new ArgumentException('Each source must have exactly one indexed identity.', 'mapping');
    if (source !== target) changes.set(source, target);
  }
  if (changes.size === 0) return s.document;
  for (const diagnostic of s.diagnostics) {
    Cancel(token);
    if ([D.DuplicateIdentity, D.MultipleIdentities, D.NullIdentity, D.InvalidControlGroup].includes(diagnostic.Kind))
      throw new InvalidOperationException('Handle remapping requires unambiguous identities and valid common control framing.');
  }
  const targets = new Set();
  for (const [source, target] of changes) {
    Cancel(token);
    if (targets.has(target)) throw new ArgumentException('Multiple identities cannot share a target handle.', 'mapping');
    targets.add(target);
    if (s.identities.has(target) && !changes.has(target)) throw new ArgumentException('A target collides with an unchanged object identity.', 'mapping');
    if (!s.identities.has(target) && s.incoming.has(target)) throw new ArgumentException('A target would capture a previously unresolved reference.', 'mapping');
  }
  let maximum = 0n;
  for (const identity of s.identities.keys()) {
    Cancel(token); const mapped = changes.get(identity) ?? identity; if (mapped > maximum) maximum = mapped;
  }
  let seed = null; const replacements = new Map();
  for (const item of s.occurrences) {
    Cancel(token);
    if (item.Role === R.Opaque && (changes.has(item.NumericHandle) || targets.has(item.NumericHandle)))
      throw new InvalidOperationException('An opaque handle slot is affected; an application-specific remapping contract is required.');
    if (item.Role === R.HeaderSeed) {
      if (seed !== null) throw new InvalidOperationException('Ambiguous repeated HANDSEED declarations cannot be repaired implicitly.');
      seed = item; continue;
    }
    if ((item.Role === R.Identity || item.IsReference) && changes.has(item.NumericHandle))
      replacements.set(item.TagIndex, new DxfTag(item.Code, changes.get(item.NumericHandle).toString(16).toUpperCase()));
  }
  if (seed !== null && seed.NumericHandle <= maximum) {
    if (maximum === 0xffffffffffffffffn) throw new OverflowException('No representable next HANDSEED remains after remapping.');
    replacements.set(seed.TagIndex, new DxfTag(seed.Code, (maximum + 1n).toString(16).toUpperCase()));
  }
  function* RemappedTags() {
    for (let i = 0; i < s.document.Tags.length; i++) {
      if ((i & 255) === 0) Cancel(token);
      yield replacements.get(i) ?? s.document.Tags[i];
    }
  }
  Cancel(token); const result = s.document.WithTags(RemappedTags()); Cancel(token); return result;
}
