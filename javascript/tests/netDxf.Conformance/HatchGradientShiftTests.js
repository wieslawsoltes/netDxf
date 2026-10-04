// Port of pinned HatchGradientShiftTests.cs, including exact endpoint-neighbour bits.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, MemoryStream, Hatch, HatchGradientPatternType, Block, Insert, Vector3 } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { NumberText, Culture } from '../../runtime/GeometryRuntime.js';
import { HatchGradientAngleTags } from './HatchGradientAngleTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, Near, SameDoubleBits, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
export function RegisterHatchGradientShiftTests() {
  for (const version of SupportedVersions.filter(v => v >= DxfVersion.AutoCad2004)) for (const binary of [false, true]) {
    const suffix = `${VersionName(version)}/${BooleanName(binary)}`;
    for (const [name, type] of Object.entries(HatchGradientPatternType)) for (const shift of [0, .125, .5, .875, 1])
      Run(`hatch/gradient-shift/roundtrip/${suffix}/${name}/${shift}`, () => HatchGradientShiftRoundTrip(version, binary, type, shift));
    for (const shift of [Number.MIN_VALUE, 0.9999999999999999])
      Run(`hatch/gradient-shift/precision/${suffix}/${NumberText(shift, Culture.Invariant)}`, () => HatchGradientShiftRoundTrip(version, binary, HatchGradientPatternType.Linear, shift));
    for (const shift of [-.125, 1.125, -Number.MIN_VALUE, 1.0000000000000002])
      Run(`hatch/gradient-shift/invalid/${suffix}/${NumberText(shift, Culture.Invariant)}`, () => HatchGradientShiftInvalid(version, binary, shift));
  }
  for (const binary of [false, true]) Run(`hatch/gradient-shift/legacy-loss/${BooleanName(binary)}`, () => HatchGradientShiftLegacy(binary));
}
export function HatchGradientShiftTags(version, type, shift) {
  const tags = HatchGradientAngleTags(version, type, 0);
  tags[tags.findIndex(tag => tag.Code === 461)] = new DxfTag(461, shift);
  return tags;
}
export function HatchGradientShiftRoundTrip(version, binary, type, shift) {
  const input = new MemoryStream(RawFixtureBytes(HatchGradientShiftTags(version, type, shift), binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Gradient shift input rejected.');
    const original = single(doc.Entities.Hatches), gradient = original.Pattern;
    Equal(shift === 0, gradient.Centered, 'Centered projects exact zero, not an integer cast');
    const block = new Block('ShiftBlock'); block.Entities.Add(original.Clone());
    const insert = new Insert(block, new Vector3(10, 20, 0)).Clone();
    doc.Entities.Add(single(Array.from(insert.Explode()).filter(entity => entity instanceof Hatch)));
    for (let cycle = 0; cycle < 3; cycle++) {
      const output = new MemoryStream();
      try {
        const format = cycle % 2 === 0 ? !binary : binary;
        Check(doc.Save(output, format), 'Gradient shift save failed.');
        output.Position = 0; const raw = DxfRawDocument.Load(output);
        const records = Array.from(raw.Sections).flatMap(section => Array.from(section.Records)).filter(record => record.Name === 'HATCH');
        Equal(2, records.length, 'Original plus cloned/exploded HATCH count');
        for (const record of records) {
          const tags = Array.from(record.Tags);
          SameDoubleBits(shift, single(tags.filter(tag => tag.Code === 461)).Value, 'Exact gradient shift bits');
          Equal(1, single(tags.filter(tag => tag.Code === 450)).Value, 'Gradient marker');
          Equal(0, single(tags.filter(tag => tag.Code === 452)).Value, 'Two-color mode');
          Near(0, single(tags.filter(tag => tag.Code === 460)).Value, 'Unchanged angle');
        }
        if (cycle === 1 && type === HatchGradientPatternType.Linear && shift === .5) {
          fs.mkdirSync(artifacts, { recursive: true });
          fs.writeFileSync(path.join(artifacts, `hatch-gradient-shift-${VersionName(version)}-${BooleanName(binary)}.dxf`), output.ToArray());
        }
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Gradient shift reload failed.');
        for (const hatch of doc.Entities.Hatches) {
          Equal(type, hatch.Pattern.GradientType, 'Gradient type unchanged'); Equal(shift === 0, hatch.Pattern.Centered, 'Repeated centered projection');
          Equal(2.5, hatch.Elevation, 'Elevation unchanged');
          Equal('after pattern', single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'Following XData unchanged');
        }
        Check(new Vector3(20, 30, 40).Equals(single(doc.Entities.Lines).StartPoint), 'Following LINE unchanged');
      } finally { output.Dispose(); }
    }
    Check(input.CanRead, 'Gradient reader closed caller input.');
  } finally { input.Dispose(); }
}
export function HatchGradientShiftInvalid(version, binary, shift) {
  const input = new MemoryStream(RawFixtureBytes(HatchGradientShiftTags(version, HatchGradientPatternType.Linear, shift), binary));
  try {
    if (GetTypedIOConfiguration() === 'Debug') {
      let error;
      try { DxfDocument.Load(input); } catch (failure) { error = failure; }
      Check(error instanceof InvalidDataException, 'Invalid gradient shift accepted or wrong exception.');
      Check(error.message.includes('HATCH gradient shift') && error.message.includes('461') && error.message.includes('position'), 'Missing gradient-shift field context.');
    } else Check(DxfDocument.Load(input) === null, 'Invalid gradient shift accepted.');
    Check(input.CanRead, 'Invalid shift closed caller input.');
  } finally { input.Dispose(); }
}
export function HatchGradientShiftLegacy(binary) {
  const input = new MemoryStream(RawFixtureBytes(HatchGradientShiftTags(DxfVersion.AutoCad2000, HatchGradientPatternType.Linear, .5), binary));
  const output = new MemoryStream();
  try {
    const doc = DxfDocument.Load(input); Check(doc !== null, 'Existing permissive legacy import changed.');
    Check(!single(doc.Entities.Hatches).Pattern.Centered, 'Legacy import quantized fractional shift.');
    Check(doc.Save(output, !binary), 'Existing legacy export changed.'); output.Position = 0;
    const raw = DxfRawDocument.Load(output);
    Check(Array.from(raw.Sections).flatMap(section => Array.from(section.Records)).filter(record => record.Name === 'HATCH')
      .flatMap(record => Array.from(record.Tags)).every(tag => tag.Code !== 450 && tag.Code !== 461), 'AC1015 unexpectedly emitted gradients.');
  } finally { input.Dispose(); output.Dispose(); }
}
