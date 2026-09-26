// Complete original constructor/value cases. Original typed IO and independent
// fixture round trips are not shortened, waived, or counted as ported here.
import { DxfStoredTableCellGeometry, DxfStoredTableGeometryCell, Vector3 } from '../../index.js';
import { ArgumentOutOfRangeException, ArgumentNullException, ArgumentException, NotSupportedException } from '../../runtime/Errors.js';
import { Run, Throws, Equal } from './TestHarness.js';
export function RegisterEditableTableGeometryTests() {
  for (let scalar = 0; scalar < 12; scalar++) Run('table-geometry-edit/nonfinite-value/' + scalar, () => EditableGeometryNonfinite(scalar));
  Run('table-geometry-edit/value-snapshots', EditableGeometryValueSnapshots);
}
export function EditableContent(offset = 0) { return new DxfStoredTableCellGeometry(new Vector3(-1-offset,2,3),new Vector3(4,5,6+offset),-7,8+offset,9,-10,-2147483648); }
export function EditableGeometryNonfinite(scalar) {
  const values = Array(12).fill(1); values[scalar] = scalar % 2 === 0 ? NaN : Infinity;
  Throws(ArgumentOutOfRangeException, () => {
    const content = new DxfStoredTableCellGeometry(new Vector3(...values.slice(0,3)),new Vector3(...values.slice(3,6)),...values.slice(6,10),0);
    new DxfStoredTableGeometryCell(0,values[10],values[11],null,[content]);
  });
}
export function EditableGeometryValueSnapshots() {
  const values = [EditableContent()], cell = new DxfStoredTableGeometryCell(0,1,2,null,values);
  values.length = 0; Equal(1,cell.Geometry.Count,'constructor retained caller list');
  Throws(NotSupportedException, () => cell.Geometry.Clear());
  Throws(ArgumentNullException, () => new DxfStoredTableGeometryCell(0,1,2,null,null));
  Throws(ArgumentException, () => new DxfStoredTableGeometryCell(0,1,2,null,[null]));
}
