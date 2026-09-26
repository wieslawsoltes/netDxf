// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { DxfHandleKind } from '../IO/DxfGroupCode.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { IsAncestor, RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { TableSnapshot } from '../../runtime/TablePayload.js';
import { DependencyView, CanonicalDependencyHandle } from '../../runtime/StoredDependencyCollections.js';
import { FormatException, InvalidOperationException, NotSupportedException, NullReferenceException } from '../../runtime/Errors.js';

/** Retained FIELD and leading relationship projections. Construction/Resolve adapt
 * the internal loader; evaluator code, flags and caches are never executed. */
export class DxfStoredField extends DxfDatabaseObject {
  #source; #children = new ReferenceList(); #objects = new ReferenceList(); #references = new ReferenceList();
  #identities = new Map(); #resolved = false;
  constructor(source, payload, evaluator, code) {
    super('FIELD');
    if (source == null) throw new NullReferenceException();
    this.#source = source;
    for (const [name, value] of Object.entries({SourceVersion: source.DrawingVariables.AcadVer,
      Payload: TableSnapshot(payload), EvaluatorId: evaluator, FieldCode: code,
      Children: DependencyView(this.#children), ReferencedObjects: DependencyView(this.#objects),
      References: DependencyView(this.#references)})) Object.defineProperty(this, name, {value, enumerable: true});
  }
  static IsSemantic(tag) {
    return ![DxfHandleKind.None, DxfHandleKind.Arbitrary, DxfHandleKind.ObjectIdentity].includes(tag.HandleKind);
  }
  Resolve(children, objects, resolve) {
    for (const tag of this.Payload) {
      if (!DxfStoredField.IsSemantic(tag)) continue;
      const handle = CanonicalDependencyHandle(tag.Value);
      if (handle === '0') continue;
      const target = resolve(handle);
      if (target == null) throw new FormatException('Unresolved source FIELD dependency: ' + handle);
      this.#identities.set(handle, target); this.#references.Add(target);
    }
    const seen = new Set();
    for (const handle of children) {
      const child = resolve(handle);
      if (!(child instanceof DxfStoredField) || child.Owner !== this || seen.has(child) || IsAncestor(child, this))
        throw new FormatException('FIELD children require unique FIELD targets and reciprocal source ownership: ' + handle);
      seen.add(child); this.#children.Add(child);
    }
    for (const handle of objects) this.#objects.Add(CanonicalDependencyHandle(handle) === '0' ? null : resolve(handle));
    this.#resolved = true;
    const errors = new ReferenceList(); this.ValidateDatabaseSchema(this.Database, errors);
    if (errors.Count) throw new FormatException(Array.from(errors).join('; '));
  }
  ValidateSource(document) {
    if (document !== this.#source || !this.#resolved || this.IsErased)
      throw new InvalidOperationException('A stored FIELD requires its live source document.');
    if (document.DrawingVariables.AcadVer !== this.SourceVersion)
      throw new NotSupportedException('Stored FIELD profile conversion requires its evaluator schema.');
  }
  get DatabaseReferences() { return this.#references; }
  get DeclaredOwnedObjects() { return this.#children; }
  get AllocationReservations() { return this.Payload; }
  CloneShell() { throw new NotSupportedException('Stored FIELD cloning requires its complete private evaluator reference grammar.'); }
  ValidateDatabaseSchema(database, errors) {
    if (!this.#resolved || database == null || database.Document !== this.#source) {
      errors.Add('Stored FIELD requires its registered source document: ' + (this.Handle ?? '')); return;
    }
    const ancestors = new Set();
    for (let owner = this; owner !== null; owner = owner.Owner) {
      if (ancestors.has(owner)) { errors.Add('FIELD source ownership contains a cycle: ' + this.Handle); break; }
      ancestors.add(owner);
    }
    if (this.#source.DrawingVariables.AcadVer !== this.SourceVersion) errors.Add('Stored FIELD source profile changed: ' + this.Handle);
    for (const [handle, target] of this.#identities) if (this.#source.GetObjectByHandle(handle) !== target)
      errors.Add('Stored FIELD dependency identity changed: ' + handle);
    for (const item of database.Items) if (item.Owner === this && item !== this.ExtensionDictionary && !this.#children.Contains(item instanceof DxfStoredField ? item : null))
      errors.Add('FIELD has a child outside its declared leading slots: ' + item.Handle);
    for (const child of this.#children) if (child.Owner !== this) errors.Add('FIELD child ownership changed: ' + child.Handle);
  }
}
RegisterDatabaseModel('DxfStoredField', DxfStoredField);
