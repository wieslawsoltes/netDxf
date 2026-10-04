// Port of every original case/assertion in pinned HelixWireTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, Spline, Vector3, MemoryStream } from '../../index.js';
import { NotSupportedException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
const artifactDirectory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
const same = (a, b) => { a = Array.from(a); b = Array.from(b); return a.length === b.length && a.every((v, i) => v?.Equals ? v.Equals(b[i]) : v === b[i]); };
export const HelixWireControls = [new Vector3(5, 0, 0), new Vector3(5, 3, 1), new Vector3(2, 5, 2), new Vector3(0, 5, 3)];
export function RegisterHelixWireTests() {
  for (const v of SupportedVersions) for (const b of [false, true]) for (const c of [0, 1, 2]) for (const r of [false, true]) for (const order of [false, true])
    Run(`helix/wire/${VersionName(v)}/${BooleanName(b)}/${c}/${BooleanName(r)}/${BooleanName(order)}`, () => HelixWire(v, b, c, r, order));
}
export function HelixWireTags(version, constraint, right, reordered) {
  const tags = [], add = (code, value) => tags.push(new DxfTag(code, value));
  for (const [code, value] of [[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'HELIX'],[5,'200'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbSpline'],[70,4],[71,3],[72,8],[73,4],[74,0],[12,3],[22,4],[32,5],[13,6],[23,7],[33,8]]) add(code, value);
  for (let i = 0; i < 8; i++) add(40, i < 4 ? 0 : 1);
  for (let i = 0; i < 4; i++) { const p = HelixWireControls[i]; add(10, p.X); add(20, p.Y); add(30, p.Z); add(41, 1 + i * .125); }
  add(100, 'AcDbHelix');
  const definition = [[90,29],[91,63],[10,17.25],[20,-11.5],[30,23],[11,22.25],[21,-11.5],[31,23],[12,0],[22,0],[32,2],[40,2.75],[41,3.125],[42,-1.5],[290,right],[280,constraint]];
  // The deliberately independent definition must not cause the curve to be refitted.
  if (reordered) definition.reverse();
  for (const [code, value] of definition) add(code, value);
  for (const [code, value] of [[1001,'HELIX_TEST'],[1000,'after helix'],[0,'LINE'],[5,'201'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,20],[20,30],[30,40],[11,50],[21,60],[31,70],[0,'ENDSEC'],[0,'EOF']]) add(code, value);
  return tags;
}
export function HelixWire(version, binary, constraint, right, reordered) {
  const tags = HelixWireTags(version, constraint, right, reordered), input = new MemoryStream(RawFixtureBytes(tags, binary));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'HELIX input rejected.');
    const entity = single(Array.from(doc.Entities.All).filter(e => e.CodeName === 'HELIX'));
    Check(entity instanceof Spline, 'HELIX must retain its inherited spline representation.'); const spline = entity;
    Check(same(spline.ControlPoints, HelixWireControls), 'HELIX controls were refitted.');
    Check(new Vector3(3, 4, 5).Equals(spline.StartTangent), 'Spline tangent collided with HELIX axis codes');
    Check(new Vector3(6, 7, 8).Equals(spline.EndTangent), 'Spline end tangent');
    const clone = entity.Clone(); Equal('HELIX', clone.CodeName, 'Clone sliced the derived entity'); doc.Entities.Add(clone);
    const definition = packetTags => { const values = Array.from(packetTags), start = values.findIndex(t => t.Code === 100 && t.Value === 'AcDbHelix'); Check(start >= 0, 'HELIX subclass missing'); const rest = values.slice(start + 1), end = rest.findIndex(t => t.Code === 1001); return end < 0 ? rest : rest.slice(0, end); };
    const expected = definition(tags);
    for (let cycle = 0; cycle < 3; cycle++) {
      const output = new MemoryStream();
      try {
        if (version < DxfVersion.AutoCad2007) {
          if (GetTypedIOConfiguration() === 'Debug') Throws(NotSupportedException, () => doc.Save(output, binary));
          else Check(!doc.Save(output, binary), 'Earlier writer profile admitted HELIX.');
          Equal(0, output.Length, 'HELIX preflight wrote to stream'); return;
        }
        Check(doc.Save(output, cycle % 2 === 0 ? binary : !binary), 'HELIX save failed.'); output.Position = 0;
        const records = Array.from(DxfRawDocument.Load(output).Sections).flatMap(s => Array.from(s.Records)).filter(r => r.Name === 'HELIX'); Equal(2, records.length, 'Original/clone HELIX count');
        for (const record of records) { const packet = definition(record.Tags); for (const tag of expected) Equal(tag.Value, single(packet.filter(t => t.Code === tag.Code)).Value, 'HELIX parameter changed'); }
        if (cycle === 0 && !reordered) { fs.mkdirSync(artifactDirectory, {recursive:true}); fs.writeFileSync(path.join(artifactDirectory, `helix-wire-${VersionName(version)}-${BooleanName(binary)}-${constraint}-${BooleanName(right)}.dxf`), output.ToArray()); }
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'HELIX reload failed.');
        for (const current of Array.from(doc.Entities.All).filter(e => e.CodeName === 'HELIX')) {
          Check(current instanceof Spline, 'Reload sliced inherited spline'); Check(same(current.ControlPoints, HelixWireControls), 'Repeated HELIX save changed control polygon.');
          Check(same(spline.Knots, current.Knots) && same(spline.Weights, current.Weights), 'Repeated HELIX knots/weights changed.');
          Equal('after helix', single(current.XData.get_Item('HELIX_TEST').XDataRecord).Value, 'HELIX XData');
        }
        Check(new Vector3(20, 30, 40).Equals(single(doc.Entities.Lines).StartPoint), 'Following LINE');
      } finally { output.Dispose(); }
    }
    Check(input.CanRead, 'HELIX reader closed caller stream.');
  } finally { input.Dispose(); }
}
