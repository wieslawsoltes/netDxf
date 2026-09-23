// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
import { Dimension } from '../Entities/Dimension.js';
import { EntityObject } from '../Entities/EntityObject.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ValueEquals } from '../../runtime/GenericDictionary.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { TableSnapshot } from '../../runtime/TablePayload.js';
import { DependencyView } from '../../runtime/StoredDependencyCollections.js';
import { FormatException, NotSupportedException, NullReferenceException } from '../../runtime/Errors.js';

/** Immutable stored point projection; Bind is the explicit internal-loader adapter. */
export class DxfStoredDimAssocPoint {
  #point; #geometry = null; #parameter = new DataView(new ArrayBuffer(8));
  constructor(slot, osnap, handle, subentity, marker, parameter, point) {
    this.#point = Copy(point); this.#parameter.setFloat64(0, parameter);
    Object.assign(this, {PointIndex: slot, OsnapType: osnap, GeometryHandle: handle, SubentityType: subentity, MarkerIndex: marker});
    Object.freeze(this);
  }
  get Geometry() { return this.#geometry; }
  get NearParameter() { return this.#parameter.getFloat64(0); }
  get Point() { return Copy(this.#point); }
  Bind(geometry) { this.#geometry = geometry; }
}
/** Source-bound association identities, not an osnap or dimension evaluator. */
export class DxfStoredDimAssoc extends DxfDatabaseObject {
  #source; #dimensionHandle; #dimension = null; #sourceOwner = null; #sourceReactors = null;
  #backlinks = new Map(); #references = new ReferenceList(); #resolved = false;
  constructor(source, tags, dimensionHandle, mask, transSpace, rotatedType, points) {
    super('DIMASSOC');
    if (source == null) throw new NullReferenceException();
    this.#source = source; this.#dimensionHandle = dimensionHandle;
    for (const [name, value] of Object.entries({SourceVersion: source.DrawingVariables.AcadVer,
      AssociativityMask: mask, IsTransSpace: transSpace !== 0, RotatedDimensionType: rotatedType,
      Tags: TableSnapshot(tags), PointReferences: TableSnapshot(points)})) Object.defineProperty(this, name, {value, enumerable: true});
  }
  get Dimension() { return this.#dimension; }
  get References() { return DependencyView(this.#references); }
  get DatabaseReferences() { return this.#references; }
  get AllocationReservations() { return this.Tags; }
  CloneShell() { throw new NotSupportedException('Stored DIMASSOC cloning requires the complete dimension association lifecycle.'); }
  #ownedCorrectly() {
    return this.#sourceOwner !== null && this.#sourceOwner.Owner === this.Dimension && this.Dimension.ExtensionDictionary === this.#sourceOwner &&
      Array.from(this.#sourceOwner.Entries).some(entry => entry.Name === 'ACAD_DIMASSOC' && entry.IsHardOwner && entry.Target === this);
  }
  Resolve(resolve) {
    const dimension = resolve(this.#dimensionHandle);
    this.#dimension = dimension instanceof Dimension ? dimension : null;
    if (this.Dimension === null) throw new FormatException('DIMASSOC requires an exact source DIMENSION identity: ' + this.#dimensionHandle);
    this.#references.Add(this.Dimension);
    for (const point of this.PointReferences) {
      const geometry = resolve(point.GeometryHandle);
      if (!(geometry instanceof EntityObject)) throw new FormatException('DIMASSOC requires an exact source geometry entity: ' + point.GeometryHandle);
      point.Bind(geometry); this.#references.Add(geometry);
    }
    this.#sourceOwner = this.Owner instanceof DxfDictionary ? this.Owner : null;
    if (!this.#ownedCorrectly()) throw new FormatException('DIMASSOC requires its reciprocal DIMENSION extension dictionary and ACAD_DIMASSOC hard-owner entry.');
    this.#sourceReactors = Array.from(this.PersistentReactors);
    for (const entity of this.#references) if (entity instanceof EntityObject) this.#backlinks.set(entity, entity.PersistentReactors.Contains(this));
    this.#resolved = true;
  }
  ValidateDatabaseSchema(database, errors) {
    if (!this.#resolved) { errors.Add('Stored DIMASSOC must remain in its source document.'); return; }
    if (database == null) throw new NullReferenceException();
    if (database.Document !== this.#source) { errors.Add('Stored DIMASSOC must remain in its source document.'); return; }
    if (this.#source.DrawingVariables.AcadVer !== this.SourceVersion) errors.Add('Stored DIMASSOC version conversion requires complete schema regeneration.');
    for (const target of this.#references) if (this.#source.GetObjectByHandle(target.Handle) !== target)
      errors.Add('A stored DIMASSOC dependency is no longer registered.');
    if (this.Owner !== this.#sourceOwner || !this.#ownedCorrectly()) errors.Add('Stored DIMASSOC dimension ownership changed.');
    const reactors = Array.from(this.PersistentReactors);
    if (reactors.length !== this.#sourceReactors.length || reactors.some((item, i) => !ValueEquals(item, this.#sourceReactors[i])))
      errors.Add('Stored DIMASSOC persistent reactors changed.');
    for (const [entity, backlink] of this.#backlinks) if (entity.PersistentReactors.Contains(this) !== backlink)
      errors.Add('A stored DIMASSOC source reactor backlink changed.');
  }
}
RegisterDatabaseModel('DxfStoredDimAssoc', DxfStoredDimAssoc);
