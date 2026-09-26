// Port of pinned HatchGradientAciTests.cs: all original wire/Int16 cases.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, MemoryStream, Hatch, HatchGradientPatternType, Block, Insert, Vector2, Vector3 } from '../../index.js';
import { HatchGradientColorStateTags, AssertGradientColorState } from './HatchGradientColorStateTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
export function RegisterHatchGradientAciTests() {
  for (const version of SupportedVersions) for (const binary of [false, true])
    for (const [name, type] of Object.entries(HatchGradientPatternType)) for (const oneColor of [false, true]) for (let presence = 0; presence < 4; presence++)
      Run(`hatch/gradient-aci/wire/${VersionName(version)}/${BooleanName(binary)}/${name}/${BooleanName(oneColor)}/${presence}`,
        () => HatchGradientAciRoundTrip(version, binary, type, oneColor, presence & 1 ? 17 : null, presence & 2 ? 231 : null, presence));
  for (const version of SupportedVersions.filter(v => v >= DxfVersion.AutoCad2004)) for (const binary of [false, true])
    for (const index of [-32768, -1, 0, 1, 255, 256, 32767])
      Run(`hatch/gradient-aci/int16/${VersionName(version)}/${BooleanName(binary)}/${index}`,
        () => HatchGradientAciRoundTrip(version, binary, HatchGradientPatternType.Linear, false, index, index, -1));
}
export function HatchGradientAciTags(version, type, oneColor, index1, index2, reverseComponents) {
  const tags = HatchGradientColorStateTags(version, type, oneColor, .35).filter(tag => tag.Code !== 63);
  if (index1 !== null) tags.splice(tags.findIndex(tag => tag.Code === 463) + (reverseComponents ? 2 : 1), 0, new DxfTag(63, index1));
  if (index2 !== null) tags.splice(tags.findLastIndex(tag => tag.Code === 463) + (reverseComponents ? 2 : 1), 0, new DxfTag(63, index2));
  return tags;
}
export function HatchGradientAciRoundTrip(version, binary, type, oneColor, index1, index2, fixturePresence) {
  const input = new MemoryStream(RawFixtureBytes(HatchGradientAciTags(version, type, oneColor, index1, index2, binary), binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Gradient ACI input rejected.');
    const original = single(doc.Entities.Hatches);
    AssertGradientColorState(original.Pattern, type, oneColor, .35);
    const block = new Block('GradientAci'); block.Entities.Add(original.Clone());
    const insert = new Insert(block, Vector3.Zero).Clone();
    doc.Entities.Add(single(Array.from(insert.Explode()).filter(entity => entity instanceof Hatch)));
    for (let cycle = 0; cycle < 3; cycle++) {
      const output = new MemoryStream();
      try {
        Check(doc.Save(output, cycle % 2 === 0 ? !binary : binary), 'Gradient ACI save failed.');
        output.Position = 0; const raw = DxfRawDocument.Load(output);
        const hatches = Array.from(raw.Sections).flatMap(section => Array.from(section.Records)).filter(record => record.Name === 'HATCH');
        Equal(2, hatches.length, 'ACI original and nested/exploded clone count');
        if (version === DxfVersion.AutoCad2000) {
          Check(hatches.flatMap(hatch => Array.from(hatch.Tags)).every(tag => ![450, 463, 63].includes(tag.Code)), 'AC1015 lossy downgrade changed.');
          break;
        }
        for (const hatch of hatches) AssertGradientAciTags(hatch.Tags, index1, index2);
        if (cycle === 1 && type === HatchGradientPatternType.Linear && oneColor && fixturePresence >= 0) {
          fs.mkdirSync(artifacts, { recursive: true });
          fs.writeFileSync(path.join(artifacts, `hatch-gradient-aci-${VersionName(version)}-${BooleanName(binary)}-${fixturePresence}.dxf`), output.ToArray());
        }
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Gradient ACI reload failed.');
        for (const hatch of doc.Entities.Hatches) {
          AssertGradientColorState(hatch.Pattern, type, oneColor, .35);
          Equal(2.5, hatch.Elevation, 'ACI elevation'); Check(new Vector2(2, 3).Equals(single(hatch.SeedPoints)), 'ACI seed');
          Equal('after pattern', single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'ACI XData');
          Equal(1, hatch.BoundaryPaths.Count, 'ACI boundary count');
        }
        Check(new Vector3(20, 30, 40).Equals(single(doc.Entities.Lines).StartPoint), 'ACI following LINE');
      } finally { output.Dispose(); }
    }
    Check(input.CanRead, 'ACI reader closed caller-owned stream.');
  } finally { input.Dispose(); }
}
export function AssertGradientAciTags(tags, index1, index2) {
  const indices = [], rgb = []; let stop = -1;
  for (const tag of tags) {
    if (tag.Code === 463) { indices.push(null); stop++; }
    else if (tag.Code === 63) {
      Check(stop >= 0 && indices[stop] === null, 'ACI stop is missing or duplicated.'); indices[stop] = tag.Value;
    } else if (tag.Code === 421) rgb.push(tag.Value & 0xffffff);
  }
  Equal([index1, index2], indices, 'Authored optional ACI metadata changed.');
  Equal([0x123456, 0xabcdef], rgb, 'ACI assignment replaced the authoritative RGB stops.');
}
