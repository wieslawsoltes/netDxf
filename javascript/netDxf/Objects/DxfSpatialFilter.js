// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix4 } from '../Matrix4.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ImmutableCellView, IsDatabaseModel } from '../../runtime/DatabaseModel.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
const finite = value => { if (!Number.isFinite(value)) throw new ArgumentOutOfRangeException('value'); };
const checkVector = value => { finite(value.X); finite(value.Y); finite(value.Z); };
function checkMatrix(value) {
  for (let row = 1; row <= 4; row++) for (let col = 1; col <= 4; col++) finite(value[`M${row}${col}`]);
  if (value.M41 !== 0 || value.M42 !== 0 || value.M43 !== 0 || value.M44 !== 1)
    throw new ArgumentException('Spatial filter matrices must be affine (last row 0,0,0,1).', 'value');
}
export class DxfSpatialFilter extends DxfDatabaseObject {
  #boundary = [new Vector2(-1, -1), new Vector2(1, 1)]; #normal = Vector3.UnitZ; #origin = Vector3.Zero;
  #front = null; #back = null; #inverse = Matrix4.Identity; #transform = Matrix4.Identity;
  IsClippingEnabled = true;
  constructor() { super('SPATIAL_FILTER'); }
  static Finite(value) { finite(value); }
  get Boundary() { return ImmutableCellView(this.#boundary); }
  SetBoundary(vertices) {
    if (vertices == null) throw new ArgumentNullException('vertices');
    const copy = [];
    for (const vertex of vertices) {
      finite(vertex.X); finite(vertex.Y);
      if (copy.length === 32767) throw new ArgumentException('A spatial boundary cannot exceed 32767 vertices.', 'vertices');
      copy.push(Copy(vertex));
    }
    if (copy.length < 2) throw new ArgumentException('A spatial boundary needs at least two vertices.', 'vertices');
    this.#boundary = copy;
  }
  get Normal() { return Copy(this.#normal); }
  set Normal(value) { checkVector(value); if (value.X === 0 && value.Y === 0 && value.Z === 0) throw new ArgumentException('The spatial normal cannot be zero.', 'value'); this.#normal = Copy(value); }
  get Origin() { return Copy(this.#origin); } set Origin(value) { checkVector(value); this.#origin = Copy(value); }
  get FrontClippingDistance() { return this.#front; } set FrontClippingDistance(value) { if (value !== null) finite(value); this.#front = value; }
  get BackClippingDistance() { return this.#back; } set BackClippingDistance(value) { if (value !== null) finite(value); this.#back = value; }
  get InverseInsertTransform() { return Copy(this.#inverse); } set InverseInsertTransform(value) { checkMatrix(value); this.#inverse = Copy(value); }
  get ClipBoundaryTransform() { return Copy(this.#transform); } set ClipBoundaryTransform(value) { checkMatrix(value); this.#transform = Copy(value); }
  CloneShell() {
    const copy = new DxfSpatialFilter();
    for (const key of ['Normal','Origin','IsClippingEnabled','FrontClippingDistance','BackClippingDistance','InverseInsertTransform','ClipBoundaryTransform']) copy[key] = this[key];
    copy.SetBoundary(this.#boundary); return copy;
  }
  ValidateDatabaseSchema(database, errors) {
    const filters = this.Owner instanceof DxfDictionary ? this.Owner : null;
    const extension = filters?.Owner instanceof DxfDictionary ? filters.Owner : null;
    const insert = IsDatabaseModel(extension?.Owner, 'Insert') ? extension.Owner : null;
    if (insert == null || (extension.Database != null && insert.ExtensionDictionary !== extension) ||
        ![...extension.Entries].some(e => OrdinalIgnoreCaseEquals(e.Name, 'ACAD_FILTER') && e.Target === filters) ||
        ![...filters.Entries].some(e => OrdinalIgnoreCaseEquals(e.Name, 'SPATIAL') && e.Target === this))
      errors.Add('SPATIAL_FILTER must occupy ACAD_FILTER/SPATIAL in an INSERT extension dictionary.');
  }
}
