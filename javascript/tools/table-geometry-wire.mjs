// Input/observation only. Geometry behavior comes from the production modules.
import * as api from '../index.js';
import { tableStyleSnapshot } from './table-style-wire.mjs';
import { doubleBits } from './wire.mjs';
import { InvalidOperationException } from '../runtime/Errors.js';
const ref = value => value === null ? null : {type:value.constructor.name, handle:value.Handle, owner:value.Owner?.Handle ?? null};
const real = value => ({double:doubleBits(value)});
const point = value => [real(value.X), real(value.Y), real(value.Z)];
export function tableGeometrySnapshot(value) {
  if (value == null) return null;
  if (value instanceof api.DxfStoredTableGeometry) return {kind:'geometry', common:ref(value), version:value.SourceVersion, erased:value.IsErased, rows:value.RowCount, columns:value.ColumnCount, tags:Array.from(value.Payload, tableStyleSnapshot), cells:Array.from(value.Cells, tableGeometrySnapshot), references:Array.from(value.References, ref)};
  if (value instanceof api.DxfStoredTableGeometryCell) return {kind:'cell', flags:value.GeometryDataFlags, width:real(value.WidthWithGap), height:real(value.HeightWithGap), reference:ref(value.GeometryReference), geometry:Array.from(value.Geometry, tableGeometrySnapshot)};
  if (value instanceof api.DxfStoredTableCellGeometry) return {kind:'content', topLeft:point(value.TopLeftDistance), center:point(value.CenterDistance), width:real(value.Width), height:real(value.Height), contentWidth:real(value.ContentWidth), contentHeight:real(value.ContentHeight), value95:value.StoredValue95};
  if (typeof value === 'object' && value[Symbol.iterator]) return Array.from(value, tableGeometrySnapshot);
  return tableStyleSnapshot(value);
}
export function tableGeometryLoad(step, document, read) {
  const tags = step.tags.map(([code, value]) => new api.DxfTag(code, value && typeof value === 'object' && 'handleOf' in value ? '0'.repeat(value.pad ?? 0) + (value.lower ? read({ref:value.handleOf}).Handle.toLowerCase() : read({ref:value.handleOf}).Handle) : read(value)));
  const geometry = new api.DxfStoredTableGeometry(document, tags);
  if (step.register !== false) {
    const parent = step.owner ? read(step.owner) : document.NamedObjects;
    parent.Add(step.name ?? 'GEOMETRY_FIXTURE', geometry);
    if (step.resolve !== false) geometry.Resolve(handle => document.StoredTableHandleTarget(handle));
  }
  return geometry;
}
export function tableGeometryCall(step, target, read, values) {
  const members = step.members === null ? null : step.members.map(read), count = step.repeat ?? members?.length ?? 0, counters = [0, 0, 0, 0, 0];
  if (step.log) values.set(step.log, counters);
  const args = (step.args ?? []).map(read);
  function invoke(source) {
    if (step.action === 'cell') return new api.DxfStoredTableGeometryCell(...args, source);
    target.ReplaceGeometry(step.rows, step.columns, source); return null;
  }
  function hook(stage) {
    for (const action of step.hooks ?? []) {
      if (action.stage !== stage) continue;
      if (action.kind === 'throw') throw new InvalidOperationException('Injected caller failure.');
      if (action.kind === 'reenter' || action.kind === 'catch-reenter') { try { invoke([]); } catch (error) { if (action.kind === 'reenter') throw error; counters[4]++; } }
      else { const subject = read(action.target); if (action.kind === 'set') subject[action.member] = read(action.value); else subject[action.member](...(action.args ?? []).map(read)); }
    }
  }
  const source = members === null ? null : {GetEnumerator() { counters[0]++; hook('get'); if (step.nullEnumerator) return null; let at = -1; return {
    MoveNext() { counters[1]++; hook('move'); return ++at < count; }, get Current() { counters[2]++; hook('current'); return members[at % members.length]; }, Dispose() { counters[3]++; hook('dispose'); }
  }; }};
  return invoke(source);
}
