// Complete detached original case; document/block/INSERT transport cases remain unregistered.
import { Hatch, HatchPattern } from '../../index.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
export function RegisterHatchPixelSizeApiTests() { Run('hatch/pixel-size/model', HatchPixelModel); }
export function HatchPixelModel() {
  const hatch = new Hatch(HatchPattern.Line, false);
  Equal(0, hatch.PixelSize, 'New hatch compatibility default');
  hatch.PixelSize = .125;
  for (const bad of [-1, NaN, Infinity, -Infinity]) {
    Throws(ArgumentOutOfRangeException, () => { hatch.PixelSize = bad; });
    Equal(.125, hatch.PixelSize, 'Rejected pixel edit changed state');
  }
  const clone = hatch.Clone();
  Equal(hatch.PixelSize, clone.PixelSize, 'Clone lost sampling hint');
  clone.PixelSize = null;
  Equal(.125, hatch.PixelSize, 'Clone edit affected original');
  hatch.PixelSize = null;
  Check(hatch.Clone().PixelSize === null, 'Clone invented an absent field.');
}
