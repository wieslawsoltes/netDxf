// Port of the pinned C# conformance module; original case identities/assertions retained.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Run, Check, Equal, SameDoubleBits, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, HatchBoundaryPath, MemoryStream, Vector2, Vector3 } from '../../index.js';
import { HatchEdgeDispatchTags, ExpectEdgePacketInvalid } from './HatchEdgeDispatchTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
export const HatchFitValues = [new Vector2(0, 0), new Vector2(5.000000000000001, 5), new Vector2(10, 0)];
const single = items => { const a = Array.from(items); Equal(1, a.length, 'Expected one item'); return a[0]; };
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
const tagsOf = pairs => pairs.map(([c, v]) => new DxfTag(c, v));
export function RegisterHatchSplineFitTests() {
  for (const v of SupportedVersions.filter(v => v >= DxfVersion.AutoCad2010)) for (const b of [false, true]) {
    const suffix = `${VersionName(v)}/${BooleanName(b)}`;
    for (const count of [0, 1, 3]) for (let mask = 0; mask < 5; mask++) for (const comments of b ? [false] : [false, true])
      Run(`hatch/spline-fit/roundtrip/${suffix}/${count}/${mask}/${BooleanName(comments)}`, () => HatchSplineFitRoundTrip(v, b, count, mask, comments));
    Run(`hatch/spline-fit/reversed-tangents/${suffix}`, () => HatchSplineFitReversed(v, b));
    for (let failure = 0; failure < 5; failure++) Run(`hatch/spline-fit/invalid/${suffix}/${failure}`, () => HatchSplineFitInvalid(v, b, failure));
  }
  for (const v of SupportedVersions.filter(v => v < DxfVersion.AutoCad2010)) for (const b of [false, true])
    Run(`hatch/spline-fit/legacy-empty/${VersionName(v)}/${BooleanName(b)}`, () => HatchSplineFitLegacyEmpty(v, b));
}
export function HatchSplineFitTags(version, count, mask, comments) {
  const tags = HatchEdgeDispatchTags(version, 4), fit = tags.findIndex(t => t.Code === 97); tags[fit] = new DxfTag(97, count);
  const packet = [];
  for (let i = 0; i < count; i++) packet.push(...tagsOf([[11, HatchFitValues[i].X], [21, HatchFitValues[i].Y]]));
  if ((mask & 1) !== 0 || mask === 4) packet.push(...tagsOf([[12, mask === 4 ? 0 : 10], [22, mask === 4 ? 0 : 20]]));
  if ((mask & 2) !== 0 || mask === 4) packet.push(...tagsOf([[13, mask === 4 ? 0 : 10], [23, mask === 4 ? 0 : -20]]));
  packet.push(...tagsOf([[72, 1], [10, 10], [20, 0], [11, 0], [21, 0]]));
  tags.splice(fit + 1, 0, ...packet); tags[tags.findIndex(t => t.Code === 93)] = new DxfTag(93, 2);
  if (comments) for (let i = fit + packet.length + 1; i > fit; i--) tags.splice(i, 0, new DxfTag(999, '97 12 13 ENDSEC'));
  return tags;
}
export function HatchFitEntity(hatch) { return single(Array.from(single(hatch.BoundaryPaths).Edges).filter(e => e instanceof HatchBoundaryPath.Spline)).ConvertTo(); }
export function AssertHatchFitEntity(spline, count, mask) {
  Equal(count, spline.FitPoints.Count, 'Spline fit-point count');
  for (let i = 0; i < count; i++) { const p = spline.FitPoints.get_Item(i); SameDoubleBits(HatchFitValues[i].X, p.X, 'Fit X precision/order'); SameDoubleBits(HatchFitValues[i].Y, p.Y, 'Fit Y precision/order'); }
  Equal((mask & 1) !== 0 ? new Vector3(10, 20, 0) : mask === 4 ? Vector3.Zero : null, spline.StartTangent, 'Start tangent presence/value');
  Equal((mask & 2) !== 0 ? new Vector3(10, -20, 0) : mask === 4 ? Vector3.Zero : null, spline.EndTangent, 'End tangent presence/value');
  Equal(3, spline.ControlPoints.length, 'Fit metadata changed controls'); Equal(6, spline.Knots.length, 'Fit metadata changed knots');
}
export function HatchSplineFitRoundTrip(version, binary, count, mask, comments) {
  const input = new MemoryStream(RawFixtureBytes(HatchSplineFitTags(version, count, mask, comments), binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Spline fit fixture rejected.'); const hatch = single(doc.Entities.Hatches);
    AssertHatchFitEntity(HatchFitEntity(hatch), count, mask);
    const boundary = single(hatch.BoundaryPaths), edge = single(Array.from(boundary.Edges).filter(e => e instanceof HatchBoundaryPath.Spline));
    AssertHatchFitEntity(edge.Clone().ConvertTo(), count, mask);
    AssertHatchFitEntity(single(Array.from(boundary.Clone().Edges).filter(e => e instanceof HatchBoundaryPath.Spline)).ConvertTo(), count, mask);
    doc.Entities.Add(hatch.Clone());
    for (let cycle = 0; cycle < 3; cycle++) {
      const output = new MemoryStream(), transport = cycle % 2 === 0 ? !binary : binary;
      try {
        Check(doc.Save(output, transport), 'Fit metadata save failed.'); output.Position = 0; const raw = DxfRawDocument.Load(output);
        for (const record of Array.from(raw.Sections).flatMap(s => Array.from(s.Records)).filter(r => r.Name === 'HATCH')) {
          const all = Array.from(record.Tags), start = all.findIndex(t => t.Code === 94), end = all.findIndex((t, i) => i >= start && t.Code === 72), tags = all.slice(start, end < 0 ? undefined : end);
          Equal(count, single(tags.filter(t => t.Code === 97)).Value, 'Serialized fit count');
          Equal(HatchFitValues.slice(0, count).map(p => p.X), tags.filter(t => t.Code === 11).map(t => t.Value), 'Serialized fit X data changed.');
          Equal(mask === 4 || (mask & 1) !== 0, tags.some(t => t.Code === 12), 'Serialized start presence'); Equal(mask === 4 || (mask & 2) !== 0, tags.some(t => t.Code === 13), 'Serialized end presence');
        }
        if (cycle === 1 && count === 3 && mask === 3 && !comments) { fs.mkdirSync(artifacts, { recursive: true }); fs.writeFileSync(path.join(artifacts, `hatch-spline-fit-${VersionName(version)}-${BooleanName(binary)}.dxf`), output.ToArray()); }
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Spline fit reload failed.');
        for (const current of doc.Entities.Hatches) {
          AssertHatchFitEntity(HatchFitEntity(current), count, mask); Equal(2.5, current.Elevation, 'Fit metadata changed elevation'); Equal(new Vector2(2, 3), single(current.SeedPoints), 'Fit metadata changed seed');
          Equal('after pattern', single(current.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'Following fit XData lost'); Equal(2, single(current.BoundaryPaths).Edges.Count, 'Following LINE edge lost');
        }
      } finally { output.Dispose(); }
    }
    Check(input.CanRead, 'Fit reader closed caller stream.');
  } finally { input.Dispose(); }
}
export function HatchSplineFitReversed(version, binary) {
  const tags = HatchSplineFitTags(version, 3, 3, false), start = tags.findIndex(t => t.Code === 12), first = tags.splice(start, 2); tags.splice(start + 2, 0, ...first);
  const input = new MemoryStream(RawFixtureBytes(tags, binary)); try { const doc = DxfDocument.Load(input); Check(doc !== null, 'Reordered tangent packet rejected.'); AssertHatchFitEntity(HatchFitEntity(single(doc.Entities.Hatches)), 3, 3); } finally { input.Dispose(); }
}
export function HatchSplineFitInvalid(version, binary, failure) {
  const tags = HatchSplineFitTags(version, 3, 3, false), start = tags.findIndex(t => t.Code === 12), fit = tags.findIndex(t => t.Code === 97);
  switch (failure) { case 0: tags.splice(start, 0, ...tagsOf([[12, 1], [22, 2]])); break; case 1: tags.splice(start, 0, ...tagsOf([[13, 1], [23, 2]])); break; case 2: tags.splice(start + 1, 1); break; case 3: tags[fit] = new DxfTag(97, 2); break; case 4: tags[fit] = new DxfTag(97, 2147483647); break; }
  ExpectEdgePacketInvalid(tags, binary);
}
export function HatchSplineFitLegacyEmpty(version, binary) {
  const input = new MemoryStream(RawFixtureBytes(HatchEdgeDispatchTags(version, 4), binary)); try {
    const doc = DxfDocument.Load(input); Check(doc !== null, 'Legacy control spline rejected.'); const output = new MemoryStream(); try {
      Check(doc.Save(output, !binary), 'Legacy control spline export changed.'); output.Position = 0;
      const tags = single(Array.from(DxfRawDocument.Load(output).Sections).flatMap(s => Array.from(s.Records)).filter(r => r.Name === 'HATCH')).Tags;
      Equal(1, Array.from(tags).filter(t => t.Code === 97).length, 'Legacy spline emitted a fit packet');
    } finally { output.Dispose(); }
  } finally { input.Dispose(); }
}
