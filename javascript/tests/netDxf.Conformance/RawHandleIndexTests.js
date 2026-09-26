// Port of tests/netDxf.Conformance/RawHandleIndexTests.cs; typed-output controls remain unported.
import { DxfTag, DxfRawDocument, DxfVersion, DxfRawHandleIndex, DxfRawHandleRole as R,
  DxfRawHandleDiagnosticKind as D, DxfRawHandleIndexOptions } from '../../index.js';
import * as E from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { SaveRaw, LoadRaw } from './RawDocumentTests.js';
export const HandleProfiles = [DxfVersion.AutoCad12, DxfVersion.AutoCad13, DxfVersion.AutoCad14, ...SupportedVersions];
export const HandleProfileName = HeaderVersion;
const T = (code, value) => new DxfTag(code, value);
export function RegisterRawHandleIndexTests() {
  for (const v of HandleProfiles) for (const b of [false, true]) {
    Run(`handles/contexts/${VersionName(v)}/${BooleanName(b)}`, () => HandleContexts(v, b));
    for (let k = 0; k < 7; k++)
      Run(`handles/diagnostics/${VersionName(v)}/${BooleanName(b)}/${k}`, () => HandleDiagnostics(v, b, k));
  }
  Run('handles/budgets-cancellation-and-stale-records', HandleIndexGuards);
  Run('handles/deep-owner-chain-no-recursion', HandleDeepChain);
  Run('handles/header-and-xrecord-xdata-context', HandleHeaderAndXRecord);
  Run('handles/read-only-concurrent-lookup', HandleConcurrentLookup);
}
export function HandleFixture(version) {
  return [T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,HandleProfileName(version)),
    T(9,'$DWGCODEPAGE'),T(3,'ANSI_1252'),T(9,'$HANDSEED'),T(5,'0000AB'),T(0,'ENDSEC'),
    T(0,'SECTION'),T(2,'TABLES'),T(0,'TABLE'),T(2,'DIMSTYLE'),T(5,'11'),
    T(0,'DIMSTYLE'),T(105,'000d'),DxfTag.CreateDimensionStyleArrowName('00aB'),T(330,'11'),
    T(100,'AcDbSymbolTableRecord'),T(100,'AcDbDimStyleTableRecord'),T(2,'Style'),T(0,'ENDTAB'),T(0,'ENDSEC'),
    T(0,'SECTION'),T(2,'ENTITIES'),T(0,'LINE'),T(5,'00aB'),
    T(102,'{ACAD_REACTORS'),T(330,'E'),T(102,'}'),T(102,'{ACAD_XDICTIONARY'),T(360,'C'),T(102,'}'),
    T(102,'{VENDOR'),T(5,'ab'),T(330,'DEADBEEF'),T(102,'{ACAD_REACTORS'),T(330,'C'),T(102,'}'),T(102,'}'),
    T(330,'10'),T(100,'AcDbEntity'),T(8,'0'),T(100,'AcDbLine'),
    T(10,1),T(20,2),T(30,3),T(11,4),T(21,5),T(31,6),
    T(330,'F'),T(340,'E'),T(350,'C'),T(360,'E'),T(390,'C'),T(480,'E'),T(320,'AB'),
    T(1001,'HANDLES_TEST'),T(1002,'{'),T(1005,'E'),T(1002,'}'),
    T(0,'ENDSEC'),T(0,'SECTION'),T(2,'OBJECTS'),
    T(0,'DICTIONARY'),T(5,'10'),T(330,'0'),T(100,'AcDbDictionary'),T(3,'Children'),T(350,'C'),
    T(0,'DICTIONARY'),T(5,'C'),T(330,'AB'),T(100,'AcDbDictionary'),T(3,'Entry'),T(360,'E'),
    T(0,'XRECORD'),T(5,'E'),T(330,'C'),T(100,'AcDbXrecord'),T(280,1),
    T(330,'DEADBEEF'),T(102,'application payload, not a control group'),T(1,'data'),
    T(0,'ENDSEC'),T(0,'SECTION'),T(2,'APP_SECTION'),T(0,'CUSTOM'),T(5,'AB'),T(0,'ENDSEC'),T(0,'EOF')];
}
export function HandleRoundTrip(tags, binary) { return LoadRaw(SaveRaw(DxfRawDocument.Create(tags, binary))); }
export function HandleContexts(v, b) {
  const doc = HandleRoundTrip(HandleFixture(v), b), before = SaveRaw(doc), index = DxfRawHandleIndex.Create(doc);
  Equal(1, index.FindDefinitions('ab').Count);
  Equal('LINE', index.FindDefinitions('000AB')[0].Record.Name);
  const definition = index.FindDefinitions('AB')[0];
  Equal(doc.Tags[definition.TagIndex].Value, definition.Handle);
  Equal('00aB', DxfRawHandleIndex.Create(DxfRawDocument.Create(HandleFixture(v))).FindDefinitions('AB')[0].Handle);
  Equal(105, index.FindDefinitions('D')[0].Code);
  Check(index.Occurrences.every(x => x.Code !== 5 || x.Record?.Name !== 'DIMSTYLE'));
  const items = index.GetOccurrences(definition.Record);
  Equal(1, items.filter(x => x.Role === R.Owner).length);
  Equal('10', items.find(x => x.Role === R.Owner).CanonicalHandle);
  Equal('E', items.find(x => x.Role === R.Reactor).CanonicalHandle);
  Equal('C', items.find(x => x.Role === R.ExtensionDictionary).CanonicalHandle);
  Equal(3, items.filter(x => x.Role === R.Opaque).length);
  Check(items.some(x => x.Code === 330 && x.Role === R.SoftPointer && x.CanonicalHandle === 'F'));
  Equal('HANDLES_TEST', items.find(x => x.Role === R.XData).Context);
  Check(index.FindReferences('AB').every(x => x.Role !== R.Arbitrary));
  Equal(1, index.Occurrences.filter(x => x.Role === R.HeaderSeed).length);
  Equal(1, index.Diagnostics.filter(x => x.Kind === D.UnresolvedReference).length);
  Equal('F', index.Diagnostics[0].Handle); Equal(1, index.Diagnostics.Count);
  for (const item of index.Occurrences) Equal(item.Handle, doc.Tags[item.TagIndex].Value);
  Equal(before, SaveRaw(doc));
}
export function HandleDiagnostics(v, b, variant) {
  const tags = HandleFixture(v), at = tags.findIndex(t => t.Code === 0 && t.Value === 'LINE');
  const expected = [D.DuplicateIdentity, D.MultipleIdentities, D.NullIdentity, D.MultipleOwners,
    D.InvalidControlGroup, D.OwnerCycle, D.AmbiguousReference][variant];
  if (variant === 0 || variant === 6) tags.splice(at, 0, T(0,'POINT'), T(5, variant === 0 ? 'AB' : 'E'));
  if (variant === 1) tags.splice(at + 2, 0, T(5,'222'));
  if (variant === 2) tags[at + 1] = T(5,'000');
  if (variant === 3) tags.splice(at + 2, 0, T(330,'E'));
  if (variant === 4) tags.splice(at + 2, 0, T(102,'}'));
  if (variant === 5) tags[tags.findIndex((t, i) => i >= at && t.Code === 330 && t.Value === '10')] = T(330,'C');
  const index = DxfRawHandleIndex.Create(HandleRoundTrip(tags, b));
  Check(index.Diagnostics.some(x => x.Kind === expected), 'Missing diagnostic ' + expected);
  if (variant === 0) Equal(2, index.FindDefinitions('00ab').Count);
  if (variant === 5) Equal(2, index.Diagnostics.filter(x => x.Kind === expected).length);
}
export function HandleIndexGuards() {
  const doc = DxfRawDocument.Create(HandleFixture(DxfVersion.AutoCad2018)), index = DxfRawHandleIndex.Create(doc);
  Throws(E.ArgumentNullException, () => DxfRawHandleIndex.Create(null));
  Throws(E.ArgumentOutOfRangeException, () => new DxfRawHandleIndexOptions(0));
  Throws(E.ArgumentOutOfRangeException, () => new DxfRawHandleIndexOptions(1, 0));
  Throws(E.InvalidDataException, () => DxfRawHandleIndex.Create(doc, new DxfRawHandleIndexOptions(1)));
  const bad = HandleFixture(DxfVersion.AutoCad2018), at = bad.findIndex(t => t.Code === 0 && t.Value === 'LINE');
  bad.splice(at, 0, T(0,'POINT'), T(5,'AB'));
  Throws(E.InvalidDataException, () => DxfRawHandleIndex.Create(DxfRawDocument.Create(bad), new DxfRawHandleIndexOptions(1000, 1)));
  Throws(E.OperationCanceledException, () => DxfRawHandleIndex.Create(doc, null, { aborted: true }));
  for (const handle of ['', ' 1', '1 ', '+1', '0x1', 'G', '10000000000000000'])
    Throws(E.ArgumentException, () => index.FindDefinitions(handle));
  Throws(E.ArgumentNullException, () => index.FindReferences(null));
  const stale = doc.WithTags(doc.Tags).Sections.flatMap(s => s.Records)[0];
  Throws(E.ArgumentException, () => index.GetOccurrences(stale));
  for (const items of [index.Occurrences, index.Diagnostics, index.FindDefinitions('AB')])
    Throws(TypeError, () => items.splice(0));
}
export function HandleDeepChain() {
  const tags = [T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,'AC1032'),T(0,'ENDSEC'),T(0,'SECTION'),T(2,'OBJECTS')];
  for (let i = 12000; i >= 1; i--)
    tags.push(T(0,'DICTIONARY'), T(5,i.toString(16).toUpperCase()), T(330,(i-1).toString(16).toUpperCase()), T(100,'AcDbDictionary'));
  tags.push(T(0,'ENDSEC'), T(0,'EOF'));
  const index = DxfRawHandleIndex.Create(DxfRawDocument.Create(tags));
  Equal(24000, index.Occurrences.Count); Equal(0, index.Diagnostics.Count);
}
export async function HandleConcurrentLookup() {
  const doc = DxfRawDocument.Create(HandleFixture(DxfVersion.AutoCad2018)), index = DxfRawHandleIndex.Create(doc);
  // Native JS objects are isolate-local. This checks interleaved readers, not shared CLR threads.
  await Promise.all(Array.from({ length: 256 }, async () => {
    await Promise.resolve();
    Equal('LINE', index.FindDefinitions('aB')[0].Record.Name); Check(index.FindReferences('E').Count > 1);
    const independent = DxfRawHandleIndex.Create(doc);
    Equal(index.Occurrences.map(x => x.TagIndex), independent.Occurrences.map(x => x.TagIndex));
  }));
}
export function HandleHeaderAndXRecord() {
  const tags = HandleFixture(DxfVersion.AutoCad2018), header = tags.findIndex(t => t.Code === 0 && t.Value === 'ENDSEC');
  tags.splice(header, 0, T(9,'$CUSTOM'),T(5,'AB'),T(9,'$CUSTOMARB'),T(320,'AB'),T(9,'$CMATERIAL'),T(347,'C'));
  const data = tags.findIndex(t => t.Code === 102 && t.Value === 'application payload, not a control group');
  tags.splice(data + 2, 0, T(1001,'XREC_APP'),T(1005,'C'));
  const index = DxfRawHandleIndex.Create(DxfRawDocument.Create(tags));
  Equal(R.Opaque, index.Occurrences.find(x => x.Record?.Name === '$CUSTOM').Role);
  Equal(R.Arbitrary, index.Occurrences.find(x => x.Record?.Name === '$CUSTOMARB').Role);
  Equal(R.HeaderReference, index.Occurrences.find(x => x.Record?.Name === '$CMATERIAL').Role);
  Equal(R.XData, index.Occurrences.find(x => x.Context === 'XREC_APP').Role);
}
