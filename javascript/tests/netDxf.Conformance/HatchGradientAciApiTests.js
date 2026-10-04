// Direct model cases from the identically named pinned C# source. Typed IO cases remain unported.
import { AciColor, HatchGradientPattern, HatchGradientPatternType } from '../../index.js';
import { Run, Check, Equal } from './TestHarness.js';
import { GradientRgb } from './HatchGradientColorStateTests.js';
const Linear = HatchGradientPatternType.Linear;
export function RegisterHatchGradientAciApiTests() {
  for (let c = 0; c < 6; c++) Run(`hatch/gradient-aci/api-default/${c}`, () => HatchGradientAciDefault(c));
  for (let a = 0; a < 3; a++) for (let b = 0; b < 3; b++) Run(`hatch/gradient-aci/api-clone/${a}/${b}`, () => HatchGradientAciClone(a, b));
  for (const value of [-32768, -1, 0, 1, 255, 256, 32767]) Run(`hatch/gradient-aci/api-int16/${value}`, () => HatchGradientAciApiInt16(value));
  Run('hatch/gradient-aci/api-color-mode-independence', HatchGradientAciModeIndependence);
}
export function HatchGradientAciDefault(constructor) {
  const pattern = [() => new HatchGradientPattern(), () => new HatchGradientPattern('metadata'),
    () => new HatchGradientPattern(AciColor.Red, 0.35, Linear), () => new HatchGradientPattern(AciColor.Red, 0.35, Linear, 'metadata'),
    () => new HatchGradientPattern(AciColor.Red, AciColor.Blue, Linear), () => new HatchGradientPattern(AciColor.Red, AciColor.Blue, Linear, 'metadata')][constructor]();
  Check(pattern.IsColor1AciIndexAutomatic && pattern.IsColor2AciIndexAutomatic, 'Constructor changed automatic ACI defaults.');
  Equal(pattern.Color1.Index, pattern.Color1AciIndex, 'Automatic first index');
  Equal(pattern.Color2.Index, pattern.Color2AciIndex, 'Automatic second index');
  pattern.Color1 = AciColor.Green; pattern.Color2 = AciColor.Yellow;
  Equal(pattern.Color1.Index, pattern.Color1AciIndex, 'Automatic index after color replacement');
  pattern.Color2.Index = 42; Equal(42, pattern.Color2AciIndex, 'Automatic index after mutable color edit');
  Check(pattern.IsColor2AciIndexAutomatic, 'Reading automatic index mutated its mode.');
}
export function HatchGradientAciClone(first, second) {
  const pattern = new HatchGradientPattern(AciColor.FromTrueColor(0x123456), AciColor.FromTrueColor(0xABCDEF), Linear);
  if (first !== 0) pattern.Color1AciIndex = first === 1 ? 17 : null;
  if (second !== 0) pattern.Color2AciIndex = second === 1 ? 231 : null;
  const copy = pattern.Clone();
  Equal(pattern.Color1AciIndex, copy.Color1AciIndex, 'Cloned first metadata'); Equal(pattern.Color2AciIndex, copy.Color2AciIndex, 'Cloned second metadata');
  Equal(first === 0, copy.IsColor1AciIndexAutomatic, 'Clone froze automatic first index'); Equal(second === 0, copy.IsColor2AciIndexAutomatic, 'Clone froze automatic second index');
  copy.Color1.Index = 41; copy.Color2.Index = 42;
  Equal(first === 0 ? 41 : first === 1 ? 17 : null, copy.Color1AciIndex, 'Cloned first mode after mutation');
  Equal(second === 0 ? 42 : second === 1 ? 231 : null, copy.Color2AciIndex, 'Cloned second mode after mutation');
  Equal(0x123456, GradientRgb(pattern.Color1), 'Clone aliased source first RGB'); Equal(0xABCDEF, GradientRgb(pattern.Color2), 'Clone aliased source second RGB');
  copy.ResetColor1AciIndex(); copy.ResetColor2AciIndex();
  Check(copy.IsColor1AciIndexAutomatic && copy.IsColor2AciIndexAutomatic, 'Reset did not restore automatic modes.');
  Equal(41, copy.Color1AciIndex, 'Reset first index'); Equal(42, copy.Color2AciIndex, 'Reset second index');
  Equal(first === 0, pattern.IsColor1AciIndexAutomatic, 'Clone reset changed source mode');
}
export function HatchGradientAciApiInt16(value) {
  const pattern = new HatchGradientPattern(); pattern.Color1AciIndex = value; pattern.Color2AciIndex = value;
  Equal(value, pattern.Color1AciIndex, 'Exact first Int16 metadata'); Equal(value, pattern.Color2AciIndex, 'Exact second Int16 metadata');
  const copy = pattern.Clone(); Equal(pattern.Color1AciIndex, copy.Color1AciIndex, 'Clone retained Int16 value');
  pattern.Color1AciIndex = null; pattern.Color2AciIndex = null;
  Check(pattern.Color1AciIndex === null && pattern.Color2AciIndex === null, 'Null must retain explicit absence.');
  Check(!pattern.IsColor1AciIndexAutomatic && !pattern.IsColor2AciIndexAutomatic, 'Null selected automatic rather than absent.');
}
export function HatchGradientAciModeIndependence() {
  const pattern = new HatchGradientPattern(AciColor.Red, 0.35, Linear); pattern.Color1AciIndex = 17; pattern.Color2AciIndex = null;
  const rgb = GradientRgb(pattern.Color2); pattern.Color2AciIndex = 231;
  Check(pattern.SingleColor, 'ACI edit incorrectly selected two-color mode.'); Equal(rgb, GradientRgb(pattern.Color2), 'ACI edit recolored RGB stop');
  pattern.Tint = 0.75; Equal(231, pattern.Color2AciIndex, 'Tint edit overwrote explicit ACI metadata');
  pattern.Color2AciIndex = null; pattern.SingleColor = true; Check(pattern.Color2AciIndex === null, 'Single-color derivation fabricated absent ACI metadata.');
  pattern.Color1 = AciColor.Green; Equal(17, pattern.Color1AciIndex, 'RGB replacement overwrote explicit first metadata');
  pattern.ResetColor2AciIndex(); pattern.Tint = 0.25; Equal(pattern.Color2.Index, pattern.Color2AciIndex, 'Automatic ACI failed to follow tint-derived RGB');
}
