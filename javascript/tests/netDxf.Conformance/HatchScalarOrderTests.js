// Port of the pinned C# conformance module; original case identities/assertions retained.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Run, Check, Equal, SameDoubleBits, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { DxfDocument, DxfTag, HatchType, HatchBoundaryPath, MemoryStream, Vector2, Vector3 } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { HatchDoubleTags } from './HatchDoublePatternTests.js';
import { HatchEdgeDispatchTags, ExpectEdgePacketInvalid } from './HatchEdgeDispatchTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
const single = items => { const a = Array.from(items); Equal(1, a.length, 'Expected one item'); return a[0]; };
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
export function RegisterHatchScalarOrderTests() {
  for (const v of SupportedVersions) for (const b of [false, true]) for (const k of [1, 2, 3]) {
    const suffix = `${VersionName(v)}/${BooleanName(b)}/${k}`;
    Run(`hatch/scalar-order/permutations/${suffix}`, () => HatchScalarPermutations(v, b, k));
    Run(`hatch/scalar-order/missing-duplicate/${suffix}`, () => HatchScalarInvalid(v, b, k));
    Run(`hatch/scalar-order/next-edge/${suffix}`, () => HatchScalarNextEdge(v, b, k));
  }
  for (const v of SupportedVersions) for (const b of [false, true]) for (const s of [false, true])
    Run(`hatch/scalar-order/header/${VersionName(v)}/${BooleanName(b)}/${BooleanName(s)}`, () => HatchUnorderedHeader(v, b, s));
}
export function HatchScalarTags(version, kind, shift, reverse) {
  const tags = HatchEdgeDispatchTags(version, kind), start = tags.findIndex(t => t.Code === 72) + 1, end = tags.findIndex(t => t.Code === 97), packet = tags.slice(start, end);
  if (kind === 1) { packet[0] = new DxfTag(10, -.125); packet[1] = new DxfTag(20, 2.0000000000000004); }
  if (reverse) packet.reverse();
  tags.splice(start, end - start, ...packet.slice(shift), ...packet.slice(0, shift)); return tags;
}
export function AssertHatchScalar(edge, kind) {
  Equal(kind, edge.Type, 'Scalar edge kind');
  if (edge instanceof HatchBoundaryPath.Line) {
    SameDoubleBits(-.125, edge.Start.X, 'Line start X'); SameDoubleBits(2.0000000000000004, edge.Start.Y, 'Line start Y'); Equal(new Vector2(10, 10), edge.End, 'Line end');
  } else if (edge instanceof HatchBoundaryPath.Arc) {
    Equal(new Vector2(1, 2), edge.Center, 'Arc center'); Equal(3, edge.Radius, 'Arc radius'); Equal(0, edge.StartAngle, 'Arc start'); Equal(360, edge.EndAngle, 'Arc end'); Check(edge.IsCounterclockwise, 'Arc direction');
  } else if (edge instanceof HatchBoundaryPath.Ellipse) {
    Equal(new Vector2(1, 2), edge.Center, 'Ellipse center'); Equal(new Vector2(3, 0), edge.EndMajorAxis, 'Ellipse major vector'); Equal(.5, edge.MinorRatio, 'Ellipse ratio'); Equal(0, edge.StartAngle, 'Ellipse start'); Equal(360, edge.EndAngle, 'Ellipse end'); Check(edge.IsCounterclockwise, 'Ellipse direction');
  } else throw new Error('Unexpected scalar edge.');
}
export function HatchScalarPermutations(version, binary, kind) {
  const count = kind === 1 ? 4 : kind === 2 ? 6 : 8;
  for (const reverse of [false, true]) for (let shift = 0; shift < count; shift++) {
    const tags = HatchScalarTags(version, kind, shift, reverse);
    if (!binary && reverse) { const start = tags.findIndex(t => t.Code === 72), end = tags.findIndex(t => t.Code === 97); for (let i = end; i > start; i--) tags.splice(i, 0, new DxfTag(999, '72 97 0 ENDSEC')); }
    const input = new MemoryStream(RawFixtureBytes(tags, binary));
    try {
      let doc = DxfDocument.Load(input); Check(doc !== null, 'Reordered HATCH scalar input rejected.');
      for (let cycle = 0; cycle < 2; cycle++) {
        const hatch = single(doc.Entities.Hatches);
        AssertHatchScalar(single(single(hatch.BoundaryPaths).Edges), kind); AssertHatchScalar(single(single(hatch.Clone().BoundaryPaths).Edges), kind);
        Equal('after pattern', single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'Following XData'); Equal(new Vector2(2, 3), single(hatch.SeedPoints), 'Seed');
        const output = new MemoryStream(); try {
          Check(doc.Save(output, !binary), 'Scalar edge save failed.');
          if (cycle === 0 && shift === 0 && reverse) { fs.mkdirSync(artifacts, { recursive: true }); fs.writeFileSync(path.join(artifacts, `hatch-scalar-order-${VersionName(version)}-${BooleanName(binary)}-${kind}.dxf`), output.ToArray()); }
          output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Scalar edge reload failed.');
        } finally { output.Dispose(); }
      }
      Check(input.CanRead, 'Scalar reader closed input.');
    } finally { input.Dispose(); }
  }
}
export function HatchScalarInvalid(version, binary, kind) {
  const original = HatchScalarTags(version, kind, 1, true), start = original.findIndex(t => t.Code === 72) + 1, end = original.findIndex(t => t.Code === 97);
  for (let i = start; i < end; i++) {
    const missing = original.slice(); missing.splice(i, 1); ExpectEdgePacketInvalid(missing, binary);
    const duplicate = original.slice(); duplicate.splice(end, 0, original[i]); ExpectEdgePacketInvalid(duplicate, binary);
  }
  const unframed = original.slice(); unframed.splice(end, 0, new DxfTag(75, 0)); ExpectEdgePacketInvalid(unframed, binary);
  if (kind !== 1) { const flag = original.slice(); flag[flag.findIndex((t, i) => i >= start && t.Code === 73)] = new DxfTag(73, 2); ExpectEdgePacketInvalid(flag, binary); }
  const overrun = original.slice(); overrun.splice(end - 1, 1); overrun.splice(end - 1, 0, ...[[0, 'LINE'], [5, 'ABCD'], [100, 'AcDbEntity'], [8, '0'], [100, 'AcDbLine']].map(([c, v]) => new DxfTag(c, v))); ExpectEdgePacketInvalid(overrun, binary);
}
export function HatchScalarNextEdge(version, binary, kind) {
  const tags = HatchScalarTags(version, kind, 1, true), refs = tags.findIndex(t => t.Code === 97);
  tags[tags.findIndex(t => t.Code === 93)] = new DxfTag(93, 2);
  tags.splice(refs, 0, ...[[72, 1], [21, -7], [11, 8], [20, 5], [10, -6]].map(([c, v]) => new DxfTag(c, v)));
  const input = new MemoryStream(RawFixtureBytes(tags, binary)); try {
    const doc = DxfDocument.Load(input); Check(doc !== null, 'Two reordered edges rejected.');
    const edges = Array.from(single(single(doc.Entities.Hatches).BoundaryPaths).Edges); Equal(2, edges.length, 'Counted edges changed'); AssertHatchScalar(edges[0], kind);
    Equal(new Vector2(-6, 5), edges[1].Start, 'Second edge start'); Equal(new Vector2(8, -7), edges[1].End, 'Second edge end');
  } finally { input.Dispose(); }
}
export function HatchUnorderedHeader(version, binary, spline) {
  const source = spline ? HatchEdgeDispatchTags(version, 4) : HatchDoubleTags(version, HatchType.UserDefined, 0);
  const start = spline ? source.findIndex(t => t.Code === 94) : source.findIndex(t => t.Code === 92) + 1, count = spline ? 5 : 3, header = source.slice(start, start + count).reverse();
  for (let shift = 0; shift < count; shift++) {
    const tags = source.slice(); tags.splice(start, count, ...header.slice(shift), ...header.slice(0, shift)); if (!binary) tags.splice(start + 1, 0, new DxfTag(999, '95 96 93 73'));
    const input = new MemoryStream(RawFixtureBytes(tags, binary)); try {
      let doc = DxfDocument.Load(input); Check(doc !== null, 'Reordered list header rejected.');
      for (let cycle = 0; cycle < 2; cycle++) {
        const hatch = single(doc.Entities.Hatches), edge = single(single(hatch.BoundaryPaths).Edges);
        if (spline) { Equal(2, edge.Degree, 'Reordered degree'); Check(!edge.IsRational && !edge.IsPeriodic, 'Reordered spline flags'); Equal([0, 0, 0, 1, 1, 1], edge.Knots, 'Knot list changed'); Equal(new Vector3(5, 10, 1), edge.ControlPoints[1], 'Control list changed'); }
        else { Check(edge.IsClosed && edge.Vertexes.length === 4, 'Polyline header changed'); Equal(new Vector3(10, 10, 0), edge.Vertexes[2], 'Polyline list changed'); }
        Equal(new Vector2(2, 3), single(hatch.SeedPoints), 'Following header seed');
        const output = new MemoryStream(); try { Check(doc.Save(output, !binary), 'Reordered header save failed'); output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Reordered header reload failed'); } finally { output.Dispose(); }
      }
    } finally { input.Dispose(); }
  }
  for (let i = 0; i < count; i++) for (const duplicate of [false, true]) {
    const tags = source.slice(); if (duplicate) tags.splice(start + count, 0, source[start + i]); else tags.splice(start + i, 1);
    const input = new MemoryStream(RawFixtureBytes(tags, binary)); try { if (GetTypedIOConfiguration() === 'Debug') Throws(InvalidDataException, () => DxfDocument.Load(input)); else Check(DxfDocument.Load(input) === null, 'Malformed list header accepted.'); } finally { input.Dispose(); }
  }
}
