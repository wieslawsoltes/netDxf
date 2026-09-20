import test from 'node:test';
import assert from 'node:assert/strict';
import { Hatch, HatchBoundaryPath as H, HatchPattern, HatchGradientPattern, Circle, Line, Spline, Vector2, Vector3, Matrix3, Matrix4 } from '../../index.js';
import { ArgumentException, NotSupportedException } from '../../runtime/Errors.js';
import { hatchEntityCorpus } from '../../tools/hatch-entity-corpus.mjs';
import { jsGeometry } from '../../tools/foundations-wire.mjs';
import { D, I, A, V } from '../../tools/geometry-corpus.mjs';
const linePath = () => new H([new Line(Vector3.Zero, Vector3.UnitX)]);
const vectors = list => Array.from(list, v => [v.X, v.Y, v.Z]);

test('HATCH successful Matrix4 identity unlinks without replacing the original edge, path, pattern or seed list', () => {
  const p = linePath(), source = p.Entities.get_Item(0), h = new Hatch(HatchPattern.Line, [p], true);
  const edge = p.Edges.get_Item(0), pattern = h.Pattern, seeds = h.SeedPoints;
  const events = [];
  h.HatchBoundaryPathAdded.Add(() => events.push('add'));
  h.HatchBoundaryPathRemoved.Add(() => events.push('remove'));
  h.TransformBy(Matrix4.Identity);
  assert.equal(h.Associative, false); assert.equal(p.Entities.Count, 0);
  assert.equal(p.Edges.get_Item(0), edge); assert.equal(h.BoundaryPaths.get_Item(0), p);
  assert.equal(h.Pattern, pattern); assert.equal(h.SeedPoints, seeds);
  assert.equal(source.Reactors.Count, 0); assert.deepEqual(events, []);
});

test('HATCH invalid identity geometry fails before unlinking, clearing contours or raising boundary events', () => {
  const p = linePath(), source = p.Entities.get_Item(0), h = new Hatch(HatchPattern.Solid, [p], true);
  const edge = p.Edges.get_Item(0), events = [];
  h.HatchBoundaryPathRemoved.Add(() => events.push('remove'));
  edge.End = new Vector2(NaN, 2);
  assert.throws(() => h.TransformBy(Matrix3.Identity, Vector3.Zero), ArgumentException);
  assert.equal(h.Associative, true); assert.equal(p.Entities.get_Item(0), source);
  assert.ok(Array.from(source.Reactors).includes(h)); assert.deepEqual(events, []);
  assert.equal(h.BoundaryPaths.get_Item(0), p); assert.ok(Number.isNaN(edge.End.X));
});

test('HATCH source ownership and shared reactor occurrences are released only after the final path', () => {
  const source = new Line(Vector3.Zero, Vector3.UnitX), a = new H([source]), b = new H([source]);
  const h = new Hatch(HatchPattern.Solid, [a, b], true);
  assert.equal(Array.from(source.Reactors).filter(r => r === h).length, 2);
  h.BoundaryPaths.Remove(a);
  assert.equal(a.ContainingHatch, null); assert.equal(b.ContainingHatch, h);
  assert.equal(Array.from(source.Reactors).filter(r => r === h).length, 1);
  h.BoundaryPaths.Remove(b); assert.equal(source.Reactors.Count, 0);
});

test('HATCH path cancellation leaves candidates detached and retains all existing source links', () => {
  const a = linePath(), b = linePath(), h = new Hatch(HatchPattern.Solid, [a], true);
  h.BoundaryPaths.BeforeAddItem.Add((_, e) => { e.Cancel = true; });
  assert.throws(() => h.BoundaryPaths.Add(b), ArgumentException);
  assert.equal(h.BoundaryPaths.Count, 1); assert.equal(b.ContainingHatch, null);
  assert.equal(a.Entities.get_Item(0).Reactors.Count, 1); assert.equal(b.Entities.get_Item(0).Reactors.Count, 0);
});

test('HATCH boundary reconstruction preflights every conversion before unlinking existing sources', () => {
  const p = linePath(), source = p.Entities.get_Item(0), h = new Hatch(HatchPattern.Solid, [p], true);
  const invalid = new H.Spline(); h.BoundaryPaths.Add(H.FromEdges([invalid]));
  assert.throws(() => h.CreateBoundary(true));
  assert.equal(h.Associative, true); assert.equal(p.Entities.get_Item(0), source);
  assert.ok(Array.from(source.Reactors).includes(h));
});

test('nonuniform solid HATCH promotes a circular edge without transforming its former source', () => {
  const source = new Circle(new Vector3(2, 3, 0), 4), path = new H([source]);
  const h = new Hatch(HatchPattern.Solid, [path], true); h.PixelSize = null;
  h.TransformBy(Matrix3.Scale(2, 3, 1), new Vector3(7, 11, 0));
  assert.ok(h.BoundaryPaths.get_Item(0).Edges.get_Item(0) instanceof H.Ellipse);
  assert.equal(h.Associative, false); assert.equal(source.Radius, 4);
  assert.deepEqual([source.Center.X, source.Center.Y], [2, 3]); assert.equal(source.Reactors.Count, 0);
  assert.equal(h.PixelSize, null); assert.notEqual(h.BoundaryPaths.get_Item(0), path);
});

test('gradient HATCH rejects nonuniform transforms atomically, including seeds and source reactors', () => {
  const path = linePath(), source = path.Entities.get_Item(0), h = new Hatch(new HatchGradientPattern(), [path], true);
  const pattern = h.Pattern, seeds = vectors(h.SeedPoints);
  assert.throws(() => h.TransformBy(Matrix3.Scale(2, 3, 1), Vector3.Zero), NotSupportedException);
  assert.equal(h.Pattern, pattern); assert.deepEqual(vectors(h.SeedPoints), seeds);
  assert.equal(h.BoundaryPaths.get_Item(0), path); assert.equal(h.Associative, true);
  assert.equal(path.Entities.get_Item(0), source); assert.equal(source.Reactors.Count, 1);
});

test('HATCH seed OCS placement follows the world-space affine map', () => {
  const h = new Hatch(HatchPattern.Solid, [linePath()], false);
  h.Elevation = 4; h.SeedPoints.Clear(); h.SeedPoints.Add(new Vector2(2, -3));
  h.TransformBy(Matrix3.Scale(2, 3, 1), new Vector3(7, 11, 13));
  assert.equal(h.Elevation, 17);
  assert.deepEqual([h.SeedPoints.get_Item(0).X, h.SeedPoints.get_Item(0).Y], [11, 2]);
});

test('periodic boundary conversion retains compact control data and clones fit metadata independently', () => {
  const source = new Spline([Vector3.Zero, new Vector3(1, 2, 0), new Vector3(3, 2, 0), Vector3.UnitX], [1, 1, 1, 1], 2, true);
  const edge = new H.Spline(source), clone = edge.Clone();
  edge.FitPoints.Add(new Vector2(4, 5)); edge.StartTangent = new Vector2(6, 7);
  assert.equal(clone.FitPoints.Count, 0); assert.equal(clone.StartTangent, null);
  const result = edge.ConvertTo(); assert.equal(result.ControlPoints.length, source.ControlPoints.length);
  assert.deepEqual(vectors(result.ControlPoints), vectors(source.ControlPoints));
  assert.notEqual(result.Knots, edge.Knots); assert.equal(result.IsClosedPeriodic, true);
});

test('HATCH differential inputs are unique, deterministic and require the complete 1075-scenario set', () => {
  const corpus = hatchEntityCorpus();
  assert.deepEqual(corpus, hatchEntityCorpus());
  assert.equal(new Set(corpus.map(p => p.name)).size, 1075);
  assert.equal(corpus.reduce((n, p) => n + p.request.steps.length, 0), 20990);
  assert.deepEqual([...new Set(corpus.map(p => p.category))].sort(), ['hatch-affine', 'hatch-association', 'hatch-boundary', 'hatch-events', 'hatch-model', 'hatch-spline']);
  for (const probe of corpus) assert.ok(!Object.hasOwn(probe, 'expected'));
});

test('HATCH reflection resolves nested CLR edge types and explicit enumerable constructor signatures', () => {
  const result = jsGeometry({ steps: [
    { kind: 'new', type: 'Entities.HatchBoundaryPath+Line', args: [], id: 'edge' },
    { kind: 'new', type: 'Entities.HatchBoundaryPath', args: [A('Entities.HatchBoundaryPath+Edge', [{ref:'edge'}])], signature: ['IEnumerable<Entities.HatchBoundaryPath+Edge>'], id: 'path' },
    { kind: 'snapshot', target: 'path' }
  ] });
  assert.ok(result.every(r => r.ok)); assert.equal(result[2].value.type, 'HatchBoundaryPath');
  assert.equal(result[2].value.edges[0].kind, 1); assert.equal(result[2].value.entities.length, 0);
});

test('plain-array reflection indexes reject overflow and copy vector elements on both read and write', () => {
  const result = jsGeometry({ steps: [
    { kind: 'new', type: 'Entities.HatchBoundaryPath+Polyline', args: [], id: 'edge' },
    { kind: 'set', target: 'edge', member: 'Vertexes', value: A('Vector3', [V('Vector3', 1, 2, 3)]) },
    { kind: 'get', target: 'edge', member: 'Vertexes', id: 'array' },
    { kind: 'index', target: 'array', args: [I(0)], id: 'point' },
    { kind: 'set', target: 'point', member: 'X', value: D(9) },
    { kind: 'index', target: 'array', args: [I(0)] },
    { kind: 'set-index', target: 'array', args: [I(0)], value: {ref:'point'} },
    { kind: 'set', target: 'point', member: 'X', value: D(11) },
    { kind: 'index', target: 'array', args: [I(0)] },
    { kind: 'index', target: 'array', args: [I(-1)] },
    { kind: 'set-index', target: 'array', args: [I(1)], value: V('Vector3', 0, 0, 0) },
    { kind: 'snapshot', target: 'array' }
  ] });
  assert.ok(result.slice(0, 9).every(r => r.ok));
  assert.equal(result[5].value.values[0], '3FF0000000000000');
  assert.equal(result[8].value.values[0], '4022000000000000');
  assert.equal(result[9].ok, false); assert.equal(result[10].ok, false);
  assert.deepEqual(result[11].value, [result[8].value]);
});
