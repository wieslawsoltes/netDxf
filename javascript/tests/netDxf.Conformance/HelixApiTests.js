// Port of every original case/assertion in pinned HelixApiTests.cs.
import { DxfDocument, DxfVersion, DxfTag, DxfClass, Helix, HelixConstraint, Spline, Block, Insert, Layout, EntityType, Vector3, Matrix3, MemoryStream } from '../../index.js';
import { ArgumentNullException, ArgumentOutOfRangeException, NotSupportedException, InvalidDataException, EndOfStreamException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, Near, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { NearFitVector } from './HatchSplineFitApiTests.js';
import { HelixWireTags, HelixWireControls } from './HelixWireTests.js';
const single = items => { const a = Array.from(items); Equal(1, a.length, 'Expected one item'); return a[0]; };
const same = (a, b) => { a = Array.from(a); b = Array.from(b); return a.length === b.length && a.every((v, i) => v?.Equals ? v.Equals(b[i]) : v === b[i]); };
const equalVector = (expected, actual, message) => Check(expected.Equals(actual), message);
const multiply = (a, b) => Matrix3.Multiply(a, b);
export function RegisterHelixApiTests() {
  Run('helix/api/defaults-clone-conversion', HelixApiClone); Run('helix/api/finite-validation', HelixApiValidation);
  for (let op = 0; op < 6; op++) Run(`helix/api/transform/${op}`, () => HelixTransform(op));
  for (const v of SupportedVersions) for (const b of [false, true]) for (let p = 0; p < 4; p++) Run(`helix/api/profile/${VersionName(v)}/${BooleanName(b)}/${p}`, () => HelixExportProfile(v, b, p));
  for (const b of [false, true]) {
    for (let f = 0; f < 10; f++) Run(`helix/api/invalid/${BooleanName(b)}/${f}`, () => HelixInvalid(b, f));
    Run(`helix/api/absent-parameters/${BooleanName(b)}`, () => HelixAbsentParameters(b));
    Run(`helix/api/class-conflict/${BooleanName(b)}`, () => HelixClassConflict(b));
    Run(`helix/api/class-preservation/${BooleanName(b)}`, () => HelixClassPreservation(b));
  }
}
export function NewHelixFixture() {
  const input = new MemoryStream(RawFixtureBytes(HelixWireTags(DxfVersion.AutoCad2018, 1, true, false), false));
  try { const doc = DxfDocument.Load(input); Check(doc !== null, 'HELIX fixture rejected.'); return single(doc.Entities.Helices).Clone(); } finally { input.Dispose(); }
}
export function HelixApiClone() {
  Throws(ArgumentNullException, () => new Helix(null));
  const curve = new Spline(HelixWireControls, [1, 1.25, 1.5, 1.75], 3), authored = new Helix(curve);
  Equal(29, authored.MajorReleaseNumber, 'Default major release'); Equal(63, authored.MaintenanceReleaseNumber, 'Default maintenance');
  equalVector(Vector3.Zero, authored.AxisBasePoint, 'Default axis base'); equalVector(Vector3.UnitX, authored.StartPoint, 'Default start'); equalVector(Vector3.UnitZ, authored.AxisVector, 'Default axis');
  Equal(1, authored.Radius, 'Default radius'); Equal(1, authored.Turns, 'Default turns'); Equal(1, authored.TurnHeight, 'Default turn height'); Equal(HelixConstraint.TurnHeight, authored.Constraint, 'Default constraint'); Check(authored.IsRightHanded, 'Default handedness');
  authored.ControlPoints[0] = Vector3.Zero; equalVector(HelixWireControls[0], curve.ControlPoints[0], 'HELIX constructor aliases source controls');
  const source = NewHelixFixture(), copy = source.Clone(), spline = source.ToSpline();
  Equal(EntityType.Helix, copy.Type, 'Clone entity type'); Equal(EntityType.Spline, spline.Type, 'Downgrade entity type'); Equal('SPLINE', spline.CodeName, 'ToSpline retained HELIX identity');
  Check(copy.Handle === null && copy.Owner === null && spline.Handle === null, 'Copy retained database identity.'); Check(same(source.ControlPoints, spline.ControlPoints) && same(source.Weights, spline.Weights), 'ToSpline refitted geometry.');
  equalVector(source.AxisBasePoint, copy.AxisBasePoint, 'Clone axis base'); equalVector(source.AxisVector, copy.AxisVector, 'Clone axis magnitude'); equalVector(source.StartPoint, copy.StartPoint, 'Clone start point'); Equal(source.TurnHeight, copy.TurnHeight, 'Clone signed pitch');
  copy.ControlPoints[0] = Vector3.Zero; copy.Weights[0] = 5; copy.Knots[0] = -1; copy.XData.get_Item('HELIX_TEST').XDataRecord.Clear(); copy.Radius = 19;
  Check(same(source.ControlPoints, HelixWireControls), 'Clone aliases controls.'); Equal(1, source.Weights[0], 'Clone aliases weights'); Equal(0, source.Knots[0], 'Clone aliases knots'); Equal(1, source.XData.get_Item('HELIX_TEST').XDataRecord.Count, 'Clone aliases XData'); Equal(2.75, source.Radius, 'Clone aliases metadata');
  const block = new Block('HelixClone'); block.Entities.Add(source.Clone()); const clonedInsert = new Insert(block).Clone(); single(Array.from(clonedInsert.Block.Entities).filter(e => e instanceof Helix)).ControlPoints[0] = Vector3.Zero;
  equalVector(HelixWireControls[0], single(Array.from(block.Entities).filter(e => e instanceof Helix)).ControlPoints[0], 'Nested clone aliases controls');
  const doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(source.Clone()); Equal(1, Array.from(doc.Entities.Helices).length, 'Typed collection'); Equal(1, Array.from(doc.Entities.Splines).length, 'Inherited spline collection');
  Check(doc.Entities.Remove(single(doc.Entities.Helices)), 'HELIX remove failed.'); Equal(0, Array.from(doc.Entities.All).length, 'HELIX remove left entity');
}
export function HelixApiValidation() {
  const h = NewHelixFixture();
  for (const invalid of [NaN, Infinity, -Infinity]) {
    for (const property of ['Radius', 'Turns', 'TurnHeight']) Throws(ArgumentOutOfRangeException, () => { h[property] = invalid; });
    for (const p of [new Vector3(invalid,0,0), new Vector3(0,invalid,0), new Vector3(0,0,invalid)]) for (const property of ['AxisVector','AxisBasePoint','StartPoint']) Throws(ArgumentOutOfRangeException, () => { h[property] = p; });
  }
  for (const [property, value] of [['AxisVector',Vector3.Zero],['Radius',-1],['Turns',0],['MajorReleaseNumber',-1],['MaintenanceReleaseNumber',-1],['Constraint',3]]) Throws(ArgumentOutOfRangeException, () => { h[property] = value; });
  Equal(2.75, h.Radius, 'Failed setter mutated radius'); Equal(3.125, h.Turns, 'Failed setter mutated turns'); equalVector(new Vector3(0,0,2), h.AxisVector, 'Failed setter mutated axis');
  h.Radius = 0; h.TurnHeight = 0; h.Turns = 1000; h.AxisVector = new Vector3(0,0,9); Equal(9, h.AxisVector.Z, 'Setter normalized magnitude'); Check(same(h.ControlPoints, HelixWireControls), 'Editing parameters refitted control polygon.');
}
export function HelixTransform(operation) {
  const h = NewHelixFixture(), original = h.Clone();
  const matrix = [() => Matrix3.Identity, () => multiply(Matrix3.RotationX(.6), Matrix3.RotationZ(-.4)), () => multiply(Matrix3.RotationY(.3), Matrix3.Scale(2)), () => multiply(Matrix3.Reflection(Vector3.UnitX), Matrix3.Scale(3)), () => Matrix3.Scale(2,3,4), () => Matrix3.Scale(0)][operation](), translation = new Vector3(101,-33,77);
  if (operation >= 4) {
    Throws(NotSupportedException, () => h.TransformBy(matrix, translation)); Check(same(h.ControlPoints, original.ControlPoints), 'Rejected affine transform mutated controls.'); equalVector(original.AxisBasePoint, h.AxisBasePoint, 'Rejected transform mutated parameters');
    const block = new Block('HelixAffine'); block.Entities.Add(h);
    if (operation === 4) { const insert = new Insert(block, translation); insert.Scale = new Vector3(2,3,4); const curve = single(Array.from(insert.Explode()).filter(e => e instanceof Spline)); Check(!(curve instanceof Helix) && curve.CodeName === 'SPLINE', 'Affine explosion retained false HELIX parameters.'); for (let i = 0; i < curve.ControlPoints.length; i++) NearFitVector(Vector3.Add(multiply(matrix, original.ControlPoints[i]), translation), curve.ControlPoints[i], 'Affine exploded curve'); }
    return;
  }
  const scale = operation === 2 ? 2 : operation === 3 ? 3 : 1; h.TransformBy(matrix, translation);
  for (const [key,label] of [['AxisBasePoint','Axis base transform'],['StartPoint','Start point transform']]) NearFitVector(Vector3.Add(multiply(matrix, original[key]), translation), h[key], label);
  NearFitVector(multiply(matrix, original.AxisVector), h.AxisVector, 'Axis vector transform'); for (let i = 0; i < h.ControlPoints.length; i++) NearFitVector(Vector3.Add(multiply(matrix, original.ControlPoints[i]), translation), h.ControlPoints[i], 'Spline control transform');
  NearFitVector(multiply(matrix, original.StartTangent), h.StartTangent, 'Spline tangent transform'); Near(original.Radius * scale, h.Radius, 'Radius scale'); Near(original.TurnHeight * scale, h.TurnHeight, 'Pitch scale'); Equal(operation !== 3, h.IsRightHanded, 'Reflection handedness'); Equal(original.Turns, h.Turns, 'Transform changed turn count');
  const doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(h); const stream = new MemoryStream();
  try { Check(doc.Save(stream), 'Transformed HELIX save failed.'); stream.Position = 0; const loaded = single(DxfDocument.Load(stream).Entities.Helices); NearFitVector(h.Normal, loaded.Normal, 'Inherited HELIX normal'); } finally { stream.Dispose(); }
}
export function HelixExportProfile(version, binary, placement) {
  const doc = new DxfDocument(version), h = NewHelixFixture().Clone();
  switch (placement) {
    case 0: doc.Entities.Add(h); break;
    case 1: doc.Layouts.Add(new Layout('HelixPaper')); doc.Entities.ActiveLayout = 'HelixPaper'; doc.Entities.Add(h); doc.Entities.ActiveLayout = 'Model'; break;
    case 2: { const inner = new Block('HelixInner'); inner.Entities.Add(h); const outer = new Block('HelixOuter'); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break; }
    default: { const unused = new Block('HelixUnused'); unused.Entities.Add(h); doc.Blocks.Add(unused); }
  }
  const stream = new MemoryStream(), bytes = Uint8Array.of(4,5,6); stream.Write(bytes); stream.Position = 1;
  try {
    const handle = h.Handle, seed = doc.DrawingVariables.HandleSeed, apps = doc.ApplicationRegistries.Count, layouts = doc.Layouts.Count;
    if (version < DxfVersion.AutoCad2007) {
      if (GetTypedIOConfiguration() === 'Debug') Throws(NotSupportedException, () => doc.Save(stream, binary)); else Check(!doc.Save(stream, binary), 'Older profile emitted HELIX.');
      Check(same(bytes, stream.ToArray()), 'HELIX preflight wrote bytes.'); Equal(1, stream.Position, 'HELIX preflight advanced stream'); Equal(handle, h.Handle, 'HELIX preflight changed identity'); Equal(seed, doc.DrawingVariables.HandleSeed, 'HELIX preflight allocated handles'); Equal(apps, doc.ApplicationRegistries.Count, 'HELIX preflight registered APPIDs'); Equal(layouts, doc.Layouts.Count, 'HELIX preflight added layout');
    } else {
      stream.SetLength(0); stream.Position = 0; Check(doc.Save(stream, binary), 'Supported HELIX save failed.'); stream.Position = 0; const loaded = DxfDocument.Load(stream); Check(loaded !== null, 'HELIX reload failed.');
      const actual = single(Array.from(loaded.Blocks).flatMap(b => Array.from(b.Entities)).filter(e => e instanceof Helix)); equalVector(h.AxisVector, actual.AxisVector, 'Block HELIX axis'); Check(same(h.ControlPoints, actual.ControlPoints), 'Block HELIX controls'); Equal(1, loaded.Classes.get_Item('HELIX').InstanceCount, 'HELIX CLASS instance count'); Check(!doc.Classes.Contains('HELIX'), 'Writer attached synthesized class to caller collection.');
    }
  } finally { stream.Dispose(); }
}
export function HelixInvalid(binary, failure) {
  const tags = HelixWireTags(DxfVersion.AutoCad2018, 1, true, false), marker = tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbHelix'), find = code => tags.findIndex((t, i) => i > marker && t.Code === code);
  switch (failure) {
    case 0: tags.splice(marker,1); break;
    case 1: tags.splice(find(40),0,new DxfTag(40,1)); break;
    case 2: tags.splice(find(22),1); break;
    case 3: tags[find(32)] = new DxfTag(32,0); break;
    case 4: tags[find(280)] = new DxfTag(280,3); break;
    case 5: tags[find(90)] = new DxfTag(90,-1); break;
    case 6: tags[find(41)] = new DxfTag(41,0); break;
    case 7: tags[find(40)] = new DxfTag(40,-1); break;
    case 8: tags.splice(marker+1,0,new DxfTag(100,'AcDbHelix')); break;
    default: tags.splice(marker);
  }
  const stream = new MemoryStream(RawFixtureBytes(tags,binary));
  try {
    if (GetTypedIOConfiguration() === 'Debug') { let error; try { DxfDocument.Load(stream); } catch (e) { error=e; } Check(error instanceof InvalidDataException || error instanceof EndOfStreamException, 'Invalid HELIX accepted or wrong diagnostic: '+error); }
    else Check(DxfDocument.Load(stream) === null, 'Invalid HELIX accepted.');
    Check(stream.CanRead, 'Malformed HELIX closed caller stream.');
  } finally { stream.Dispose(); }
}
export function HelixAbsentParameters(binary) {
  const tags = HelixWireTags(DxfVersion.AutoCad2018, 1, true, false), marker = tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbHelix'); tags.splice(marker+1,tags.findIndex(t=>t.Code===1001)-marker-1);
  const input = new MemoryStream(RawFixtureBytes(tags,binary));
  try { const h=single(DxfDocument.Load(input).Entities.Helices); Equal(1,h.Radius,'Absent radius default'); equalVector(Vector3.UnitZ,h.AxisVector,'Absent axis default'); Equal('after helix',single(h.XData.get_Item('HELIX_TEST').XDataRecord).Value,'Absent parameter XData'); } finally { input.Dispose(); }
}
export function HelixClassConflict(binary) {
  const doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(NewHelixFixture().Clone()); doc.Classes.Add(new DxfClass('HELIX','WrongClass','Other')); const output=new MemoryStream();
  try { if(GetTypedIOConfiguration()==='Debug')Throws(InvalidDataException,()=>doc.Save(output,binary));else Check(!doc.Save(output,binary),'Conflicting HELIX class accepted.');Equal(0,output.Length,'Class conflict wrote bytes'); } finally { output.Dispose(); }
}
export function HelixClassPreservation(binary) {
  const doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(NewHelixFixture().Clone());doc.Classes.Add(Object.assign(new DxfClass('HELIX','AcDbHelix','Authored application'),{IsEntity:true,ProxyFlags:17,InstanceCount:77}));const output=new MemoryStream();
  try { Check(doc.Save(output,binary),'Compatible HELIX class rejected.');output.Position=0;const definition=DxfDocument.Load(output).Classes.get_Item('HELIX');Equal('Authored application',definition.ApplicationName,'Authored class application');Equal(17,definition.ProxyFlags,'Authored proxy flags');Equal(1,definition.InstanceCount,'Recomputed class count');Equal(77,doc.Classes.get_Item('HELIX').InstanceCount,'Save mutated caller class'); } finally { output.Dispose(); }
}
