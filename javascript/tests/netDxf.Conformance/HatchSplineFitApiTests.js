// Port of pinned HatchSplineFitApiTests.cs; original identities and assertions retained.
import { Run, Check, Equal, Throws, Near, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { DxfDocument, DxfVersion, Hatch, HatchPattern, HatchBoundaryPath, Spline, Block, Insert, Layout, MemoryStream, Vector2, Vector3, Matrix3, MathHelper } from '../../index.js';
import { ArgumentOutOfRangeException, NotSupportedException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { HatchSplineFitTags, HatchFitValues, AssertHatchFitEntity } from './HatchSplineFitTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
const single = items => { const a = Array.from(items); Equal(1, a.length, 'Expected one item'); return a[0]; };
const multiply = (a, b) => Matrix3.Multiply(a, b);
export function RegisterHatchSplineFitApiTests() {
  Run('hatch/spline-fit/api/default-and-validation', HatchSplineFitApiValidation);
  Run('hatch/spline-fit/api/edit-clone-convert', HatchSplineFitApiEdit);
  Run('hatch/spline-fit/api/insert', HatchSplineFitInsert);
  for (const normal of [Vector3.UnitZ, Vector3.Negate(Vector3.UnitZ), new Vector3(1, 2, 3)]) for (let transform = 0; transform < 4; transform++)
    Run(`hatch/spline-fit/api/transform/${normal.ToString()}/${transform}`, () => HatchSplineFitTransform(normal, transform));
  for (const v of SupportedVersions) for (const b of [false, true]) for (let p = 0; p < 4; p++) for (let m = 0; m < 3; m++)
    Run(`hatch/spline-fit/api/export-profile/${VersionName(v)}/${BooleanName(b)}/${p}/${m}`, () => HatchSplineFitExportProfile(v, b, p, m));
}
export function NewHatchSplineFit() {
  const input = new MemoryStream(RawFixtureBytes(HatchSplineFitTags(DxfVersion.AutoCad2018, 3, 3, false), false));
  try { const doc = DxfDocument.Load(input); Check(doc !== null, 'Fit API fixture rejected.'); return single(doc.Entities.Hatches); } finally { input.Dispose(); }
}
export function HatchFitEdge(hatch) { return single(Array.from(single(hatch.BoundaryPaths).Edges).filter(e => e instanceof HatchBoundaryPath.Spline)); }
export function HatchSplineFitApiValidation() {
  const empty = new HatchBoundaryPath.Spline(); Equal(0, empty.FitPoints.Count, 'New fit collection not empty'); Check(empty.StartTangent === null && empty.EndTangent === null, 'New spline invented tangents.');
  const edge = HatchFitEdge(NewHatchSplineFit());
  for (const bad of [NaN, Infinity, -Infinity]) for (const value of [new Vector2(bad, 0), new Vector2(0, bad)]) {
    Throws(ArgumentOutOfRangeException, () => edge.FitPoints.Add(value));
    Throws(ArgumentOutOfRangeException, () => edge.FitPoints.Insert(0, value));
    Throws(ArgumentOutOfRangeException, () => edge.FitPoints.set_Item(0, value));
    // Non-generic IList.Add shares the checked collection entry in this port.
    Throws(ArgumentOutOfRangeException, () => edge.FitPoints.Add(value));
    Throws(ArgumentOutOfRangeException, () => { edge.StartTangent = value; });
    Throws(ArgumentOutOfRangeException, () => { edge.EndTangent = value; });
    AssertHatchFitEntity(edge.ConvertTo(), 3, 3);
  }
  edge.StartTangent = Vector2.Zero; edge.EndTangent = null;
  Equal(Vector2.Zero, edge.StartTangent, 'Zero tangent normalized away'); Check(edge.EndTangent === null, 'Cleared tangent retained.');
}
export function HatchSplineFitApiEdit() {
  const hatch = NewHatchSplineFit(), edge = HatchFitEdge(hatch), knots = Array.from(edge.Knots), controls = Array.from(edge.ControlPoints);
  edge.FitPoints.Insert(1, new Vector2(7, 8)); edge.FitPoints.Add(new Vector2(7, 8)); edge.FitPoints.set_Item(0, new Vector2(9, 10)); edge.FitPoints.RemoveAt(2);
  const expected = [new Vector2(9, 10), new Vector2(7, 8), new Vector2(10, 0), new Vector2(7, 8)];
  Equal(expected, Array.from(edge.FitPoints), 'Fit edits lost order/duplicates.'); Equal(knots, edge.Knots, 'Fit edits refitted the knots.'); Equal(controls, edge.ControlPoints, 'Fit edits refitted the controls.');
  const copied = HatchFitEdge(hatch.Clone()); Check(edge.FitPoints !== copied.FitPoints, 'Fit clone aliases source collection.');
  copied.FitPoints.Clear(); copied.StartTangent = null; copied.EndTangent = new Vector2(1, 2);
  Equal(expected, Array.from(edge.FitPoints), 'Clone edit changed source fits.'); Equal(new Vector2(10, 20), edge.StartTangent, 'Clone edit changed source tangent');
  const converted = HatchBoundaryPath.Spline.ConvertFrom(edge.ConvertTo());
  Equal(expected, Array.from(converted.FitPoints), 'Conversion regenerated fit metadata.'); Equal(knots, converted.Knots, 'Conversion regenerated knots.'); Equal(controls, converted.ControlPoints, 'Conversion regenerated controls.');
  Equal(edge.StartTangent, converted.StartTangent, 'Converted start tangent'); Equal(edge.EndTangent, converted.EndTangent, 'Converted end tangent');
  converted.FitPoints.Clear(); Equal(expected, Array.from(edge.FitPoints), 'Converted collection aliases source.');
  const authored = new Spline([new Vector3(0, 0, 0), new Vector3(1, 2, 0), new Vector3(4, 0, 0)]);
  authored.StartTangent = new Vector3(3, 4, 0); authored.EndTangent = new Vector3(5, 6, 0);
  const authoredEdge = new HatchBoundaryPath.Spline(authored);
  Equal(Array.from(authored.FitPoints, p => new Vector2(p.X, p.Y)), Array.from(authoredEdge.FitPoints), 'Fit-authored entity conversion lost fits.'); Equal(new Vector2(3, 4), authoredEdge.StartTangent, 'Fit-authored entity lost tangent');
}
export function FitWorld(hatch, point, vector) { return multiply(MathHelper.ArbitraryAxis(hatch.Normal), new Vector3(point.X, point.Y, vector ? 0 : hatch.Elevation)); }
export function NearFitVector(expected, actual, what) { Near(expected.X, actual.X, what + ' X'); Near(expected.Y, actual.Y, what + ' Y'); Near(expected.Z, actual.Z, what + ' Z'); }
export function CheckWorldFitData(hatch, fit, start, end) {
  const edge = HatchFitEdge(hatch); Equal(fit.length, edge.FitPoints.Count, 'Transformed fit count');
  for (let i = 0; i < fit.length; i++) NearFitVector(fit[i], FitWorld(hatch, edge.FitPoints.get_Item(i), false), 'Transformed fit');
  Check(edge.StartTangent !== null && edge.EndTangent !== null, 'Transform dropped tangents.');
  NearFitVector(start, FitWorld(hatch, edge.StartTangent, true), 'Transformed start tangent'); NearFitVector(end, FitWorld(hatch, edge.EndTangent, true), 'Transformed end tangent');
  const boundary = single(Array.from(hatch.CreateBoundary(false)).filter(e => e instanceof Spline)); Equal(fit.length, boundary.FitPoints.Count, 'CreateBoundary dropped fits');
  for (let i = 0; i < fit.length; i++) NearFitVector(fit[i], boundary.FitPoints.get_Item(i), 'Boundary world fit');
  NearFitVector(start, boundary.StartTangent, 'Boundary world start'); NearFitVector(end, boundary.EndTangent, 'Boundary world end');
}
export function HatchSplineFitTransform(normal, operation) {
  const hatch = NewHatchSplineFit(); hatch.Normal = normal; const edge = HatchFitEdge(hatch);
  const matrix = operation === 0 ? Matrix3.Identity : operation === 1 ? Matrix3.RotationZ(.7) : operation === 2 ? multiply(multiply(Matrix3.RotationX(.7), Matrix3.RotationY(-.3)), Matrix3.Scale(2)) : Matrix3.Reflection(Vector3.UnitX);
  const translation = new Vector3(17, -23, 31), originalFit = Array.from(edge.FitPoints, p => FitWorld(hatch, p, false)), start = FitWorld(hatch, edge.StartTangent, true), end = FitWorld(hatch, edge.EndTangent, true);
  CheckWorldFitData(hatch, originalFit, start, end); hatch.TransformBy(matrix, translation);
  CheckWorldFitData(hatch, originalFit.map(p => Vector3.Add(multiply(matrix, p), translation)), multiply(matrix, start), multiply(matrix, end));
  Equal(HatchFitValues, Array.from(edge.FitPoints), 'Transform changed a detached original edge.');
}
export function HatchSplineFitInsert() {
  const hatch = NewHatchSplineFit(); hatch.Pattern = HatchPattern.Solid;
  const block = new Block('SplineFitBlock'); block.Entities.Add(hatch.Clone());
  const insert = new Insert(block, new Vector3(10, 20, 30)); insert.Scale = new Vector3(2, 3, 1); insert.Rotation = 30;
  const copy = insert.Clone(); HatchFitEdge(single(Array.from(copy.Block.Entities).filter(e => e instanceof Hatch))).FitPoints.Clear();
  Equal(HatchFitValues, Array.from(HatchFitEdge(single(Array.from(block.Entities).filter(e => e instanceof Hatch))).FitPoints), 'INSERT clone aliases fits.');
  const matrix = multiply(Matrix3.RotationZ(Math.PI / 6), Matrix3.Scale(2, 3, 1)), edge = HatchFitEdge(hatch), expected = Array.from(edge.FitPoints, p => Vector3.Add(multiply(matrix, FitWorld(hatch, p, false)), insert.Position));
  const start = multiply(matrix, FitWorld(hatch, edge.StartTangent, true)), end = multiply(matrix, FitWorld(hatch, edge.EndTangent, true)), exploded = single(Array.from(insert.Explode()).filter(e => e instanceof Hatch));
  CheckWorldFitData(exploded, expected, start, end); HatchFitEdge(exploded).FitPoints.Clear(); Equal(HatchFitValues, Array.from(HatchFitEdge(hatch).FitPoints), 'Explosion aliases source.');
}
export function HatchSplineFitExportProfile(version, binary, placement, metadata) {
  const hatch = NewHatchSplineFit().Clone(), edge = HatchFitEdge(hatch);
  if (metadata === 0) { edge.StartTangent = null; edge.EndTangent = null; }
  else { edge.FitPoints.Clear(); if (metadata === 1) edge.EndTangent = null; else edge.StartTangent = null; }
  const doc = new DxfDocument(version);
  switch (placement) {
    case 0: doc.Entities.Add(hatch); break;
    case 1: doc.Layouts.Add(new Layout('FitPaper')); doc.Entities.ActiveLayout = 'FitPaper'; doc.Entities.Add(hatch); doc.Entities.ActiveLayout = 'Model'; break;
    case 2: { const inner = new Block('FitInner'); inner.Entities.Add(hatch); const outer = new Block('FitOuter'); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break; }
    default: { const unused = new Block('FitUnused'); unused.Entities.Add(hatch); doc.Blocks.Add(unused); break; }
  }
  const output = new MemoryStream(), original = new Uint8Array([11, 22, 33, 44]); output.Write(original); output.Position = 2;
  const handles = doc.DrawingVariables.HandleSeed, identity = hatch.Handle, apps = doc.ApplicationRegistries.Count, layouts = doc.Layouts.Count;
  try {
    if (version < DxfVersion.AutoCad2010) {
      if (GetTypedIOConfiguration() === 'Debug') { let failure; try { doc.Save(output, binary); } catch (error) { failure = error; } Check(failure instanceof NotSupportedException, 'Lossy legacy fit export accepted or wrong error.'); Check(failure.message.includes('HATCH') && failure.message.includes('2010'), 'Missing fit export-profile diagnostic.'); }
      else Check(!doc.Save(output, binary), 'Lossy legacy fit export accepted.');
      Equal(original, output.ToArray(), 'Fit preflight modified destination bytes.'); Equal(2, output.Position, 'Fit preflight advanced destination'); Equal(handles, doc.DrawingVariables.HandleSeed, 'Fit preflight allocated handles'); Equal(identity, hatch.Handle, 'Fit preflight changed entity identity'); Equal(apps, doc.ApplicationRegistries.Count, 'Fit preflight registered APPIDs'); Equal(layouts, doc.Layouts.Count, 'Fit preflight created a layout');
    } else {
      output.SetLength(0); output.Position = 0; Check(doc.Save(output, binary), 'Supported fit export rejected.'); output.Position = 0;
      const loaded = DxfDocument.Load(output); Check(loaded !== null, 'Supported fit export invalid.');
      const actual = HatchFitEdge(single(Array.from(loaded.Blocks).flatMap(b => Array.from(b.Entities)).filter(e => e instanceof Hatch)));
      Equal(Array.from(edge.FitPoints), Array.from(actual.FitPoints), 'Profile export changed fits.'); Equal(edge.StartTangent, actual.StartTangent, 'Profile start tangent'); Equal(edge.EndTangent, actual.EndTangent, 'Profile end tangent');
    }
  } finally { output.Dispose(); }
}
