// Port of tests/netDxf.Conformance/RawEmbeddedHandleTests.cs, preserving all test identities.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfTag, DxfVersion, DxfRawHandleIndex, DxfRawHandleRole as R, DxfRawHandleDiagnosticKind as D,
  DxfRawReferenceTraversal as F, DxfTagValueType } from '../../index.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
import { Encoding } from '../../runtime/Encoding.js';
import { Run, Check, Equal, VersionName, BooleanName } from './TestHarness.js';
import { HandleProfiles, HandleProfileName } from './RawHandleIndexTests.js';
import { RawFixtureBytes, LoadRaw, SaveRaw, SameRawTags } from './RawDocumentTests.js';
const T = (code, value) => new DxfTag(code, value);
export function RegisterRawEmbeddedHandleTests() {
  for (const v of HandleProfiles) for (const b of [false, true]) {
    for (const t of ['MTEXT','ATTRIB','ATTDEF','VENDOR_ENTITY']) for (const c of [false, true])
      Run(`handles/embedded/context/${VersionName(v)}/${BooleanName(b)}/${t}/${BooleanName(c)}`, () => EmbeddedHandleContext(v, b, t, c));
    Run(`handles/embedded/unsafe-remap/${VersionName(v)}/${BooleanName(b)}`, () => EmbeddedHandleUnsafeRemap(v, b));
    for (let c = 0; c < 4; c++)
      Run(`handles/embedded/context-boundary/${VersionName(v)}/${BooleanName(b)}/${c}`, () => EmbeddedHandleBoundary(v, b, c));
  }
}
export function EmbeddedHandleLoad(version, binary, tags) {
  return LoadRaw(RawFixtureBytes(tags, binary, Encoding.UTF8, '\n', version === DxfVersion.AutoCad12));
}
export function EmbeddedHandleTags(version, type, controls) {
  const tags = [T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,HandleProfileName(version)),
    T(9,'$DWGCODEPAGE'),T(3,'ANSI_1252'),T(9,'$HANDSEED'),T(5,'1000'),T(0,'ENDSEC'),
    T(0,'SECTION'),T(2,'OBJECTS'),T(0,'DICTIONARY'),T(5,'10'),T(330,'0'),T(100,'AcDbDictionary'),
    T(0,'DICTIONARY'),T(5,'20'),T(330,'10'),T(100,'AcDbDictionary'),T(0,'ENDSEC'),
    T(0,'SECTION'),T(2,'ENTITIES'),T(0,type),T(5,'A'),T(102,'{ACAD_REACTORS'),T(330,'20'),T(102,'}'),
    T(330,'10'),T(100,'AcDbEntity'),T(100,'AcDbMText'),T(340,'20'),
    T(101,'Embedded Object'),T(70,1),T(330,'FA'),T(340,'FE'),T(360,'20'),T(5,'30'),T(320,'40'),T(1001,'EMBEDDED_APP'),T(1005,'20')];
  if (controls) tags.push(T(102,'{ACAD_REACTORS'),T(330,'20'),T(102,'}'),T(102,'application payload, not a control'),
    T(100,'AcDbEntity'),T(101,'Embedded Object'),T(5,'A'),T(360,'FE'));
  tags.push(T(0,'POINT'),T(5,'B'),T(330,'10'),T(100,'AcDbEntity'),T(100,'AcDbPoint'),T(10,1),T(20,2),T(30,3),
    T(1001,'REAL_XDATA'),T(1005,'20'),T(0,'ENDSEC'),T(0,'EOF'));
  return tags;
}
export function EmbeddedHandleContext(v, b, type, controls) {
  const raw = EmbeddedHandleLoad(v, b, EmbeddedHandleTags(v, type, controls)), before = SaveRaw(raw);
  const index = DxfRawHandleIndex.Create(raw), record = index.FindDefinitions('A')[0].Record;
  const start = raw.Tags.findIndex(t => t.Code === 101), expected = [];
  for (let i = start + 1; i < record.EndTagIndex; i++) if (raw.Tags[i].ValueType === DxfTagValueType.Handle) expected.push(i);
  const items = index.GetOccurrences(record), opaque = items.filter(x => x.Role === R.Opaque);
  Equal(expected, opaque.map(x => x.TagIndex)); Check(opaque.every(x => x.Context === 'Embedded Object'));
  Equal(0, index.Diagnostics.Count); Equal(1, items.filter(x => x.Role === R.Owner).length);
  Equal(1, items.filter(x => x.Role === R.Reactor).length); Equal(0, index.FindDefinitions('30').Count); Equal(0, index.FindReferences('FA').Count);
  Equal('REAL_XDATA', index.GetOccurrences(index.FindDefinitions('B')[0].Record).find(x => x.Role === R.XData).Context);
  const closure = index.GetDependencyClosure([record], F.All); Equal(3, closure.Records.Count);
  Check(closure.AreSelectedReferencesResolved); Equal(expected, closure.UninterpretedHandles.map(x => x.TagIndex));
  const changed = index.RemapHandles(new Map([['B','B1']]));
  for (const position of expected) Check(raw.Tags[position] === changed.Tags[position]);
  Equal(before, SaveRaw(raw)); SameRawTags(changed.Tags, LoadRaw(SaveRaw(changed, !b)).Tags);
  if (v === DxfVersion.AutoCad2018 && type === 'MTEXT') {
    const dir = process.env.DXF_JS_TEST_ARTIFACTS || path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance');
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, `handles-embedded-${BooleanName(b)}-${BooleanName(controls)}.dxf`), SaveRaw(changed));
  }
}
export function EmbeddedHandleUnsafeRemap(v, b) {
  const raw = EmbeddedHandleLoad(v, b, EmbeddedHandleTags(v, 'MTEXT', false)), index = DxfRawHandleIndex.Create(raw), original = SaveRaw(raw);
  let caught = false;
  try { index.RemapHandles(new Map([['20','200']])); }
  catch (error) { Check(error instanceof InvalidOperationException); Check(error.message.toLowerCase().includes('opaque')); caught = true; }
  Check(caught, 'Affected embedded-object slot was rewritten without its application schema.');
  Equal(original, SaveRaw(raw)); Check(raw === index.RemapHandles(new Map([['20','0020']])));
}
export function EmbeddedHandleBoundary(v, b, context) {
  const tags = EmbeddedHandleTags(v, 'MTEXT', false), start = tags.findIndex(t => t.Code === 101);
  const end = tags.findIndex((t, i) => i >= start && t.Code === 0); tags.splice(start, end - start);
  if (context === 0) tags.splice(start, 0, T(102,'{VENDOR'),T(101,'Embedded Object'),T(330,'FA'),T(102,'}'),T(340,'20'));
  else if (context === 1) {
    const at = tags.findIndex(t => t.Code === 0 && t.Value === 'DICTIONARY');
    tags[at] = T(0,'XRECORD'); tags[at + 3] = T(100,'AcDbXrecord');
    tags.splice(at + 4, 0, T(101,'Embedded Object'),T(330,'FA'),T(1001,'XREC'),T(1005,'20'));
  } else if (context === 2) tags.splice(start, 0, T(101,'not an embedded object'),T(340,'20'));
  else tags.splice(start, 0, T(102,'}'),T(101,'Embedded Object'),T(330,'FA'));
  const index = DxfRawHandleIndex.Create(EmbeddedHandleLoad(v, b, tags));
  if (context === 3) Equal(1, index.Diagnostics.filter(x => x.Kind === D.InvalidControlGroup).length);
  else {
    Equal(0, index.Diagnostics.Count); Equal(1, index.FindDefinitions('A').Count);
    if (context === 0 || context === 2) Equal(2, index.GetOccurrences(index.FindDefinitions('A')[0].Record).filter(x => x.Role === R.HardPointer).length);
    if (context === 1) Equal('XREC', index.GetOccurrences(index.FindDefinitions('10')[0].Record).find(x => x.Role === R.XData).Context);
  }
}
