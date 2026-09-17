// The complete detached model case from the pinned C# file. Typed IO remains unported.
import { HatchPattern, HatchGradientPattern } from '../../index.js';
import { Run, Check, Equal } from './TestHarness.js';
export function RegisterHatchDoublePatternTests() { Run('hatch/double/model-defaults-and-clones', HatchDoubleDefaults); }
export function HatchDoubleDefaults() {
  for (const pattern of [new HatchPattern('U'), HatchPattern.Line, HatchPattern.Net, HatchPattern.Solid, new HatchGradientPattern()]) {
    Check(!pattern.IsDouble, 'A new pattern invented a double flag.'); pattern.IsDouble = true;
    const copy = pattern.Clone(); Check(copy.IsDouble, 'Pattern clone lost the double flag.');
    Equal(pattern.constructor, copy.constructor, 'Pattern clone changed subtype'); copy.IsDouble = false;
    Check(pattern.IsDouble, 'Clone edit changed source double flag.');
  }
}
