import test from 'node:test';
import assert from 'node:assert/strict';
import { Hatch, HatchBoundaryPath, HatchPattern, Circle, Line, Vector2, Vector3, Matrix3 } from '../../index.js';
import { ArgumentException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';

const circlePath = () => new HatchBoundaryPath([new Circle(Vector3.Zero, 2)]);

test('public entry exposes HATCH and all nested boundary edge constructors', () => {
  assert.equal(typeof Hatch, 'function');
  for (const name of ['Polyline', 'Line', 'Arc', 'Ellipse', 'Spline']) {
    assert.equal(typeof HatchBoundaryPath[name], 'function');
    assert.equal(new HatchBoundaryPath[name]().Type, HatchBoundaryPath.EdgeType[name]);
  }
});

test('HATCH defaults and optional pixel-size edits retain accepted signed zero', () => {
  const h = new Hatch(HatchPattern.Solid, false);
  assert.equal(h.Associative, false);
  assert.equal(h.PixelSize, 0);
  assert.equal(h.SeedPoints.Count, 1);
  assert.equal(h.SeedPoints.get_Item(0).X, 0);
  assert.equal(h.SeedPoints.get_Item(0).Y, 0);
  h.PixelSize = null;
  assert.equal(h.Clone().PixelSize, null);
  h.PixelSize = -0;
  for (const value of [-1, NaN, Infinity, -Infinity]) {
    assert.throws(() => { h.PixelSize = value; }, ArgumentOutOfRangeException);
    assert.ok(Object.is(h.PixelSize, -0));
  }
  assert.ok(Object.is(h.Clone().PixelSize, -0));
});

test('HATCH seed editing validates before mutation and copies value elements', () => {
  const h = new Hatch(HatchPattern.Solid, false), p = new Vector2(3, 4);
  h.SeedPoints.Add(p);
  p.X = 99;
  h.SeedPoints.get_Item(1).Y = 99;
  assert.equal(h.SeedPoints.get_Item(1).X, 3);
  assert.equal(h.SeedPoints.get_Item(1).Y, 4);
  assert.throws(() => h.SeedPoints.set_Item(1, new Vector2(NaN, 0)), ArgumentOutOfRangeException);
  assert.equal(h.SeedPoints.get_Item(1).X, 3);
  assert.throws(() => h.SeedPoints.Insert(-1, new Vector2(NaN, 0)), { name: 'ArgumentOutOfRangeException', ParamName: 'index' });
  assert.equal(h.SeedPoints.Count, 2);
});

test('entity contours update their detached geometry explicitly', () => {
  const source = new Line(Vector3.Zero, Vector3.UnitX), path = new HatchBoundaryPath([source]);
  source.EndPoint = new Vector3(4, 5, 6);
  assert.equal(path.Edges.get_Item(0).End.X, 1);
  path.Update();
  assert.equal(path.Edges.get_Item(0).End.X, 4);
  assert.equal(path.Edges.get_Item(0).End.Y, 5);
  assert.equal(path.Entities.get_Item(0), source);
});

test('associative HATCH links source reactors and unlink releases them', () => {
  const path = circlePath(), source = path.Entities.get_Item(0);
  const h = new Hatch(HatchPattern.Solid, [path], true);
  assert.equal(path.ContainingHatch, h);
  assert.ok(Array.from(source.Reactors).includes(h));
  const released = h.UnLinkBoundary();
  assert.equal(released.get_Item(0), source);
  assert.equal(h.Associative, false);
  assert.equal(path.Entities.Count, 0);
  assert.ok(!Array.from(source.Reactors).includes(h));
  assert.equal(path.Edges.Count, 1);
});

test('HATCH rejects duplicate boundary ownership without transferring the path', () => {
  const path = circlePath(), h = new Hatch(HatchPattern.Solid, [path], true);
  assert.throws(() => h.BoundaryPaths.Add(path), ArgumentException);
  assert.equal(h.BoundaryPaths.Count, 1);
  assert.throws(() => new Hatch(HatchPattern.Solid, [path], false), ArgumentException);
  assert.equal(path.ContainingHatch, h);
});

test('HATCH clones independently own pattern, paths and seeds without source association', () => {
  const h = new Hatch(HatchPattern.Line, [circlePath()], true);
  h.SeedPoints.Add(new Vector2(1, 2));
  const q = h.Clone();
  assert.equal(q.Associative, false);
  assert.notEqual(q.Pattern, h.Pattern);
  assert.notEqual(q.BoundaryPaths.get_Item(0), h.BoundaryPaths.get_Item(0));
  assert.equal(q.BoundaryPaths.get_Item(0).Entities.Count, 0);
  q.SeedPoints.Clear();
  q.Pattern.Scale = 3;
  assert.equal(h.SeedPoints.Count, 2);
  assert.equal(h.Pattern.Scale, 1);
});

test('identity HATCH transformation preserves geometry identity but unlinks source association', () => {
  const path = circlePath(), source = path.Entities.get_Item(0);
  const h = new Hatch(HatchPattern.Solid, [path], true), pattern = h.Pattern, edge = path.Edges.get_Item(0);
  const center = source.Center;
  h.TransformBy(Matrix3.Identity, Vector3.Zero);
  assert.equal(h.BoundaryPaths.get_Item(0), path);
  assert.equal(path.Edges.get_Item(0), edge);
  assert.equal(path.Entities.Count, 0);
  assert.equal(h.Pattern, pattern);
  assert.equal(h.Associative, false);
  assert.equal(path.ContainingHatch, h);
  assert.deepEqual(source.Center, center);
  assert.ok(!Array.from(source.Reactors).includes(h));
});
