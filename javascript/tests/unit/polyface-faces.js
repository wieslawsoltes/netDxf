import test from 'node:test';
import assert from 'node:assert/strict';
import { PolyfaceMeshFace } from '../../netDxf/Entities/PolyfaceMeshFace.js';
import { Layer, AciColor, Vector3 } from '../../index.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, IndexOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';

test('polyface placeholder is writable but cannot be cloned until initialized', () => {
  const face = new PolyfaceMeshFace(); assert.deepEqual([...face.VertexIndexes], [0, 0, 0, 0]);
  assert.equal(face.Layer, null); assert.equal(face.Color, null); assert.throws(() => face.Clone(), ArgumentException);
  face.VertexIndexes[0] = -1; assert.equal(face.ValidateVertexIndexes(1), 1); assert.deepEqual([...face.Clone().VertexIndexes], [-1, 0, 0, 0]);
});
test('polyface active signed indices stop at first zero without inspecting padding', () => {
  const face = new PolyfaceMeshFace([1, 0, -32768, 32767]); assert.equal(face.ValidateVertexIndexes(1), 1);
  face.VertexIndexes[1] = -2; assert.throws(() => face.ValidateVertexIndexes(1), { name: 'ArgumentOutOfRangeException', ParamName: 'VertexIndexes', ActualValue: -2 });
  assert.throws(() => new PolyfaceMeshFace([]), ArgumentOutOfRangeException);
  assert.throws(() => new PolyfaceMeshFace([0, 1]), ArgumentException);
  assert.throws(() => new PolyfaceMeshFace([1, 2, 3, 4, 5]), ArgumentOutOfRangeException);
  assert.throws(() => new PolyfaceMeshFace(null), ArgumentNullException);
  assert.equal(new PolyfaceMeshFace([-32768]).ValidateVertexIndexes(32768), 1);
});
test('fixed array preserves signed Int16 data, rejects invalid edits and does not resize', () => {
  const source = [1, -2, 3], face = new PolyfaceMeshFace(source); source[0] = 99;
  const array = face.VertexIndexes; assert.equal(array, face.VertexIndexes); assert.equal(array.Length, 3); assert.equal(array[0], 1);
  for (const value of [32768, -32769, 0.5, NaN]) assert.throws(() => { array[0] = value; }, ArgumentOutOfRangeException);
  assert.equal(array[0], 1); assert.throws(() => array.get_Item(3), IndexOutOfRangeException);
  assert.throws(() => { array[-1] = 1; }, IndexOutOfRangeException); assert.throws(() => { array.length = 4; }, NotSupportedException);
  assert.throws(() => array.push(4), IndexOutOfRangeException); assert.throws(() => { delete array[0]; }, TypeError);
});
test('face layer callbacks can substitute null and exceptions leave the reference unchanged', () => {
  const face = new PolyfaceMeshFace([1]), old = new Layer('OLD'), proposed = new Layer('PROPOSED'), replacement = new Layer('REPLACEMENT');
  face.Layer = old; const handler = (sender, e) => { assert.equal(sender, face); assert.equal(face.Layer, old); assert.equal(e.OldValue, old); assert.equal(e.NewValue, proposed); e.NewValue = replacement; };
  face.LayerChanged.Add(handler); face.Layer = proposed; assert.equal(face.Layer, replacement); face.LayerChanged.Remove(handler);
  const fail = () => { throw new InvalidOperationException(); }; face.LayerChanged.Add(fail);
  assert.throws(() => { face.Layer = null; }, InvalidOperationException); assert.equal(face.Layer, replacement); face.LayerChanged.Remove(fail);
  face.LayerChanged.Add((sender, e) => { e.NewValue = null; }); face.Layer = proposed; assert.equal(face.Layer, null);
});
test('face clones independently copy indices and explicit or inherited appearance', () => {
  const face = new PolyfaceMeshFace([1, -2, 3]); assert.equal(face.Clone().Layer, null); assert.equal(face.Clone().Color, null);
  face.Layer = new Layer('FACE'); face.Color = AciColor.Red; const copy = face.Clone();
  assert.notEqual(copy.Layer, face.Layer); assert.notEqual(copy.Color, face.Color); copy.VertexIndexes[0] = 2; copy.Layer.Name = 'CLONE';
  assert.equal(face.VertexIndexes[0], 1); assert.equal(face.Layer.Name, 'FACE'); assert.equal(face.ToString(), 'PolyfaceMeshFace');
});
test('fixed vector arrays copy assigned values and expose explicit value versus element access', () => {
  const input = new Vector3(1, 2, 3), array = FixedArray([input]); input.X = 9; assert.equal(array[0].X, 1);
  array.get_Item(0).X = 8; assert.equal(array[0].X, 1); array[0].X = 4; assert.equal(array.get_Item(0).X, 4);
  const copy = array.Clone(); copy[0].X = 99; assert.equal(array[0].X, 4);
});
