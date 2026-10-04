// Port of both original registration groups in pinned EmptyHatchTests.cs.
import { DxfDocument, DxfRawDocument, DxfTag, MemoryStream, HatchPattern, HatchGradientPattern, Hatch, HatchBoundaryPath, Circle, Block, Insert, Layout, Vector2, Vector3 } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { HatchPathCountTags } from './HatchPathCountTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single = items => { const values = Array.from(items); Equal(1, values.length, 'Expected one item'); return values[0]; };
export function RegisterEmptyHatchTests() { RegisterEmptyHatchReadTests(); RegisterEmptyHatchExportTests(); }
export function RegisterEmptyHatchReadTests() {
  for (const version of SupportedVersions) for (const binary of [false, true]) for (const absent of [false, true]) for (const associative of [false, true])
    Run(`hatch/empty/read/${VersionName(version)}/${BooleanName(binary)}/${BooleanName(absent)}/${BooleanName(associative)}`, () => EmptyHatchRead(version, binary, absent, associative));
}
export function RegisterEmptyHatchExportTests() {
  for (const version of SupportedVersions) for (const binary of [false, true]) for (let placement = 0; placement < 4; placement++) for (let pattern = 0; pattern < 3; pattern++)
    Run(`hatch/empty/preflight/${VersionName(version)}/${BooleanName(binary)}/${placement}/${pattern}`, () => EmptyHatchPreflight(version, binary, placement, pattern));
}
export function EmptyHatchRead(version, binary, absent, associative) {
  let tags = HatchPathCountTags(version, 0);
  if (absent) tags = tags.filter(tag => tag.Code !== 91);
  tags[tags.findIndex(tag => tag.Code === 71)] = new DxfTag(71, associative ? 1 : 0);
  const input = new MemoryStream(RawFixtureBytes(tags, binary));
  try {
    const doc = DxfDocument.Load(input); Check(doc !== null, 'Empty metadata fixture rejected');
    Equal(1, Array.from(doc.Entities.Hatches).length, 'Empty HATCH disappeared'); const hatch = single(doc.Entities.Hatches);
    Equal(0, hatch.BoundaryPaths.Count, 'Empty HATCH invented geometry'); Equal(associative, hatch.Associative, 'Association metadata lost');
    Equal(2.5, hatch.Elevation, 'Empty elevation lost'); Check(new Vector2(2, 3).Equals(single(hatch.SeedPoints)), 'Empty seed lost');
    Equal('after pattern', single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value, 'Empty XData lost');
    Check(hatch.Handle !== null && hatch === doc.GetObjectByHandle(hatch.Handle), 'Empty identity not registered');
    const copy = hatch.Clone(); Equal(0, copy.BoundaryPaths.Count, 'Empty clone invented geometry');
    copy.SeedPoints.Clear(); Equal(1, hatch.SeedPoints.Count, 'Empty clone aliases metadata');
    Check(new Vector3(20, 30, 40).Equals(single(doc.Entities.Lines).StartPoint), 'Empty entity consumed following line');
    input.Position = 0; const raw = DxfRawDocument.Load(input), unchanged = new MemoryStream();
    try { raw.Save(unchanged); Equal(input.ToArray(), unchanged.ToArray(), 'Raw empty-HATCH preservation changed'); }
    finally { unchanged.Dispose(); }
    const boundary = new HatchBoundaryPath([new Circle(Vector3.Zero, 3)]); hatch.BoundaryPaths.Add(boundary);
    const output = new MemoryStream();
    try {
      Check(doc.Save(output, !binary), 'Repaired empty HATCH cannot export'); output.Position = 0;
      const loaded = DxfDocument.Load(output); Check(loaded !== null, 'Repaired reload failed');
      Equal(1, single(loaded.Entities.Hatches).BoundaryPaths.Count, 'Repaired geometry lost');
    } finally { output.Dispose(); }
  } finally { input.Dispose(); }
}
export function EmptyHatchPreflight(version, binary, placement, pattern) {
  const fill = pattern === 0 ? HatchPattern.Solid : pattern === 1 ? HatchPattern.Line : new HatchGradientPattern();
  const hatch = new Hatch(fill, false), doc = new DxfDocument(version);
  switch (placement) {
    case 0: doc.Entities.Add(hatch); break;
    case 1: doc.Layouts.Add(new Layout('EmptyPaper')); doc.Entities.ActiveLayout = 'EmptyPaper'; doc.Entities.Add(hatch); doc.Entities.ActiveLayout = 'Model'; break;
    case 2: {
      const inner = new Block('EmptyInner'); inner.Entities.Add(hatch);
      const outer = new Block('EmptyOuter'); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break;
    }
    default: { const unused = new Block('EmptyUnused'); unused.Entities.Add(hatch); doc.Blocks.Add(unused); break; }
  }
  const output = new MemoryStream(), original = new Uint8Array([1, 2, 3, 4, 5]); output.Write(original); output.Position = 2;
  const seed = doc.DrawingVariables.HandleSeed, handle = hatch.Handle, apps = doc.ApplicationRegistries.Count, layouts = doc.Layouts.Count;
  try {
    if (GetTypedIOConfiguration() === 'Debug') {
      let error;
      try { doc.Save(output, binary); } catch (value) { error = value; }
      Check(error instanceof InvalidDataException, 'Empty HATCH silently exported or wrong exception.');
      Check(error.message.includes('HATCH') && error.message.includes('boundary'), 'Empty preflight lacks context');
    } else Check(!doc.Save(output, binary), 'Empty HATCH export falsely reported success');
    Equal(original, output.ToArray(), 'Empty preflight changed stream bytes'); Equal(2, output.Position, 'Empty preflight advanced stream');
    Equal(seed, doc.DrawingVariables.HandleSeed, 'Empty preflight allocated handles'); Equal(handle, hatch.Handle, 'Empty preflight changed identity');
    Equal(apps, doc.ApplicationRegistries.Count, 'Empty preflight registered APPIDs'); Equal(layouts, doc.Layouts.Count, 'Empty preflight created layouts');
  } finally { output.Dispose(); }
}
