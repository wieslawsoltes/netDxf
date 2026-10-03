// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { TableGeometryState } from '../../runtime/TableGeometryState.js';
import { TableSnapshot, TableHandleMap } from '../../runtime/TablePayload.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { DxfTag } from '../IO/DxfTag.js';
import { XDataCode } from '../XDataCode.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException } from '../../runtime/Errors.js';
export function GeometryRecordTagCount(owner, payloadCount) {
  let count = payloadCount + 2;
  if (owner.ExtensionDictionary !== null) count += 3;
  const reactors = new Set(Array.from(owner.PersistentReactors).filter(item => item !== null).map(item => item.Handle === null ? null : OrdinalIgnoreCaseKey(item.Handle)));
  if (reactors.size) count += reactors.size + 2;
  for (const data of owner.XData.Values) { count++; for (const record of data.XDataRecord) count += record.Code === XDataCode.BinaryData ? Math.max(1, Math.trunc((record.Value.length + 126) / 127)) : 1; }
  return count;
}
function referenceText(cell) {
  const current = cell.GeometryReference === null ? '0' : cell.GeometryReference.Handle;
  return cell.ReferenceHandle !== null && BigInt('0x' + cell.ReferenceHandle) === BigInt('0x' + current) ? cell.ReferenceHandle : current;
}
const samePoint = (a, b) => Object.is(a.X, b.X) && Object.is(a.Y, b.Y) && Object.is(a.Z, b.Z);
function sameGeometry(owner, rows, columns, cells) {
  if (rows !== owner.RowCount || columns !== owner.ColumnCount || cells.length !== owner.Cells.Count) return false;
  return cells.every((a, i) => {
    const b = owner.Cells.get_Item(i);
    return a.GeometryDataFlags === b.GeometryDataFlags && Object.is(a.WidthWithGap, b.WidthWithGap) && Object.is(a.HeightWithGap, b.HeightWithGap) && a.GeometryReference === b.GeometryReference && a.Geometry.Count === b.Geometry.Count &&
      Array.from(a.Geometry).every((x, j) => { const y = b.Geometry.get_Item(j); return samePoint(x.TopLeftDistance, y.TopLeftDistance) && samePoint(x.CenterDistance, y.CenterDistance) &&
        ['ContentWidth', 'ContentHeight', 'Width', 'Height', 'StoredValue95'].every(key => Object.is(x[key], y[key])); });
  });
}
export function InstallTableGeometryEditing(Type) {
  Type.prototype.ReplaceGeometry = function(rowCount, columnCount, cells) {
    const state = TableGeometryState(this), fail = text => { throw new InvalidOperationException(text); };
    if (state.editing) { state.reentered = true; fail('TABLEGEOMETRY replacement cannot be reentered.'); }
    if (cells == null) throw new ArgumentNullException('cells');
    if (rowCount < 0 || rowCount > Type.MaximumPayloadTags) throw new ArgumentOutOfRangeException('rowCount');
    if (columnCount < 0 || columnCount > Type.MaximumPayloadTags) throw new ArgumentOutOfRangeException('columnCount');
    state.editing = true; state.reentered = false;
    try {
      let tagCount = 4; const replacement = [];
      ConsumeManagedEnumerable(cells, cell => {
        if (cell == null) throw new ArgumentException('A stored geometry cell cannot be null.', 'cells');
        tagCount += 5 + 11 * cell.Geometry.Count;
        if (tagCount > Type.MaximumPayloadTags) throw new ArgumentException('The requested geometry exceeds the record tag limit.', 'cells');
        replacement.push(cell);
      });
      if (state.reentered) fail('TABLEGEOMETRY replacement was reentered during enumeration.');
      if (!state.resolved || this.IsErased || this.Database === null || this.Database.Document !== state.source || state.source.GetObjectByHandle(this.Handle) !== this) fail('TABLEGEOMETRY must remain registered in its source document.');
      const errors = this.Database.Validate();
      if (errors.Count) fail('Cannot replace TABLEGEOMETRY in an invalid source database: ' + Array.from(errors).join('; '));
      if (GeometryRecordTagCount(this, tagCount) > Type.MaximumPayloadTags) throw new ArgumentException('The requested geometry and common metadata exceed the record tag limit.', 'cells');
      const references = new ReferenceList(), handles = new TableHandleMap();
      for (const cell of replacement) {
        const target = cell.GeometryReference; if (target === null) continue;
        if (target.Handle === null || state.source.StoredTableHandleTarget(target.Handle) !== target) throw new ArgumentException('Every geometry reference must be an actual registered identity in the source document.', 'cells');
        references.Add(target); handles.set(referenceText(cell), target);
      }
      if (sameGeometry(this, rowCount, columnCount, replacement)) return;
      const tags = [], add = (code, value) => tags.push(new DxfTag(code, value));
      const point = (code, value) => { add(code, value.X); add(code + 10, value.Y); add(code + 20, value.Z); };
      add(100, 'AcDbTableGeometry'); add(90, rowCount); add(91, columnCount); add(92, replacement.length);
      for (const cell of replacement) {
        add(93, cell.GeometryDataFlags); add(40, cell.WidthWithGap); add(41, cell.HeightWithGap); add(330, referenceText(cell)); add(94, cell.Geometry.Count);
        for (const value of cell.Geometry) { point(10, value.TopLeftDistance); point(11, value.CenterDistance); add(43, value.ContentWidth); add(44, value.ContentHeight); add(45, value.Width); add(46, value.Height); add(95, value.StoredValue95); }
      }
      const payload = TableSnapshot(tags), snapshot = TableSnapshot(replacement);
      state.payload = payload; state.cells = snapshot; state.references = references; state.handles = handles; state.rows = rowCount; state.columns = columnCount;
    } finally { state.editing = false; state.reentered = false; }
  };
}
