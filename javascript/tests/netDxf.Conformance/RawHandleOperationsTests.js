// Port of tests/netDxf.Conformance/RawHandleOperationsTests.cs; typed controls remain unported.
import { DxfTag, DxfRawDocument, DxfVersion, DxfRawHandleIndex, DxfRawHandleRole as R,
  DxfRawReferenceTraversal as F, DxfRawHandleIndexOptions } from '../../index.js';
import * as E from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, VersionName, BooleanName } from './TestHarness.js';
import { SaveRaw, LoadRaw, SameRawTags } from './RawDocumentTests.js';
import { HandleProfiles, HandleProfileName, HandleFixture, HandleRoundTrip } from './RawHandleIndexTests.js';
const T = (code, value) => new DxfTag(code, value);
export function RegisterRawHandleOperationsTests() {
  for (const v of HandleProfiles) for (const b of [false, true]) {
    Run(`handles/remap/permutation/${VersionName(v)}/${BooleanName(b)}`, () => HandlePermutation(v, b));
    Run(`handles/remap/fresh-target/${VersionName(v)}/${BooleanName(b)}`, () => HandleFreshTarget(v, b));
    Run(`handles/closure/${VersionName(v)}/${BooleanName(b)}`, () => HandleClosure(v, b));
  }
  for (let f = 0; f < 12; f++) Run(`handles/remap/reject/${f}`, () => HandleRemapInvalid(f));
  Run('handles/operations/cancel-stale-and-budget', HandleOperationGuards);
  Run('handles/operations/closure-evidence', HandleClosureEvidence);
}
export function HandleOperationFixture(version) {
  return [T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,HandleProfileName(version)),T(9,'$DWGCODEPAGE'),T(3,'ANSI_1252'),
    T(9,'$HANDSEED'),T(5,'31'),T(0,'ENDSEC'),T(0,'SECTION'),T(2,'OBJECTS'),
    T(0,'DICTIONARY'),T(5,'10'),T(330,'0'),T(100,'AcDbDictionary'),T(3,'Hard'),T(360,'20'),T(3,'Soft'),T(350,'30'),
    T(0,'XRECORD'),T(5,'20'),T(330,'10'),T(100,'AcDbXrecord'),T(280,1),T(1,'literal 10 is application text'),
    T(0,'XRECORD'),T(5,'30'),T(330,'10'),T(100,'AcDbXrecord'),T(280,1),T(1,'data'),T(0,'ENDSEC'),
    T(0,'SECTION'),T(2,'ENTITIES'),T(0,'LINE'),T(5,'A'),T(102,'{ACAD_REACTORS'),T(330,'30'),T(102,'}'),
    T(330,'10'),T(100,'AcDbEntity'),T(100,'AcDbLine'),T(10,1),T(20,2),T(30,3),T(11,4),T(21,5),T(31,6),
    T(340,'20'),T(330,'99'),T(320,'10'),T(1001,'REFS'),T(1005,'30'),T(0,'ENDSEC'),T(0,'EOF')];
}
export function HandlePermutation(v, b) {
  const source = HandleRoundTrip(HandleOperationFixture(v), b), index = DxfRawHandleIndex.Create(source), original = SaveRaw(source);
  const edited = index.RemapHandles(new Map([['0010','20'],['20','10'],['30','A'],['a','30']]));
  const next = DxfRawHandleIndex.Create(edited);
  Equal('DICTIONARY', next.FindDefinitions('20')[0].Record.Name); Equal('LINE', next.FindDefinitions('30')[0].Record.Name);
  Equal(source.Tags.Count, edited.Tags.Count);
  const changes = { '10':'20', '20':'10', '30':'A', 'A':'30' };
  for (const item of index.Occurrences) {
    const expected = item.Role === R.Identity || item.IsReference ? changes[item.CanonicalHandle] ?? item.Handle : item.Handle;
    Equal(expected, edited.Tags[item.TagIndex].Value);
  }
  Check(source.HasOriginalBytes && !edited.HasOriginalBytes); Equal(original, SaveRaw(source));
  SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited, !b)).Tags);
  Check(next.RemapHandles(new Map([['20','0020']])) === edited);
  Throws(E.ArgumentException, () => next.GetOccurrences(index.FindDefinitions('10')[0].Record));
}
export function HandleFreshTarget(v, b) {
  const source = HandleRoundTrip(HandleOperationFixture(v), b), index = DxfRawHandleIndex.Create(source);
  const edited = index.RemapHandles(new Map([['10','1000']])), next = DxfRawHandleIndex.Create(edited);
  Equal('1001', next.Occurrences.find(x => x.Role === R.HeaderSeed).CanonicalHandle);
  Equal(0, next.FindDefinitions('10').Count); Equal(1, next.FindDefinitions('1000').Count);
  const changed = new Set(index.Occurrences.filter(x =>
    (x.CanonicalHandle === '10' && (x.IsReference || x.Role === R.Identity)) || x.Role === R.HeaderSeed).map(x => x.TagIndex));
  for (let i = 0; i < source.Tags.Count; i++) if (!changed.has(i)) Check(source.Tags[i] === edited.Tags[i]);
  Equal('10', next.Occurrences.find(x => x.Role === R.Arbitrary).Handle);
}
export function HandleClosure(v, b) {
  const index = DxfRawHandleIndex.Create(HandleRoundTrip(HandleOperationFixture(v), b)), root = index.FindDefinitions('10')[0].Record;
  const closure = index.GetDependencyClosure([root, root]);
  Equal(2, closure.Records.Count); Check(closure.AreSelectedReferencesResolved);
  Equal(3, index.GetDependencyClosure([root], F.All).Records.Count);
  const line = index.FindDefinitions('A')[0].Record, all = index.GetDependencyClosure([line], F.All);
  Equal(4, all.Records.Count); Equal(1, all.UnresolvedReferences.Count); Equal('99', all.UnresolvedReferences[0].CanonicalHandle);
  Check(!all.AreSelectedReferencesResolved); Equal(1, index.GetDependencyClosure([line], F.None).Records.Count);
  Throws(TypeError, () => closure.Records.splice(0));
}
export function HandleRemapInvalid(failure) {
  const tags = HandleOperationFixture(DxfVersion.AutoCad2018); let map = new Map([['10','100']]);
  switch (failure) {
    case 0: map = new Map([['10','30']]); break;
    case 1: map = new Map([['10','99']]); break;
    case 2: map = new Map([['10','100'],['20','100']]); break;
    case 3: map = new Map([['10','100'],['0010','200']]); break;
    case 4: map = new Map([['0','100']]); break;
    case 5: map = new Map([['10','0']]); break;
    case 6: map = new Map([['99','100']]); break;
    case 7: tags.splice(tags.findIndex(t => t.Code === 1 && t.Value === 'data'), 0, T(330,'10')); break;
    case 8: tags.splice(tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbEntity'), 0, T(102,'{VENDOR'),T(340,'100'),T(102,'}')); break;
    case 9: map = new Map([['10','FFFFFFFFFFFFFFFF']]); break;
    case 10: tags.splice(tags.findIndex(t => t.Code === 0 && t.Value === 'LINE'), 0, T(0,'POINT'),T(5,'30')); break;
    case 11: tags.splice(tags.findIndex(t => t.Code === 9 && t.Value === '$HANDSEED'), 0, T(9,'$HANDSEED'),T(5,'31')); break;
  }
  const source = DxfRawDocument.Create(tags), index = DxfRawHandleIndex.Create(source);
  Throws(failure <= 6 ? E.ArgumentException : failure === 9 ? E.OverflowException : E.InvalidOperationException, () => index.RemapHandles(map));
  for (let i = 0; i < tags.length; i++) Check(tags[i] === source.Tags[i]);
}
export function HandleOperationGuards() {
  const source = DxfRawDocument.Create(HandleOperationFixture(DxfVersion.AutoCad2018)), index = DxfRawHandleIndex.Create(source);
  const cancelled = { aborted: true };
  Throws(E.OperationCanceledException, () => index.RemapHandles(new Map([['10','100']]), cancelled));
  Throws(E.OperationCanceledException, () => index.GetDependencyClosure([], undefined, cancelled));
  Throws(E.ArgumentNullException, () => index.RemapHandles(null)); Throws(E.ArgumentNullException, () => index.GetDependencyClosure(null));
  Throws(E.ArgumentOutOfRangeException, () => index.GetDependencyClosure([], 256));
  const foreign = source.WithTags(source.Tags).Sections.flatMap(s => s.Records)[0];
  Throws(E.ArgumentException, () => index.GetDependencyClosure([foreign]));
  let disposed = false;
  function* Endless() { try { while (true) yield index.FindDefinitions('10')[0].Record; } finally { disposed = true; } }
  const limited = DxfRawHandleIndex.Create(source, new DxfRawHandleIndexOptions(100, 100));
  Throws(E.InvalidDataException, () => limited.GetDependencyClosure(Endless())); Check(disposed);
  Check(source === index.RemapHandles(new Map()));
}
export function HandleClosureEvidence() {
  const source = DxfRawDocument.Create(HandleFixture(DxfVersion.AutoCad2018)), index = DxfRawHandleIndex.Create(source);
  const closure = index.GetDependencyClosure([index.FindDefinitions('AB')[0].Record], F.All); Check(closure.UninterpretedHandles.Count > 0);
  const tags = HandleOperationFixture(DxfVersion.AutoCad2018), at = tags.findIndex(t => t.Code === 0 && t.Value === 'LINE');
  tags.splice(at, 0, T(0,'POINT'),T(5,'20'));
  const ambiguous = DxfRawHandleIndex.Create(DxfRawDocument.Create(tags));
  const result = ambiguous.GetDependencyClosure([ambiguous.FindDefinitions('10')[0].Record]);
  Equal(1, result.AmbiguousReferences.Count); Equal(1, result.Records.Count);
}
