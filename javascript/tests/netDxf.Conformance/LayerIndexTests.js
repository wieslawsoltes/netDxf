// Complete detached runtime cases 0-5 from the pinned LayerIndexApi method.
// Registered-document cases 6-9 and typed IO cases are not registered or counted.
import { DxfLayerIndex, DxfLayerIndexEntry, DxfIdBuffer } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
export function RegisterLayerIndexTests() {
  for (let scenario = 0; scenario < 6; scenario++) Run(`layer-index/api/${scenario}`, () => LayerIndexApi(scenario));
}
export function LayerIndexApi(scenario) {
  const index = new DxfLayerIndex(), a = new DxfIdBuffer(), b = new DxfIdBuffer();
  if (scenario === 0) { for (const value of [NaN, Infinity, -Infinity]) Throws(ArgumentOutOfRangeException, () => { index.Timestamp = value; }); return; }
  if (scenario === 1) { for (const name of ['', 'x\0', 'x\n', 'x\r', '\uD800', '\uDC00']) Throws(ArgumentException, () => new DxfLayerIndexEntry(name, a)); return; }
  if (scenario === 2) { Throws(ArgumentNullException, () => index.SetEntries(null)); Throws(ArgumentException, () => index.SetEntries([null])); return; }
  index.SetEntries([new DxfLayerIndexEntry('A', a), new DxfLayerIndexEntry('B', b)]);
  if (scenario === 3) { Throws(ArgumentException, () => index.SetEntries([new DxfLayerIndexEntry('A', a), new DxfLayerIndexEntry('Again', a)])); Equal(2, index.Entries.Count, 'duplicate rejection changed entries'); return; }
  if (scenario === 4) { const other = new DxfLayerIndex(); Throws(ArgumentException, () => other.SetEntries([new DxfLayerIndexEntry('A', a)])); Check(a.Owner === index, 'foreign adoption changed child owner'); return; }
  if (scenario === 5) { index.SetEntries([new DxfLayerIndexEntry('B', b)]); Check(a.Owner === null && b.Owner === index, 'detached replacement did not release removed child'); return; }
  throw new RangeError('Registered-document cases are not ported.');
}
