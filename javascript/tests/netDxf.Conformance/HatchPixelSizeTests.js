// Port of pinned HatchPixelSizeTests.cs; bitwise optional values and failures.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, HatchType, MemoryStream, Vector2 } from '../../index.js';
import { InvalidDataException, EndOfStreamException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { HatchDoubleTags } from './HatchDoublePatternTests.js';
import { EqualHatchSeeds } from './HatchSeedPointTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, SameDoubleBits, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
export function RegisterHatchPixelSizeTests() {
  for (const version of SupportedVersions) for (const binary of [false, true]) {
    const suffix = `${VersionName(version)}/${BooleanName(binary)}`;
    for (let location = 0; location < 3; location++) Run(`hatch/pixel-size/wire/${suffix}/${location}`, () => HatchPixelWire(version, binary, location));
    for (let failure = 0; failure < 3; failure++) Run(`hatch/pixel-size/invalid/${suffix}/${failure}`, () => HatchPixelInvalid(version, binary, failure));
  }
}
export function HatchPixelTags(version, pixel, location) {
  const tags = HatchDoubleTags(version, HatchType.UserDefined, 1);
  if (pixel !== null) {
    const at = location === 0 ? tags.findIndex(tag => tag.Code === 75) : location === 1 ? tags.findIndex(tag => tag.Code === 98) : tags.findLastIndex(tag => tag.Code === 0 && tag.Value === 'ENDSEC');
    tags.splice(at, 0, new DxfTag(47, pixel));
  }
  return tags;
}
export function HatchPixelWire(version, binary, location) {
  for (const expected of [null, 0, -0, .125, 1e-20, Number.MIN_VALUE, Number.MAX_VALUE]) {
    const tags = HatchPixelTags(version, expected, location);
    if (!binary && expected !== null) tags.splice(tags.findIndex(tag => tag.Code === 47), 0, new DxfTag(999, '47 ENDSEC'));
    const input = new MemoryStream(RawFixtureBytes(tags, binary));
    try {
      let doc = DxfDocument.Load(input); Check(doc !== null, 'Pixel-size fixture failed to load.');
      const original = single(doc.Entities.Hatches);
      Equal(2.5, original.Elevation, 'Pixel size changed elevation'); Equal(1, original.BoundaryPaths.Count, 'Pixel size changed boundaries');
      EqualHatchSeeds([new Vector2(2, 3)], original.SeedPoints);
      Equal('after pattern', single(original.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'Following XData changed');
      doc.Entities.Add(original.Clone());
      for (let cycle = 0; cycle < 2; cycle++) {
        const output = new MemoryStream();
        try {
          Check(doc.Save(output, cycle === 0 ? !binary : binary), 'Pixel-size fixture failed to save.');
          output.Position = 0; const raw = DxfRawDocument.Load(output);
          const hatches = Array.from(raw.Sections).flatMap(section => Array.from(section.Records)).filter(record => record.Name === 'HATCH');
          Equal(2, hatches.length, 'Pixel clone count');
          for (const hatch of hatches) {
            const values = Array.from(hatch.Tags).filter(tag => tag.Code === 47);
            Equal(expected !== null ? 1 : 0, values.length, 'Pixel-size optional-field presence');
            if (expected !== null) SameDoubleBits(expected, values[0].Value, 'Pixel-size bits changed');
          }
          if (cycle === 0 && location === 1 && expected === .125) {
            fs.mkdirSync(artifacts, { recursive: true });
            fs.writeFileSync(path.join(artifacts, `hatch-pixel-${VersionName(version)}-${BooleanName(!binary)}.dxf`), output.ToArray());
          }
          output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Pixel-size reload failed.');
          Check(output.CanRead, 'Pixel round trip closed caller stream.');
        } finally { output.Dispose(); }
      }
      Check(input.CanRead, 'Pixel input closed caller stream.');
    } finally { input.Dispose(); }
  }
}
export function HatchPixelInvalid(version, binary, failure) {
  const tags = HatchPixelTags(version, failure === 0 ? -.125 : .125, 1);
  if (failure === 1) tags.splice(tags.findIndex(tag => tag.Code === 75), 0, new DxfTag(47, .25));
  let bytes = RawFixtureBytes(failure === 2 ? tags.slice(0, tags.findIndex(tag => tag.Code === 47)) : tags, binary);
  if (failure === 2) bytes = Uint8Array.from([...bytes, ...(binary ? [47, 0] : new TextEncoder().encode('47\n'))]);
  const input = new MemoryStream(bytes);
  try {
    if (GetTypedIOConfiguration() === 'Debug') {
      if (failure === 2) Throws(EndOfStreamException, () => DxfDocument.Load(input));
      else {
        let error;
        try { DxfDocument.Load(input); } catch (value) { error = value; }
        Check(error instanceof InvalidDataException, 'Invalid pixel-size data was accepted or raised the wrong exception.');
        Check(error.message.includes('47'), 'Pixel diagnostic lost group 47.');
      }
    } else Check(DxfDocument.Load(input) === null, 'Invalid pixel-size data was accepted.');
    Check(input.CanRead, 'Invalid pixel data closed input.');
  } finally { input.Dispose(); }
}
