// Port of pinned SplinePeriodicInputTests.cs, including its separately registered overlap cases.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, Spline, Block, Insert, Vector3, MemoryStream } from '../../index.js';
import { NotSupportedException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, SameDoubleBits, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
const artifactDirectory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
const same = (a, b) => { a = Array.from(a); b = Array.from(b); return a.length === b.length && a.every((v, i) => v?.Equals ? v.Equals(b[i]) : v === b[i]); };
export function RegisterSplinePeriodicInputTests() {
  for (const v of SupportedVersions) for (const b of [false, true]) for (const d of [1, 2, 3]) for (const f of [2, 3, 7, 2048, 2055]) for (const w of [false, true]) for (const p of f === 2 ? [0, d] : [0])
    Run(`spline/periodic-input/${VersionName(v)}/${BooleanName(b)}/${d}/${f}/${BooleanName(w)}/${p}`, () => SplinePeriodicInput(v, b, d, f, w, p));
  RegisterSplinePeriodicOverlapTests();
}
export function RegisterSplinePeriodicOverlapTests() {
  for (const v of SupportedVersions) for (const b of [false, true]) for (const f of [2, 2048]) for (const c of [10, 20, 30, 41])
    Run(`spline/periodic-input/nonrepresentable/${VersionName(v)}/${BooleanName(b)}/${f}/${c}`, () => {
      const tags = PeriodicSplineInputTags(v, 2, f, true), at = tags.findIndex(t => t.Code === c); tags[at] = new DxfTag(c, tags[at].Value + .125);
      const input = new MemoryStream(RawFixtureBytes(tags, b));
      try {
        if (GetTypedIOConfiguration() === 'Debug') { let error; try { DxfDocument.Load(input); } catch (e) { error = e; } Check(error instanceof NotSupportedException, 'Nonoverlapping cyclic data was silently compacted.'); Check(error.message.includes('Periodic SPLINE'), 'Missing contextual periodic layout diagnostic.'); }
        else Check(DxfDocument.Load(input) === null, 'Nonoverlapping cyclic data was silently compacted.');
        Check(input.CanRead, 'Unsupported import closed caller stream.');
      } finally { input.Dispose(); }
    });
}
export function PeriodicSplineInputTags(version, degree, flags, weighted, phase = 0) {
  const tags = [], add = (code, value) => tags.push(new DxfTag(code, value));
  for (const [code, value] of [[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'SPLINE'],[5,'200'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbSpline'],[70,flags],[71,degree],[72,5+2*degree+1],[73,5+degree],[74,0]]) add(code, value);
  for (let i = 0; i < 5 + 2 * degree + 1; i++) add(40, -3.25 + i * .125);
  for (let i = 0; i < 5 + degree; i++) { const j = (i + 5 - degree + phase) % 5; add(10, j*j); add(20, j%2*4-j); add(30, j); if (weighted) add(41, 1 + j*.25); }
  for (const [code, value] of [[1001,'PERIODIC_INPUT'],[1000,'standard periodic bit'],[0,'LINE'],[5,'201'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,20],[20,30],[30,40],[11,50],[21,60],[31,70],[0,'ENDSEC'],[0,'EOF']]) add(code, value);
  return tags;
}
export function SplinePeriodicInput(version, binary, degree, flags, weighted, phase = 0) {
  const tags = PeriodicSplineInputTags(version, degree, flags, weighted, phase), input = new MemoryStream(RawFixtureBytes(tags, binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Periodic input rejected.'); const spline = single(doc.Entities.Splines);
    Check(spline.IsClosedPeriodic, 'Documented periodic flag did not set periodic state.'); Equal(5, spline.ControlPoints.length, 'Wire overlap not normalized into compact controls');
    for (let j = 0; j < 5; j++) { const k = (j + phase) % 5; Check(new Vector3(k*k, k%2*4-k, k).Equals(spline.ControlPoints[j]), 'Periodic control order'); Equal(weighted ? 1 + k*.25 : 1, spline.Weights[j], 'Periodic control weight'); }
    const expectedKnots = tags.filter(t => t.Code === 40).map(t => t.Value); Check(same(expectedKnots, spline.Knots), 'Import regenerated periodic knots.');
    const samples = Array.from(spline.PolygonalVertexes(31)), block = new Block('PeriodicBlock'); block.Entities.Add(spline.Clone());
    const clone = new Insert(block).Clone(), nested = single(Array.from(clone.Block.Entities).filter(e => e instanceof Spline));
    Check(nested.IsClosedPeriodic && same(nested.ControlPoints, spline.ControlPoints), 'Nested clone changed periodic data.'); doc.Entities.Add(spline.Clone());
    for (let cycle = 0; cycle < 3; cycle++) {
      const output = new MemoryStream();
      try {
        Check(doc.Save(output, cycle % 2 === 0 ? binary : !binary), 'Periodic save failed.'); output.Position = 0;
        for (const record of Array.from(DxfRawDocument.Load(output).Sections).flatMap(s => Array.from(s.Records)).filter(r => r.Name === 'SPLINE')) {
          const packet = Array.from(record.Tags); Check((single(packet.filter(t => t.Code === 70)).Value & 3) === 3, 'Export omitted standard closed/periodic bits.');
          for (const code of [10, 20, 30, 40]) { const expected = tags.slice(0, tags.findIndex(t => t.Code === 1001)).filter(t => t.Code === code).map(t => t.Value), actual = packet.filter(t => t.Code === code).map(t => t.Value); Equal(expected.length, actual.length, 'Periodic serialized count'); for (let i = 0; i < actual.length; i++) SameDoubleBits(expected[i], actual[i], 'Periodic wire coordinate/knot'); }
        }
        if (cycle === 0 && flags === 7 && weighted) { fs.mkdirSync(artifactDirectory, {recursive:true}); fs.writeFileSync(path.join(artifactDirectory, `spline-periodic-input-${VersionName(version)}-${BooleanName(binary)}-${degree}.dxf`), output.ToArray()); }
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Periodic reload rejected.');
        for (const current of doc.Entities.Splines) { Check(current.IsClosedPeriodic, 'Reload lost periodic state.'); Check(same(current.Knots, expectedKnots) && same(current.ControlPoints, spline.ControlPoints), 'Periodic geometry changed across transports.'); Check(same(samples, current.PolygonalVertexes(31)), 'Periodic evaluator changed across transports.'); Equal('standard periodic bit', single(current.XData.get_Item('PERIODIC_INPUT').XDataRecord).Value, 'Following periodic XData'); }
        Check(new Vector3(20, 30, 40).Equals(single(doc.Entities.Lines).StartPoint), 'Following LINE');
      } finally { output.Dispose(); }
    }
    Check(input.CanRead, 'Periodic parser closed caller stream.');
  } finally { input.Dispose(); }
}
