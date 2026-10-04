// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { Vector3 } from '../Vector3.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceView, RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { TableSnapshot, TableHandleMap, IsTableReference, ValidTableUtf16 } from '../../runtime/TablePayload.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { SetTableGeometryState, TableGeometryState } from '../../runtime/TableGeometryState.js';
import { FormatException, NotSupportedException, ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { InstallTableGeometryEditing, GeometryRecordTagCount } from './DxfStoredTableGeometry.Edit.js';
const maximum = 1048576;
export function CheckGeometryFinite(value, parameter) {
  if (value instanceof Vector3) { CheckGeometryFinite(value.X, parameter); CheckGeometryFinite(value.Y, parameter); CheckGeometryFinite(value.Z, parameter); }
  else if (!Number.isFinite(value)) throw new ArgumentOutOfRangeException(parameter, undefined, 'Stored geometry must be finite.');
}
/** Immutable scalar packet. Vector properties return CLR-value-semantic copies. */
export class DxfStoredTableCellGeometry {
  #topLeft; #center;
  constructor(topLeft, center, contentWidth, contentHeight, width, height, value95) {
    for (const [name, value] of Object.entries({topLeft, center, contentWidth, contentHeight, width, height})) CheckGeometryFinite(value, name);
    this.#topLeft = Copy(topLeft); this.#center = Copy(center);
    Object.assign(this, {ContentWidth:contentWidth, ContentHeight:contentHeight, Width:width, Height:height, StoredValue95:value95});
    Object.freeze(this);
  }
  get TopLeftDistance() { return Copy(this.#topLeft); }
  get CenterDistance() { return Copy(this.#center); }
}
const loaded = Symbol('stored-cell');
export class DxfStoredTableGeometryCell {
  #reference = null; #handle = null;
  constructor(flags, width, height, reference, geometry, token) {
    if (token !== loaded) {
      CheckGeometryFinite(width, 'width'); CheckGeometryFinite(height, 'height');
      if (geometry == null) throw new ArgumentNullException('geometry');
    }
    const values = [];
    if (token === loaded) { for (const value of geometry) values.push(value); this.#handle = reference; }
    else {
      ConsumeManagedEnumerable(geometry, value => {
        if (value == null) throw new ArgumentException('A content-geometry packet cannot be null.', 'geometry');
        if (values.length === Math.trunc(maximum / 11)) throw new ArgumentException('Content geometry exceeds the TABLEGEOMETRY storage limit.', 'geometry');
        values.push(value);
      });
      this.#reference = reference;
    }
    Object.assign(this, {GeometryDataFlags:flags, WidthWithGap:width, HeightWithGap:height, Geometry:TableSnapshot(values)});
    Object.freeze(this);
  }
  get GeometryReference() { return this.#reference; }
  get ReferenceHandle() { return this.#handle; } // Internal retained-loader adapter.
  SetReference(value) { this.#reference = value; }
  static FromStored(flags, width, height, handle, geometry) { return new this(flags, width, height, handle, geometry, loaded); }
}
/** Source-bound retained packet. Construction/Resolve adapt internal reader APIs,
 * not public TABLE creation or complete typed DXF reader admission. */
export class DxfStoredTableGeometry extends DxfDatabaseObject {
  static get MaximumPayloadTags() { return maximum; }
  constructor(source, tags) {
    super('TABLEGEOMETRY'); tags = Array.from(tags); let index = 0;
    const read = code => { if (index >= tags.length || tags[index].Code !== code) throw new FormatException('TABLEGEOMETRY requires ordered group ' + code + '.'); return tags[index++].Value; };
    const count = code => { const value = read(code); if (value < 0 || value > maximum) throw new FormatException('TABLEGEOMETRY count exceeds its storage limit.'); return value; };
    const number = code => { const value = read(code); if (!Number.isFinite(value)) throw new FormatException('TABLEGEOMETRY geometry must be finite.'); return value; };
    const point = code => new Vector3(number(code), number(code + 10), number(code + 20));
    if (read(100) !== 'AcDbTableGeometry') throw new FormatException('TABLEGEOMETRY requires its public subclass.');
    const rows = count(90), columns = count(91), length = count(92), cells = [];
    if (length > Math.trunc((tags.length - index) / 5)) throw new FormatException('TABLEGEOMETRY cell count exceeds its stored payload.');
    for (let cell = 0; cell < length; cell++) {
      const flags = read(93), width = number(40), height = number(41), handle = read(330), size = count(94), geometry = [];
      if (size > Math.trunc((tags.length - index) / 11)) throw new FormatException('TABLEGEOMETRY content count exceeds its stored payload.');
      for (let item = 0; item < size; item++) geometry.push(new DxfStoredTableCellGeometry(point(10), point(11), number(43), number(44), number(45), number(46), read(95)));
      cells.push(DxfStoredTableGeometryCell.FromStored(flags, width, height, handle, geometry));
    }
    if (index !== tags.length) throw new FormatException('TABLEGEOMETRY contains unexpected data after its counted cells.');
    SetTableGeometryState(this, {source, version:source.DrawingVariables.AcadVer, payload:TableSnapshot(tags), rows, columns, cells:TableSnapshot(cells), references:new ReferenceList(), handles:new TableHandleMap(), sourceOwner:null, resolved:false, editing:false, reentered:false});
  }
  get SourceVersion() { return TableGeometryState(this).version; }
  get Payload() { return TableGeometryState(this).payload; }
  get RowCount() { return TableGeometryState(this).rows; }
  get ColumnCount() { return TableGeometryState(this).columns; }
  get Cells() { return TableGeometryState(this).cells; }
  get References() { return ReadOnlyReferenceView(TableGeometryState(this).references); }
  get DatabaseReferences() { const state = TableGeometryState(this); return Array.from(state.references).filter(item => state.source.GetObjectByHandle(item.Handle) === item); }
  get AllocationReservations() { return this.Payload; }
  CloneShell() { throw new NotSupportedException('Stored TABLEGEOMETRY cloning requires its complete application schema.'); }
  Resolve(resolve) {
    const state = TableGeometryState(this);
    for (const tag of this.Payload) {
      if (!IsTableReference(tag) || BigInt('0x' + tag.Value) === 0n) continue;
      const target = resolve(tag.Value);
      if (target == null) throw new FormatException('TABLEGEOMETRY requires an exact source reference identity: ' + tag.Value);
      state.handles.set(tag.Value, target); state.references.Add(target);
    }
    for (const cell of this.Cells) if (BigInt('0x' + cell.ReferenceHandle) !== 0n) cell.SetReference(state.handles.get(cell.ReferenceHandle));
    state.sourceOwner = this.Owner;
    if (state.sourceOwner === null || state.source.GetObjectByHandle(state.sourceOwner.Handle) !== state.sourceOwner) throw new FormatException('TABLEGEOMETRY requires a registered source owner.');
    const ancestry = new Set();
    for (let ancestor = state.sourceOwner; ancestor !== null && ancestor !== state.source; ancestor = ancestor.Owner) {
      if (ancestor === this || ancestry.has(ancestor)) throw new FormatException('TABLEGEOMETRY source ownership contains a cycle.'); ancestry.add(ancestor);
      if (state.source.GetObjectByHandle(ancestor.Handle) !== ancestor) throw new FormatException('TABLEGEOMETRY source ancestry contains an unregistered object.');
    }
    state.resolved = true;
  }
  ValidateDatabaseSchema(database, errors) {
    const state = TableGeometryState(this);
    if (!state.resolved || database.Document !== state.source) { errors.Add('Stored TABLEGEOMETRY must remain in its source document.'); return; }
    if (state.source.DrawingVariables.AcadVer !== this.SourceVersion) errors.Add('Stored TABLEGEOMETRY conversion requires complete schema regeneration.');
    if (GeometryRecordTagCount(this, this.Payload.Count) > maximum) errors.Add('Stored TABLEGEOMETRY exceeds its record tag limit.');
    if (this.Owner !== state.sourceOwner || state.source.GetObjectByHandle(state.sourceOwner.Handle) !== state.sourceOwner) errors.Add('Stored TABLEGEOMETRY source ownership changed.');
    for (const [handle, target] of state.handles) if (state.source.StoredTableHandleTarget(handle) !== target) errors.Add('A stored TABLEGEOMETRY dependency is no longer registered: ' + handle);
    for (const tag of this.Payload) if (typeof tag.Value === 'string' && !ValidTableUtf16(tag.Value)) errors.Add('Stored TABLEGEOMETRY contains invalid UTF-16 text.');
  }
}
InstallTableGeometryEditing(DxfStoredTableGeometry);
RegisterDatabaseModel('DxfStoredTableGeometry', DxfStoredTableGeometry);
