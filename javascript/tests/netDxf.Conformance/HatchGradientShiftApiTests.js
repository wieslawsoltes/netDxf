// Complete direct model cases only; nested Hatch/Block/Insert cases remain unported.
import { AciColor, HatchGradientPattern, HatchGradientPatternType } from '../../index.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Culture, NumberText } from '../../runtime/GeometryRuntime.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
export function RegisterHatchGradientShiftApiTests() {
  Run('hatch/gradient-shift/api/defaults', () => {
    for (const pattern of [new HatchGradientPattern(), new HatchGradientPattern('description'),
      new HatchGradientPattern(AciColor.Red, 0.4, HatchGradientPatternType.Curved),
      new HatchGradientPattern(AciColor.Red, AciColor.Blue, HatchGradientPatternType.Linear)]) {
      Equal(0, pattern.Shift, 'Default shift'); Check(pattern.Centered, 'Default compatibility flag changed.');
    }
  });
  for (const value of [NaN, Infinity, -Infinity, -Number.MIN_VALUE, -1, 1.0000000000000002, 2])
    Run(`hatch/gradient-shift/api/reject/${NumberText(value, Culture.Invariant)}`, () => {
      const pattern = new HatchGradientPattern(); pattern.Shift = 0.375;
      Throws(ArgumentOutOfRangeException, () => { pattern.Shift = value; });
      Equal(0.375, pattern.Shift, 'Failed setter changed existing value'); Check(!pattern.Centered, 'Failed setter changed compatibility projection.');
    });
}
