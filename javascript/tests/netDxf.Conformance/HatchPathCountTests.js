// Port of pinned HatchPathCountTests.cs; original valid, malformed, EOF and absent cases.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfTag, HatchType, MemoryStream, Vector2, Vector3 } from '../../index.js';
import { InvalidDataException, IOException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { HatchDoubleTags } from './HatchDoublePatternTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
export function RegisterHatchPathCountTests() {
  for (const version of SupportedVersions) for (const binary of [false, true]) {
    const suffix = `${VersionName(version)}/${BooleanName(binary)}`;
    for (const count of [0, 1, 2, 4]) for (const late of [false, true]) for (const comments of binary ? [false] : [false, true])
      Run(`hatch/path-count/valid/${suffix}/${count}/${BooleanName(late)}/${BooleanName(comments)}`, () => HatchPathCountValid(version, binary, count, late, comments));
    for (let failure = 0; failure < 18; failure++) Run(`hatch/path-count/invalid/${suffix}/${failure}`, () => HatchPathCountInvalid(version, binary, failure));
    for (let cut = 0; cut < 3; cut++) Run(`hatch/path-count/eof/${suffix}/${cut}`, () => HatchPathCountEof(version, binary, cut));
    Run(`hatch/path-count/absent/${suffix}`, () => HatchPathCountAbsent(version, binary));
  }
}
export function HatchPathCountTags(version, count, late = false, comments = false) {
  const tags = HatchDoubleTags(version, HatchType.UserDefined, 0);
  let start = tags.findIndex(tag => tag.Code === 91);
  const end = tags.findIndex(tag => tag.Code === 75), outer = tags.slice(start + 1, end);
  outer[0] = new DxfTag(92, 3);
  const inner = [[92, 0], [93, 1], [72, 2], [10, 5], [20, 5], [40, 1], [50, 0], [51, 360], [73, 1], [97, 0]].map(([code, value]) => new DxfTag(code, value));
  const packet = [new DxfTag(91, count)];
  for (let i = 0; i < count; i++) packet.push(...(i === 0 ? outer : inner));
  if (comments) for (let i = packet.length; i > 0; i--) packet.splice(i, 0, new DxfTag(999, '91 92 93 ENDSEC'));
  tags.splice(start, end - start);
  if (late) start = tags.findLastIndex(tag => tag.Code === 0 && tag.Value === 'ENDSEC');
  tags.splice(start, 0, ...packet);
  const tail = tags.findLastIndex(tag => tag.Code === 0 && tag.Value === 'ENDSEC');
  tags.splice(tail, 0, ...[[0, 'LINE'], [5, '201'], [100, 'AcDbEntity'], [8, '0'], [100, 'AcDbLine'], [10, 20], [20, 30], [30, 40], [11, 21], [21, 31], [31, 41]].map(([code, value]) => new DxfTag(code, value)));
  return tags;
}
export function HatchPathCountValid(version, binary, count, late, comments) {
  const input = new MemoryStream(RawFixtureBytes(HatchPathCountTags(version, count, late, comments), binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Valid boundary packet rejected.');
    for (let cycle = 0; cycle < 3; cycle++) {
      Equal(1, Array.from(doc.Entities.Hatches).length, 'Boundary metadata entity was discarded');
      Check(new Vector3(20, 30, 40).Equals(single(doc.Entities.Lines).StartPoint), 'Following entity consumed by boundary list');
      for (const hatch of doc.Entities.Hatches) {
        Equal(count, hatch.BoundaryPaths.Count, 'Boundary path count');
        if (count !== 0) {
          Equal(3, hatch.BoundaryPaths.get_Item(0).PathType, 'External polyline flag');
          Equal(4, single(hatch.BoundaryPaths.get_Item(0).Edges).Vertexes.length, 'Outer vertices lost');
        }
        for (const boundary of Array.from(hatch.BoundaryPaths).slice(1)) {
          Equal(0, boundary.PathType, 'Flags inherited from preceding path');
          const arc = single(boundary.Edges);
          Check(new Vector2(5, 5).Equals(arc.Center), 'Inner arc center'); Equal(1, arc.Radius, 'Inner arc radius');
        }
        Equal(2.5, hatch.Elevation, 'Boundary parser changed elevation');
        Check(new Vector2(2, 3).Equals(single(hatch.SeedPoints)), 'Boundary parser changed seed');
        Equal('after pattern', single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'Boundary parser changed XData');
        Equal(count, hatch.Clone().BoundaryPaths.Count, 'Boundary clone count');
      }
      if (count === 0) break; // Empty input is retained; export refusal is tested separately.
      const output = new MemoryStream(), format = cycle % 2 === 0 ? !binary : binary;
      try {
        Check(doc.Save(output, format), 'Boundary packet save failed.');
        if (count === 2 && cycle === 1 && !late && !comments) {
          fs.mkdirSync(artifacts, { recursive: true });
          fs.writeFileSync(path.join(artifacts, `hatch-path-count-${VersionName(version)}-${BooleanName(binary)}.dxf`), output.ToArray());
        }
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Boundary packet reload failed.');
      } finally { output.Dispose(); }
    }
    Check(input.CanRead, 'Boundary reader closed caller stream.');
  } finally { input.Dispose(); }
}
export function HatchPathCountInvalid(version, binary, failure) {
  const tags = HatchPathCountTags(version, 2), count = tags.findIndex(tag => tag.Code === 91), first = tags.findIndex(tag => tag.Code === 92);
  const second = tags.findIndex((tag, index) => index > first && tag.Code === 92), tail = tags.findIndex(tag => tag.Code === 75);
  const xdataEnd = tags.findIndex(tag => tag.Code === 0 && tag.Value === 'LINE');
  switch (failure) {
    case 0: tags[count] = new DxfTag(91, -1); break;
    case 1: tags[count] = new DxfTag(91, 0); break;
    case 2: tags[count] = new DxfTag(91, 1); break;
    case 3: tags[count] = new DxfTag(91, 3); break;
    case 4: tags.splice(count, 0, new DxfTag(91, 0)); break;
    case 5: tags.splice(tail, 0, new DxfTag(91, 0)); break;
    case 6: tags.splice(first, 1); break;
    case 7: tags.splice(second, 1); break;
    case 8: tags.splice(first, 0, new DxfTag(92, 0)); break;
    case 9: tags.splice(second + 1, 1); break;
    case 10: tags.splice(count, 1); break;
    case 11: tags.splice(xdataEnd, 0, new DxfTag(92, 0)); break;
    case 12: tags.splice(xdataEnd, 0, new DxfTag(93, 0)); break;
    case 13: tags[count] = new DxfTag(91, 2147483647); break;
    case 14: tags.splice(second, 0, ...[[0, 'LINE'], [5, '202'], [100, 'AcDbEntity'], [8, '0'], [100, 'AcDbLine']].map(([code, value]) => new DxfTag(code, value))); break;
    case 15: tags.splice(second, 0, new DxfTag(0, 'ENDSEC')); break;
    case 16: tags[count] = new DxfTag(91, -2147483648); break;
    case 17: tags.splice(count, 0, new DxfTag(93, 0)); break;
  }
  const input = new MemoryStream(RawFixtureBytes(tags, binary));
  try {
    if (GetTypedIOConfiguration() === 'Debug') {
      let error;
      try { DxfDocument.Load(input); } catch (value) { error = value; }
      Check(error instanceof InvalidDataException, 'Invalid boundary count/framing was accepted or wrong exception.');
      Check(error.message.includes('HATCH') && error.message.includes('group code') && error.message.includes('position'), 'Missing boundary diagnostic context.');
    } else Check(DxfDocument.Load(input) === null, 'Invalid boundary count/framing was accepted.');
    Check(input.CanRead, 'Rejected boundary closed caller stream.');
  } finally { input.Dispose(); }
}
export function HatchPathCountEof(version, binary, cut) {
  const tags = HatchPathCountTags(version, 2), count = tags.findIndex(tag => tag.Code === 91), first = tags.findIndex(tag => tag.Code === 92);
  const second = tags.findIndex((tag, index) => index > first && tag.Code === 92), end = cut === 0 ? count + 1 : cut === 1 ? first + 1 : second;
  const input = new MemoryStream(RawFixtureBytes(tags.slice(0, end), binary));
  try {
    if (GetTypedIOConfiguration() === 'Debug') {
      let error;
      try { DxfDocument.Load(input); } catch (value) { error = value; }
      Check(error instanceof IOException, 'Physical boundary truncation accepted or wrong exception.');
      Check(input.CanRead, 'Truncated boundary closed stream.');
    } else Check(DxfDocument.Load(input) === null, 'Physical boundary truncation accepted.');
  } finally { input.Dispose(); }
}
export function HatchPathCountAbsent(version, binary) {
  const tags = HatchPathCountTags(version, 0).filter(tag => tag.Code !== 91), input = new MemoryStream(RawFixtureBytes(tags, binary));
  try {
    const doc = DxfDocument.Load(input); Check(doc !== null, 'Existing absent-boundary policy changed.');
    Equal(1, Array.from(doc.Entities.Hatches).length, 'Absent boundary discarded HATCH metadata');
    Equal(0, single(doc.Entities.Hatches).BoundaryPaths.Count, 'Absent boundary invented geometry');
    Equal(1, Array.from(doc.Entities.Lines).length, 'Absent boundary consumed following LINE');
  } finally { input.Dispose(); }
}
