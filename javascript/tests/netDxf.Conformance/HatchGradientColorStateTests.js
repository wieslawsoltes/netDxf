// Complete API test bodies from the pinned C# source; typed wire/legacy cases remain unported.
import { AciColor, Vector2, HatchGradientPattern, HatchGradientPatternType, HatchPatternLineDefinition } from '../../index.js';
import { ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Culture, NumberText } from '../../runtime/GeometryRuntime.js';
import { Run, Check, Equal, Throws, BooleanName } from './TestHarness.js';
const Linear = HatchGradientPatternType.Linear;
export const GradientRgb = color => (color.R << 16) | (color.G << 8) | color.B;
export function RegisterHatchGradientColorStateTests() {
  for (const single of [false, true]) {
    for (const tint of [0, 0.25, 0.75, 1]) Run(`hatch/gradient-state/api-edit/${BooleanName(single)}/${tint}`, () => HatchGradientColorStateEdit(single, tint));
    for (const tint of [NaN, Infinity, -Infinity, -Number.MIN_VALUE, 1.0000000000000002])
      Run(`hatch/gradient-state/api-reject/${BooleanName(single)}/${NumberText(tint, Culture.Invariant)}`, () => HatchGradientColorStateReject(single, tint));
  }
  Run('hatch/gradient-state/api-null-color-atomic', HatchGradientColorNullAtomic);
  Run('hatch/gradient-state/api-clone-metadata', HatchGradientColorCloneMetadata);
}
export function HatchGradientColorStateEdit(single, tint) {
  const pattern = new HatchGradientPattern(AciColor.FromTrueColor(0x123456), 0.4, Linear); pattern.SingleColor = single;
  const previousRgb = GradientRgb(pattern.Color2); pattern.Tint = tint;
  Equal(single, pattern.SingleColor, 'Tint setter must not clear single-color mode'); Equal(tint, pattern.Tint, 'Tint edit');
  const hsl = AciColor.ToHsl(pattern.Color1);
  Equal(single ? GradientRgb(AciColor.FromHsl(hsl.X, hsl.Y, tint)) : previousRgb, GradientRgb(pattern.Color2), 'Tint derives second color only in single-color mode');
  const copy = pattern.Clone(); Equal(single, copy.SingleColor, 'Clone mode'); Equal(tint, copy.Tint, 'Clone tint');
  Equal(GradientRgb(pattern.Color2), GradientRgb(copy.Color2), 'Clone current RGB');
  pattern.SingleColor = true; Check(pattern.SingleColor, 'Enabling mode failed.');
  Equal(GradientRgb(AciColor.FromHsl(hsl.X, hsl.Y, tint)), GradientRgb(pattern.Color2), 'Explicit mode switch derives tint');
}
export function HatchGradientColorStateReject(single, tint) {
  const pattern = new HatchGradientPattern(AciColor.Red, 0.4, Linear); pattern.SingleColor = single;
  const before = pattern.Color2; Throws(ArgumentOutOfRangeException, () => { pattern.Tint = tint; });
  Equal(0.4, pattern.Tint, 'Rejected tint mutated state'); Equal(single, pattern.SingleColor, 'Rejected tint mutated mode');
  Check(before === pattern.Color2, 'Rejected tint replaced color.');
  Throws(ArgumentOutOfRangeException, () => new HatchGradientPattern(AciColor.Red, tint, Linear));
}
export function HatchGradientColorNullAtomic() {
  const pattern = new HatchGradientPattern(AciColor.Red, 0.4, Linear), before = pattern.Color2;
  Throws(ArgumentNullException, () => { pattern.Color2 = null; });
  Check(pattern.SingleColor && before === pattern.Color2, 'Rejected null Color2 cleared single-color state.');
  const first = pattern.Color1; Throws(ArgumentNullException, () => { pattern.Color1 = null; });
  Check(first === pattern.Color1 && pattern.SingleColor, 'Rejected null Color1 changed state.');
}
export function HatchGradientColorCloneMetadata() {
  const pattern = new HatchGradientPattern(AciColor.Red, 0.25, HatchGradientPatternType.Spherical, 'Authored description');
  Object.assign(pattern, { Origin: new Vector2(4, 5), Angle: 71, Shift: 0.625, IsDouble: true, Scale: 2 });
  const line = new HatchPatternLineDefinition(); line.Angle = 23; line.Delta = new Vector2(1, 2); pattern.LineDefinitions.Add(line);
  const copy = pattern.Clone(); Equal(pattern.Description, copy.Description, 'Description clone'); Equal(1, copy.LineDefinitions.Count, 'Dormant line definition clone');
  Check(pattern.LineDefinitions.get_Item(0) !== copy.LineDefinitions.get_Item(0), 'Line definitions aliased.');
  copy.LineDefinitions.get_Item(0).Angle = 90; Equal(23, pattern.LineDefinitions.get_Item(0).Angle, 'Cloned line edit affected source.');
  Equal(pattern.Origin, copy.Origin, 'Origin clone'); Equal(pattern.Angle, copy.Angle, 'Angle clone');
  Equal(pattern.Shift, copy.Shift, 'Shift clone'); Equal(pattern.Scale, copy.Scale, 'Scale clone');
  Check(copy.SingleColor && copy.IsDouble, 'Clone flags changed.');
}

// Shared original wire helpers. Their use does not count the unregistered wire cases.
import { DxfTag } from '../../index.js';
import { HatchGradientAngleTags } from './HatchGradientAngleTests.js';
import { Near, SameDoubleBits } from './TestHarness.js';
export function HatchGradientColorStateTags(version,type,single,tint){
  const tags=HatchGradientAngleTags(version,type,37);
  for(const [code,value]of [[452,single?1:0],[462,tint],[461,.375]])tags[tags.findIndex(t=>t.Code===code)]=new DxfTag(code,value);
  return tags;
}
export function AssertGradientColorState(pattern,type,single,tint){
  Check(pattern instanceof HatchGradientPattern,'Expected gradient pattern');
  Equal(type,pattern.GradientType,'Gradient kind');Equal(single,pattern.SingleColor,'Authored dialog mode');
  SameDoubleBits(tint,pattern.Tint,'Exact authored tint');Equal(0x123456,GradientRgb(pattern.Color1),'Authored first RGB stop');
  Equal(0xABCDEF,GradientRgb(pattern.Color2),'Authored second RGB stop must not be recomputed');
  Near(37,pattern.Angle,'Retained angle');Equal(.375,pattern.Shift,'Retained continuous shift');
}
