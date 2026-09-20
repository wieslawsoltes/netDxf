// Complete detached original case; fixture/transport/INSERT cases remain unregistered.
import { Hatch, HatchPattern, Vector2 } from '../../index.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SameDoubleBits } from './TestHarness.js';
export function RegisterHatchSeedPointApiTests() { Run('hatch/seeds/api-validation', HatchSeedApiValidation); }
function EqualHatchSeeds(expected, actual) {
  const values = Array.from(actual);
  Equal(expected.length, values.length, 'Seed count');
  for (let i = 0; i < values.length; i++) {
    SameDoubleBits(expected[i].X, values[i].X, 'Seed X');
    SameDoubleBits(expected[i].Y, values[i].Y, 'Seed Y');
  }
}
export function HatchSeedApiValidation() {
  const hatch = new Hatch(HatchPattern.Line, false);
  EqualHatchSeeds([Vector2.Zero], hatch.SeedPoints);
  hatch.SeedPoints.Clear(); hatch.SeedPoints.Add(new Vector2(1, 2));
  for (const bad of [NaN, Infinity, -Infinity]) for (const point of [new Vector2(bad, 0), new Vector2(0, bad)]) {
    Throws(ArgumentOutOfRangeException, () => hatch.SeedPoints.Add(point));
    Throws(ArgumentOutOfRangeException, () => hatch.SeedPoints.Insert(0, point));
    Throws(ArgumentOutOfRangeException, () => hatch.SeedPoints.set_Item(0, point));
    // The non-generic IList.Add entry maps to the same checked JavaScript collection method.
    Throws(ArgumentOutOfRangeException, () => hatch.SeedPoints.Add(point));
    EqualHatchSeeds([new Vector2(1, 2)], hatch.SeedPoints);
  }
  hatch.SeedPoints.Add(new Vector2(3, 4)); hatch.SeedPoints.Insert(1, new Vector2(5, 6));
  hatch.SeedPoints.set_Item(0, new Vector2(7, 8)); hatch.SeedPoints.RemoveAt(2);
  EqualHatchSeeds([new Vector2(7, 8), new Vector2(5, 6)], hatch.SeedPoints);
  const clone = hatch.Clone();
  Check(hatch.SeedPoints !== clone.SeedPoints, 'Seed clone aliases collection storage.');
  clone.SeedPoints.Clear(); Equal(2, hatch.SeedPoints.Count, 'Clearing clone changed source');
  Check(clone.Owner === null && clone.Handle === null, 'Clone copied database identity.');
}
