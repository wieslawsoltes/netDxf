// Supplemental reader regressions; not counted as original C# case identities.
import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfTag, DxfVersion, DxfRawDocument, HatchType, MemoryStream } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { DocumentTagReader } from '../../runtime/TypedDocumentIO.js';
import { EndOfStreamException, IOException, InvalidDataException, FormatException } from '../../runtime/Errors.js';
import { HatchDoubleTags } from '../netDxf.Conformance/HatchDoublePatternTests.js';
import { RawFixtureBytes } from '../netDxf.Conformance/RawDocumentTests.js';
const tagsOf = pairs => pairs.map(([code, value]) => new DxfTag(code, value));
const load = (tags, binary) => {
  const stream = new MemoryStream(RawFixtureBytes(tags, binary));
  try { return new DxfReader().Read(stream); } finally { stream.Dispose(); }
};
const replaceBoundary = packet => {
  const tags = HatchDoubleTags(DxfVersion.AutoCad2018, HatchType.UserDefined, 0);
  const start = tags.findIndex(tag => tag.Code === 91), end = tags.findIndex(tag => tag.Code === 75);
  tags.splice(start, end - start, ...tagsOf(packet)); return tags;
};
const splinePacket = () => [
  [91, 1], [92, 0], [93, 1], [72, 4], [94, 1], [73, 0], [74, 0], [95, 4], [96, 2],
  [40, 0], [40, 0], [40, 1], [40, 1], [10, 0], [20, 0], [10, 2], [20, 3],
  [97, 0], [13, 7], [23, 8], [12, 5], [22, 6], [97, 0]
];
for (const binary of [false, true]) {
  test(`HATCH resumes semantic parsing after each XData group (${binary})`, () => {
    const tags = HatchDoubleTags(DxfVersion.AutoCad2018, HatchType.UserDefined, 0);
    tags.splice(tags.findIndex(tag => tag.Code === 75), 0, ...tagsOf([[1001, 'EARLY'], [1000, 'first']]));
    tags.splice(tags.findIndex(tag => tag.Code === 0 && tag.Value === 'ENDSEC' && tags.indexOf(tag) > 10), 0,
      ...tagsOf([[47, .5], [1001, 'AFTER'], [1000, 'last'], [30, 19], [71, 1], [71, 0]]));
    const doc = load(tags, binary), hatch = Array.from(doc.Entities.Hatches)[0];
    assert.equal(hatch.Elevation, 19); assert.equal(hatch.PixelSize, .5); assert.equal(hatch.Associative, true);
    assert.equal(hatch.XData.get_Item('EARLY').XDataRecord.get_Item(0).Value, 'first');
    assert.equal(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord.get_Item(0).Value, 'after pattern');
    assert.equal(hatch.XData.get_Item('AFTER').XDataRecord.get_Item(0).Value, 'last');
  });
  test(`HATCH spline accepts end tangent before start tangent (${binary})`, () => {
    const doc = load(replaceBoundary(splinePacket()), binary), hatch = Array.from(doc.Entities.Hatches)[0];
    const spline = hatch.BoundaryPaths.get_Item(0).Edges.get_Item(0);
    assert.deepEqual([spline.StartTangent.X, spline.StartTangent.Y], [5, 6]);
    assert.deepEqual([spline.EndTangent.X, spline.EndTangent.Y], [7, 8]);
  });
  for (const component of [12, 13]) test(`HATCH duplicate spline tangent ${component} is rejected (${binary})`, () => {
    const packet = splinePacket(); packet.splice(packet.length - 1, 0, [component, 1], [component + 10, 2]);
    assert.throws(() => load(replaceBoundary(packet), binary), error => error instanceof InvalidDataException && /duplicate spline .* tangent/.test(error.message));
  });
  for (const code of [10, 20, 42, 72, 73, 93, 97, 330]) test(`HATCH excess polyline group ${code} is not silently discarded (${binary})`, () => {
    const tags = HatchDoubleTags(DxfVersion.AutoCad2018, HatchType.UserDefined, 0);
    tags.splice(tags.findIndex(tag => tag.Code === 75), 0, new DxfTag(code, code === 330 ? '200' : 0));
    assert.throws(() => load(tags, binary), error => error instanceof InvalidDataException && error.message.includes('HATCH polyline boundary'));
  });
}

test('HATCH counted seed input is not capped at 65,536 entries', () => {
  const count = 65537, tags = [new DxfTag(98, count)];
  for (let i = 0; i < count; i++) tags.push(new DxfTag(10, i), new DxfTag(20, -i));
  tags.push(new DxfTag(0, 'EOF'));
  const reader = new DxfReader(); reader.chunk = new DocumentTagReader(tags, 0);
  const seeds = reader.ReadHatchSeedPoints();
  assert.equal(seeds.length, count); assert.equal(seeds.at(-1).X, 65536); assert.equal(seeds.at(-1).Y, -65536);
  assert.equal(reader.chunk.Code, 0);
});

test('HATCH impossible seed counts fail on missing input without allocating the advertised capacity', () => {
  const reader = new DxfReader(); reader.chunk = new DocumentTagReader(tagsOf([[98, 2147483647], [0, 'LINE']]), 0);
  assert.throws(() => reader.ReadHatchSeedPoints(), error => error instanceof InvalidDataException && error.message.includes('seed X'));
  assert.equal(reader.chunk.ReadString(), 'LINE');
});

test('HATCH impossible boundary count stops at the next record', () => {
  const reader = new DxfReader(); reader.chunk = new DocumentTagReader(tagsOf([[91, 2147483647], [0, 'LINE']]), 0);
  assert.throws(() => reader.ReadHatchBoundaryPaths(2147483647), error => error instanceof InvalidDataException && error.message.includes('group code 92'));
  assert.equal(reader.chunk.ReadString(), 'LINE');
});

test('EndOfStreamException preserves the IOException catch contract', () => {
  const error = new EndOfStreamException('truncated input');
  assert.ok(error instanceof IOException); assert.equal(error.name, 'EndOfStreamException'); assert.equal(error.message, 'truncated input');
  const reader = new DocumentTagReader([new DxfTag(0, 'EOF')], 0);
  assert.throws(() => reader.Next(), IOException);
});

for (const binary of [false, true]) {
  test(`typed HATCH error precedes subsequent section framing failure (${binary})`, () => {
    const tags = replaceBoundary([[91, 1], [0, 'ENDSEC'], [92, 0], [93, 0], [97, 0]]);
    const bytes = RawFixtureBytes(tags, binary), reader = new DxfReader();
    assert.throws(() => DxfRawDocument.Load(bytes), FormatException);
    assert.throws(() => DxfRawDocument.Create(tags, binary), FormatException);
    const stream = new MemoryStream(bytes);
    try {
      assert.throws(() => reader.Read(stream), error => error instanceof InvalidDataException && error.message.includes('HATCH') && error.message.includes('group code'));
      assert.equal(reader.RawDocument, null); assert.equal(stream.CanRead, true);
    } finally { stream.Dispose(); }
  });

  test(`typed parsing never swallows a deferred framing error (${binary})`, () => {
    const tags = HatchDoubleTags(DxfVersion.AutoCad2018, HatchType.UserDefined, 0);
    tags.splice(tags.length - 1, 0, new DxfTag(1, 'outside any section'));
    const bytes = RawFixtureBytes(tags, binary), stream = new MemoryStream(bytes), reader = new DxfReader();
    try {
      assert.throws(() => reader.Read(stream), FormatException);
      assert.throws(() => DxfRawDocument.Load(bytes), FormatException);
      assert.equal(reader.RawDocument, null); assert.equal(stream.CanRead, true);
    } finally { stream.Dispose(); }
  });

  test(`valid typed input retains its strict raw preservation snapshot (${binary})`, () => {
    const bytes = RawFixtureBytes(HatchDoubleTags(DxfVersion.AutoCad2018, HatchType.UserDefined, 0), binary);
    const input = new MemoryStream(bytes), output = new MemoryStream(), reader = new DxfReader();
    try {
      reader.Read(input); assert.ok(reader.RawDocument instanceof DxfRawDocument);
      assert.equal(reader.RawDocument.HasOriginalBytes, true); reader.RawDocument.Save(output);
      assert.deepEqual(output.ToArray(), bytes);
    } finally { input.Dispose(); output.Dispose(); }
  });
}

// Source-guided writer checks: arithmetic grouping and synchronous callback timing.
// The shared math shim isolates operation ordering, not independent native math parity.
import { DxfWriter } from '../../netDxf/IO/DxfWriter.js';
import { DxfDocument, Hatch, HatchPattern, HatchBoundaryPath, HatchPatternLineDefinition, Vector2, Vector3, MathHelper } from '../../index.js';
import { DotNetMath } from '../../runtime/GeometryRuntime.js';
function patternHatch() {
  const pattern = HatchPattern.Line; pattern.LineDefinitions.Clear(); pattern.Angle = 37; pattern.Scale = .1;
  const line = new HatchPatternLineDefinition(); line.Angle = 11.5;
  line.Origin = new Vector2(1.234567890123, -2.345678901234);
  line.Delta = new Vector2(-3.456789012345, 4.567890123456); line.DashPattern.Add(1.25);
  pattern.LineDefinitions.Add(line);
  const poly = new HatchBoundaryPath.Polyline(); poly.IsClosed = true;
  poly.Vertexes = [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)];
  return new Hatch(pattern, [new HatchBoundaryPath([poly])], false);
}
function sourceCoordinates(point, angle, scale) {
  const sin = DotNetMath.Sin(angle * MathHelper.DegToRad), cos = DotNetMath.Cos(angle * MathHelper.DegToRad);
  return [cos * point.X * scale - sin * point.Y * scale, sin * point.X * scale + cos * point.Y * scale];
}
for (const version of [13, 14, 15, 16, 17, 18]) for (const binary of [false, true]) {
  test(`HATCH pattern writer preserves source product grouping (${version}/${binary})`, () => {
    const hatch = patternHatch(), pattern = hatch.Pattern, line = pattern.LineDefinitions.get_Item(0);
    const expected = [...sourceCoordinates(line.Origin, pattern.Angle, pattern.Scale),
      ...sourceCoordinates(line.Delta, line.Angle + pattern.Angle, pattern.Scale)];
    const doc = new DxfDocument(version), output = new MemoryStream(); doc.Entities.Add(hatch);
    try {
      new DxfWriter().Write(output, doc, binary); output.Position = 0;
      const raw = DxfRawDocument.Load(output), record = Array.from(raw.Sections).flatMap(s => Array.from(s.Records)).find(r => r.Name === 'HATCH');
      for (const [index, code] of [43, 44, 45, 46].entries())
        assert.ok(Object.is(Array.from(record.Tags).find(t => t.Code === code).Value, expected[index]), `Group ${code} changed product grouping`);
    } finally { output.Dispose(); }
  });
}
test('HATCH line output reads geometry after callbacks but captures per-line scale and delta angle', () => {
  const hatch = patternHatch(), p = hatch.Pattern, line = p.LineDefinitions.get_Item(0), scale = p.Scale, angle = line.Angle + p.Angle;
  const writer = new DxfWriter(), written = []; writer.doc = new DxfDocument();
  writer.chunk = { Write(code, value) {
    written.push([code, value]);
    if (code === 53) { p.Angle = 90; p.Scale = 4; line.Origin = new Vector2(2, 3); }
    if (code === 43) { line.Origin = new Vector2(100, 200); line.Delta = new Vector2(5, 6); }
    if (code === 45) line.Delta = new Vector2(100, 200);
    if (code === 79) p.Scale = 8;
  } };
  writer.WriteHatch(hatch);
  const values = code => written.find(p => p[0] === code)[1];
  assert.equal(values(53), angle);
  const origin = sourceCoordinates(new Vector2(2, 3), 90, scale), delta = sourceCoordinates(new Vector2(5, 6), angle, scale);
  for (const [code, expected] of [[43, origin[0]], [44, origin[1]], [45, delta[0]], [46, delta[1]], [49, 1.25 * scale]])
    assert.ok(Object.is(values(code), expected), `Callback-sensitive group ${code}`);
});
test('HATCH failed group 53 write never reads that line geometry', () => {
  const hatch = patternHatch(), line = hatch.Pattern.LineDefinitions.get_Item(0), failure = new Error('writer failure');
  let reads = 0;
  for (const property of ['Origin', 'Delta']) Object.defineProperty(line, property, { get() { reads++; return Vector2.Zero; } });
  const writer = new DxfWriter(); writer.doc = new DxfDocument();
  writer.chunk = { Write(code) { if (code === 53) throw failure; } };
  assert.throws(() => writer.WriteHatch(hatch), error => error === failure); assert.equal(reads, 0);
});
