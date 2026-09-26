// Complete detached model cases from the identically named pinned C# source.
// Typed wire and boundary cases remain unregistered until their typed APIs are ported.
import { Transparency } from '../../index.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Int16 } from '../../runtime/GeometryRuntime.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
const hex = value => (value >>> 0).toString(16).toUpperCase().padStart(8, '0');
export function RegisterTransparencyStoredTests() {
  for (const mode of [0x01000000, 0x02000000]) for (let alpha = 0; alpha < 256; alpha++) {
    const packed = mode | alpha; Run(`transparency/stored/packed-${hex(packed)}`, () => TransparencyStoredSample(packed));
  }
  for (const packed of [0,-1,-2147483648,2147483647,0x0300007F,0x00123456,0xA5123481|0,0x42000000])
    Run(`transparency/stored/unknown-${hex(packed)}`, () => TransparencyStoredSample(packed));
  Run('transparency/stored/authored-defaults', () => {
    Check(new Transparency().StoredAlphaValue === null && new Transparency(0).StoredAlphaValue === null && Transparency.ByBlock.StoredAlphaValue === null && Transparency.ByLayer.StoredAlphaValue === null, 'Authoring materialized packed input');
    Equal(0x01000000, Transparency.ToAlphaValue(Transparency.ByBlock), 'Authored ByBlock encoding');
    Equal(0x020000FF, Transparency.ToAlphaValue(new Transparency(0)), 'Authored opaque encoding');
  });
}
export function TransparencyStoredSample(packed) {
  const item = Transparency.FromAlphaValue(packed);
  Equal(packed, item.StoredAlphaValue, 'Imported packed alpha'); Equal(packed, Transparency.ToAlphaValue(item), 'Exact packed-alpha export');
  const percentage = Int16(100 - ((packed & 255) / 255.0) * 100), legacy = Transparency.FromCadIndex(percentage);
  Equal(legacy.Value, item.Value, 'Legacy effective percentage'); Equal(legacy.IsByBlock, item.IsByBlock, 'Legacy ByBlock interpretation');
  Check(item.Equals(legacy), 'Legacy equality changed');
  const clone = item.Clone(); Equal(packed, clone.StoredAlphaValue, 'Clone packed alpha'); Equal(packed, Transparency.ToAlphaValue(clone), 'Clone exact export');
  Throws(ArgumentOutOfRangeException, () => { item.Value = -1; }); Throws(ArgumentOutOfRangeException, () => { item.Value = 91; });
  Equal(packed, item.StoredAlphaValue, 'Failed edit discarded packed alpha');
  item.Value = 25; Check(item.StoredAlphaValue === null, 'Successful edit retained stale packed alpha');
  Equal(Transparency.ToAlphaValue(new Transparency(25)), Transparency.ToAlphaValue(item), 'Edited value encoding');
  Equal(packed, clone.StoredAlphaValue, 'Edit changed clone packed alpha');
}
