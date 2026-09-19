import test from 'node:test';
import assert from 'node:assert/strict';
import { Text, Shape, ShapeStyle, Vector3 } from '../../index.js';
import { doubleBits, fromBits } from '../../tools/wire.mjs';

// Supplemental regressions. The existing entity corpus independently compares these
// constructor/edit/clone operations with the unmodified pinned C# implementation.
test('text and shape angles retain canonical NaN bits through cold and warmed edits', () => {
  const factories = [() => new Text('label'), () => new Shape('ZIG', new ShapeStyle('S', 's.shx'))];
  const input = [Infinity, -Infinity, fromBits('7FF8000000000001'), fromBits('FFF8000000001234')];
  for (const factory of factories) for (const member of ['Rotation', ...(factory().constructor === Shape ? ['ObliqueAngle'] : [])]) {
    const value = factory();
    for (let i = 0; i < 20000; i++) {
      value[member] = i % 720 - 360;
      assert.ok(Number.isFinite(value[member]));
      value[member] = input[i % input.length];
      assert.equal(doubleBits(value[member]), 'FFF8000000000000');
      if (i % 500 === 0) assert.equal(doubleBits(value.Clone()[member]), 'FFF8000000000000');
    }
  }
});
test('binary angle storage preserves raw shape construction and normalizes only on editing', () => {
  for (const bits of ['8000000000000000','7FF8000000001234','FFF8000000001234']) {
    const value = new Shape('ZIG', new ShapeStyle('S', 's.shx'), Vector3.Zero, 1, fromBits(bits));
    assert.equal(doubleBits(value.Rotation), bits);
    if (bits !== '8000000000000000') assert.equal(doubleBits(value.Clone().Rotation), 'FFF8000000000000');
  }
  const a = new Shape('ZIG', new ShapeStyle('S', 's.shx'), Vector3.Zero, 1, -450);
  assert.equal(a.Rotation, -450); assert.equal(a.Clone().Rotation, 270);
  a.Rotation = -450; assert.equal(a.Rotation, 270);
  const b = new Text('other'); b.Rotation = 45;
  a.ObliqueAngle = -Infinity; assert.equal(b.Rotation, 45);
});
