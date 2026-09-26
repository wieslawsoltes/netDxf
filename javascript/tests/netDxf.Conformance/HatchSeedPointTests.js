// Port of pinned HatchSeedPointTests.cs; original identities and assertions retained.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, HatchType, MemoryStream, Vector2 } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { HatchDoubleTags } from './HatchDoublePatternTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, SameDoubleBits, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
export const SeedValues = [new Vector2(2.5, 3.75), new Vector2(-1e-20, Number.MIN_VALUE), new Vector2(2.5, 3.75)];

export function RegisterHatchSeedPointTests() {
  for (const version of SupportedVersions) for (const binary of [false, true]) {
    const suffix = `${VersionName(version)}/${BooleanName(binary)}`;
    for (const size of [0, 1, 3]) for (const location of [0, 1, 2])
      Run(`hatch/seeds/wire/${suffix}/${size}/${location}`, () => HatchSeedsWire(version, binary, size, location));
    for (let problem = 0; problem < 8; problem++)
      Run(`hatch/seeds/invalid/${suffix}/${problem}`, () => HatchSeedsInvalid(version, binary, problem));
  }
}

export function HatchSeedTags(version, size, location, comments) {
  const tags = HatchDoubleTags(version, HatchType.UserDefined, 1);
  let start = tags.findIndex(tag => tag.Code === 98);
  tags.splice(start, 3);
  if (location === 1) start = tags.findIndex(tag => tag.Code === 75);
  if (location === 2) start = tags.findLastIndex(tag => tag.Code === 0 && tag.Value === 'ENDSEC');
  const seeds = [new DxfTag(98, size)];
  for (const seed of SeedValues.slice(0, size)) {
    if (comments) seeds.push(new DxfTag(999, '10 ENDSEC'));
    seeds.push(new DxfTag(10, seed.X));
    if (comments) seeds.push(new DxfTag(999, '20 EOF'));
    seeds.push(new DxfTag(20, seed.Y));
  }
  tags.splice(start, 0, ...seeds);
  return tags;
}

export function EmittedHatchSeeds(bytes) {
  const input = new MemoryStream(bytes);
  try {
    const raw = DxfRawDocument.Load(input);
    const hatch = single(Array.from(single(Array.from(raw.Sections).filter(section => section.Name === 'ENTITIES')).Records).filter(record => record.Name === 'HATCH'));
    const tags = Array.from(hatch.Tags).filter(tag => tag.Code !== 999);
    let index = tags.findIndex(tag => tag.Code === 98);
    Check(index >= 0, 'Missing HATCH seed-point count.');
    const count = tags[index].Value, values = [];
    for (let i = 0; i < count; i++) {
      Equal(10, tags[++index].Code, 'Seed X group'); const x = tags[index].Value;
      Equal(20, tags[++index].Code, 'Seed Y group'); const y = tags[index].Value;
      values.push(new Vector2(x, y));
    }
    return values;
  } finally { input.Dispose(); }
}

export function EqualHatchSeeds(expected, actual) {
  const a = Array.from(expected), b = Array.from(actual);
  Equal(a.length, b.length, 'HATCH seed-point count');
  for (let i = 0; i < a.length; i++) {
    SameDoubleBits(a[i].X, b[i].X, 'Seed X bits/order');
    SameDoubleBits(a[i].Y, b[i].Y, 'Seed Y bits/order');
  }
}

export function HatchSeedsWire(version, binary, size, location) {
  const input = new MemoryStream(RawFixtureBytes(HatchSeedTags(version, size, location, !binary), binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Valid HATCH seed fixture failed to load.');
    Check(input.CanRead, 'Seed reader closed caller stream.');
    for (let cycle = 0; cycle < 3; cycle++) {
      const original = single(doc.Entities.Hatches);
      Equal(2.5, original.Elevation, 'Seeds changed HATCH elevation');
      Equal('after pattern', single(original.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'Seeds disrupted XData');
      const clone = original.Clone(), next = new DxfDocument(version); next.Entities.Add(clone);
      const output = new MemoryStream(), transport = cycle === 1 ? binary : !binary;
      try {
        Check(next.Save(output, transport), 'Seed fixture failed to save.');
        EqualHatchSeeds(SeedValues.slice(0, size), EmittedHatchSeeds(output.ToArray()));
        if (cycle === 0 && size === 3 && location === 0) {
          fs.mkdirSync(artifacts, { recursive: true });
          fs.writeFileSync(path.join(artifacts, `hatch-seeds-${VersionName(version)}-${BooleanName(transport)}.dxf`), output.ToArray());
        }
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Seed fixture failed to reload.');
      } finally { output.Dispose(); }
    }
  } finally { input.Dispose(); }
}

export function HatchSeedsInvalid(version, binary, problem) {
  const tags = HatchSeedTags(version, 1, 0, false), start = tags.findIndex(tag => tag.Code === 98);
  switch (problem) {
    case 0: tags[start] = new DxfTag(98, -1); break;
    case 1: tags.splice(start + 1, 2); break;
    case 2: tags[start + 1] = new DxfTag(11, 1); break;
    case 3: tags[start + 2] = new DxfTag(21, 1); break;
    case 4: tags[start] = new DxfTag(98, 2); break;
    case 5: tags.splice(start + 3, 0, new DxfTag(98, 0)); break;
    case 6: tags.splice(start + 2, 1); break;
    default: tags[start] = new DxfTag(98, 2147483647); break;
  }
  const input = new MemoryStream(RawFixtureBytes(tags, binary));
  try {
    if (GetTypedIOConfiguration() === 'Debug') {
      let failure;
      try { DxfDocument.Load(input); } catch (error) { failure = error; }
      Check(failure instanceof InvalidDataException, 'Malformed HATCH seeds were accepted or raised the wrong exception.');
      Check(failure.message.includes('HATCH') && failure.message.includes('seed'), 'Seed diagnostic must identify its entity and field.');
    } else Check(DxfDocument.Load(input) === null, 'Malformed HATCH seeds were accepted.');
    Check(input.CanRead, 'Malformed seeds closed caller stream.');
  } finally { input.Dispose(); }
}
