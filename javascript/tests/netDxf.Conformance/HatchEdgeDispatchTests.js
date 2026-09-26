// Port of the pinned C# conformance module; original case identities/assertions retained.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { DxfDocument, DxfTag, HatchType, HatchBoundaryPath, MemoryStream, Vector2, Vector3, DxfVersion } from '../../index.js';
import { InvalidDataException, EndOfStreamException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { HatchDoubleTags } from './HatchDoublePatternTests.js';
import { EqualHatchSeeds } from './HatchSeedPointTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
const tagsOf = pairs => pairs.map(([code, value]) => new DxfTag(code, value));
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
const fixture = (name, bytes) => { fs.mkdirSync(artifacts, { recursive: true }); fs.writeFileSync(path.join(artifacts, name), bytes); };
export function RegisterHatchEdgeDispatchTests() {
  for (const v of SupportedVersions) for (const b of [false, true]) {
    const suffix = `${VersionName(v)}/${BooleanName(b)}`;
    for (let kind = 1; kind <= 4; kind++) {
      Run(`hatch/edge-dispatch/valid/${suffix}/${kind}`, () => HatchEdgeDispatchValid(v, b, kind, false));
      if (!b) Run(`hatch/edge-dispatch/comments/${VersionName(v)}/${kind}`, () => HatchEdgeDispatchValid(v, false, kind, true));
    }
    for (let failure = 0; failure < 12; failure++) Run(`hatch/edge-dispatch/invalid/${suffix}/${failure}`, () => HatchEdgeDispatchInvalid(v, b, failure));
    for (let failure = 0; failure < 24; failure++) Run(`hatch/edge-packets/invalid/${suffix}/${failure}`, () => HatchEdgePacketInvalid(v, b, failure));
    // 48 original allocation cases remain unregistered. Functional count refusal
    // is not a replacement for GC.GetAllocatedBytesForCurrentThread assertions.
    Run(`hatch/edge-packets/sparse-weights/${suffix}`, () => HatchSplineWeights(v, b));
    if (v >= DxfVersion.AutoCad2010) for (let failure = 0; failure < 7; failure++)
      Run(`hatch/edge-packets/fit-grammar/${suffix}/${failure}`, () => HatchSplineFitGrammar(v, b, failure));
    Run(`hatch/edge-dispatch/physical-eof/${suffix}`, () => HatchEdgeDispatchEof(v, b));
  }
}
export function HatchEdgeDispatchTags(version, kind, comments = false) {
  const tags = HatchDoubleTags(version, HatchType.UserDefined, 0);
  const start = tags.findIndex(t => t.Code === 92), end = tags.findIndex(t => t.Code === 97);
  tags.splice(start, end - start);
  const edge = tagsOf([[92, 0], [93, 1], [72, kind]]);
  if (kind === 1) edge.push(...tagsOf([[10, 0], [20, 0], [11, 10], [21, 10]]));
  else if (kind === 2) edge.push(...tagsOf([[10, 1], [20, 2], [40, 3], [50, 0], [51, 360], [73, 1]]));
  else if (kind === 3) edge.push(...tagsOf([[10, 1], [20, 2], [11, 3], [21, 0], [40, .5], [50, 0], [51, 360], [73, 1]]));
  else if (kind === 4) {
    edge.push(...tagsOf([[94, 2], [73, 0], [74, 0], [95, 6], [96, 3],
      [40, 0], [40, 0], [40, 0], [40, 1], [40, 1], [40, 1],
      [10, 0], [20, 0], [10, 5], [20, 10], [10, 10], [20, 0]]));
    if (version >= DxfVersion.AutoCad2010) edge.push(new DxfTag(97, 0));
  }
  tags.splice(start, 0, ...edge);
  if (comments) for (let i = tags.findIndex(t => t.Code === 75); i > start + 1; i--) tags.splice(i, 0, new DxfTag(999, '72 97 ENDSEC'));
  return tags;
}
export function HatchEdgeDispatchValid(version, binary, kind, comments) {
  const input = new MemoryStream(RawFixtureBytes(HatchEdgeDispatchTags(version, kind, comments), binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Supported edge kind rejected.');
    for (let cycle = 0; cycle < 2; cycle++) {
      const hatch = single(doc.Entities.Hatches), edge = single(single(hatch.BoundaryPaths).Edges);
      Equal(kind, edge.Type, 'Edge kind changed');
      if (edge instanceof HatchBoundaryPath.Line) Equal(new Vector2(10, 10), edge.End, 'Line endpoint');
      else if (edge instanceof HatchBoundaryPath.Arc) Equal(3, edge.Radius, 'Arc radius');
      else if (edge instanceof HatchBoundaryPath.Ellipse) Equal(.5, edge.MinorRatio, 'Ellipse ratio');
      else if (edge instanceof HatchBoundaryPath.Spline) { Equal([0, 0, 0, 1, 1, 1], edge.Knots, 'Spline knots changed.'); Equal(new Vector3(5, 10, 1), edge.ControlPoints[1], 'Spline control data'); }
      EqualHatchSeeds([new Vector2(2, 3)], hatch.SeedPoints);
      Equal('after pattern', single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'Following XData changed');
      const output = new MemoryStream();
      try {
        Check(doc.Save(output, !binary), 'Supported edge save failed.');
        if (cycle === 0 && !comments) fixture(`hatch-edge-dispatch-${VersionName(version)}-${BooleanName(binary)}-${kind}.dxf`, output.ToArray());
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Supported edge reload failed.');
      } finally { output.Dispose(); }
    }
    Check(input.CanRead, 'Edge parser closed caller stream.');
  } finally { input.Dispose(); }
}
function expectInvalid(tags, binary, context) {
  const input = new MemoryStream(RawFixtureBytes(tags, binary));
  try {
    if (GetTypedIOConfiguration() === 'Debug') {
      let failure; try { DxfDocument.Load(input); } catch (error) { failure = error; }
      Check(failure instanceof InvalidDataException, `Invalid ${context} input was accepted or wrong exception type.`);
      Check(failure.message.includes('HATCH edge boundary') && failure.message.includes('group code') && failure.message.includes('position'), `Missing ${context} diagnostic context.`);
    } else Check(DxfDocument.Load(input) === null, `Invalid ${context} input was accepted.`);
    Check(input.CanRead, `Invalid ${context} closed caller stream.`);
  } finally { input.Dispose(); }
}
export function ExpectEdgePacketInvalid(tags, binary) { expectInvalid(tags, binary, 'edge-packet'); }
export function HatchEdgeDispatchInvalid(version, binary, failure) {
  const tags = HatchEdgeDispatchTags(version, 1), count = tags.findIndex(t => t.Code === 93), type = count + 1, refs = tags.findIndex(t => t.Code === 97);
  switch (failure) {
    case 0: tags[type] = new DxfTag(72, -1); break;
    case 1: tags[type] = new DxfTag(72, 0); break;
    case 2: tags[type] = new DxfTag(72, 5); break;
    case 3: tags[type] = new DxfTag(72, 32767); break;
    case 4: tags[type] = new DxfTag(74, 1); break;
    case 5: tags[count] = new DxfTag(93, -1); break;
    case 6: tags[refs] = new DxfTag(96, 0); break;
    case 7: tags[refs] = new DxfTag(97, -1); break;
    case 8: tags[refs] = new DxfTag(97, 1); break;
    case 9: tags[refs] = new DxfTag(97, 1); tags.splice(refs + 1, 0, new DxfTag(340, '201')); break;
    case 10: tags.splice(refs + 1, 0, new DxfTag(330, '201')); break;
    case 11: tags[count] = new DxfTag(93, 2); tags.splice(refs, 0, new DxfTag(72, 5)); break;
  }
  expectInvalid(tags, binary, 'edge-dispatch');
}
export function HatchEdgePacketInvalid(version, binary, failure) {
  const kind = failure < 3 ? failure + 1 : 4;
  let tags = HatchEdgeDispatchTags(version, kind);
  const start = tags.findIndex(t => t.Code === 72) + 1, find = code => tags.findIndex((t, i) => i >= start && t.Code === code);
  if (failure < 3) { const scalar = find(20); tags[scalar] = new DxfTag(21, tags[scalar].Value); }
  else switch (failure) {
    case 3: tags[find(94)] = new DxfTag(94, 0); break;
    case 4: tags[find(94)] = new DxfTag(94, -1); break;
    case 5: tags[find(94)] = new DxfTag(94, 32768); break;
    case 6: tags[find(94)] = new DxfTag(94, 2147483647); break;
    case 7: tags[find(94)] = new DxfTag(90, 2); break;
    case 8: tags[find(73)] = new DxfTag(73, 2); break;
    case 9: tags[find(74)] = new DxfTag(74, -1); break;
    case 10: tags[find(95)] = new DxfTag(95, -1); break;
    case 11: tags[find(96)] = new DxfTag(96, -1); break;
    case 12: tags[find(95)] = new DxfTag(95, 5); break;
    case 13: tags[find(95)] = new DxfTag(95, 7); break;
    case 14: tags[find(96)] = new DxfTag(96, 2); break;
    case 15: tags[find(96)] = new DxfTag(96, 4); break;
    case 16: tags[find(40)] = new DxfTag(41, 0); break;
    case 17: tags[find(10)] = new DxfTag(11, 0); break;
    case 18: tags[find(20)] = new DxfTag(21, 0); break;
    case 19: tags.splice(find(20) + 1, 0, ...tagsOf([[42, 1], [42, 2]])); break;
    case 20: tags.splice(find(20), 1); break;
    case 21: tags.splice(find(96), 1); break;
    case 22: case 23:
      tags = HatchEdgeDispatchTags(version, failure === 22 ? 2 : 3); tags[tags.findIndex(t => t.Code === 73)] = new DxfTag(73, 2); break;
  }
  ExpectEdgePacketInvalid(tags, binary);
}
export function HatchSplineWeights(version, binary) {
  const tags = HatchEdgeDispatchTags(version, 4), start = tags.findIndex(t => t.Code === 94);
  tags[tags.findIndex((t, i) => i >= start && t.Code === 73)] = new DxfTag(73, 1);
  const first = tags.findIndex((t, i) => i >= start && t.Code === 20); tags.splice(first + 1, 0, new DxfTag(42, .5));
  const last = tags.findIndex((t, i) => i >= first + 2 && t.Code === 97) - 1; tags.splice(last + 1, 0, new DxfTag(42, 2));
  const input = new MemoryStream(RawFixtureBytes(tags, binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Sparse spline weights rejected.');
    const expected = [new Vector3(0, 0, .5), new Vector3(5, 10, 1), new Vector3(10, 0, 2)];
    for (let i = 0; i < 2; i++) {
      const spline = single(single(single(doc.Entities.Hatches).BoundaryPaths).Edges);
      Check(spline.IsRational, 'Rational flag changed.'); Equal(expected, spline.ControlPoints, 'Spline weight defaults changed.');
      const output = new MemoryStream(); try { Check(doc.Save(output, !binary), 'Weighted spline output failed.'); output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Weighted spline reload failed.'); } finally { output.Dispose(); }
    }
  } finally { input.Dispose(); }
}
export function HatchSplineFitGrammar(version, binary, failure) {
  const tags = HatchEdgeDispatchTags(version, 4), fit = tags.findIndex(t => t.Code === 97);
  switch (failure) {
    case 0: tags[fit] = new DxfTag(97, -1); break;
    case 1: tags[fit] = new DxfTag(97, 1); break;
    case 2: tags[fit] = new DxfTag(97, 1); tags.splice(fit + 1, 0, ...tagsOf([[11, 0], [22, 0]])); break;
    case 3: tags.splice(fit + 1, 0, ...tagsOf([[12, 1], [23, 0]])); break;
    case 4: tags.splice(fit + 1, 0, new DxfTag(13, 1)); break;
    case 5: tags[fit] = new DxfTag(98, 0); break;
    default: {
      tags[fit] = new DxfTag(97, 1); tags.splice(fit + 1, 0, ...tagsOf([[11, 0], [21, 0], [12, 1], [22, 0], [13, 1], [23, 0]]));
      const input = new MemoryStream(RawFixtureBytes(tags, binary));
      try { const doc = DxfDocument.Load(input); Check(doc !== null, 'Valid fit/tangent packet rejected.'); const hatch = single(doc.Entities.Hatches); Equal(3, single(single(hatch.BoundaryPaths).Edges).ControlPoints.length, 'Fit data disrupted spline control data'); EqualHatchSeeds([new Vector2(2, 3)], hatch.SeedPoints); } finally { input.Dispose(); }
      return;
    }
  }
  ExpectEdgePacketInvalid(tags, binary);
}
export function HatchEdgeDispatchEof(version, binary) {
  const tags = HatchEdgeDispatchTags(version, 1), count = tags.findIndex(t => t.Code === 93), input = new MemoryStream(RawFixtureBytes(tags.slice(0, count + 1), binary));
  try { if (GetTypedIOConfiguration() === 'Debug') Throws(EndOfStreamException, () => DxfDocument.Load(input)); else Check(DxfDocument.Load(input) === null, 'Truncated edge declaration accepted.'); Check(input.CanRead, 'Truncated edge closed caller stream.'); } finally { input.Dispose(); }
}
