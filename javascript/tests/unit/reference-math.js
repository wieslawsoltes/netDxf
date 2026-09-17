// Supplemental regressions; the independent System.Math differential owns expected corpus results.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { DotNetMath } from '../../runtime/GeometryRuntime.js';
import { Words, fma, add2, sub2, mul2, div2 } from '../../runtime/reference-math/arithmetic.js';
import { Sin, Cos } from '../../runtime/reference-math/sincos.js';
import { Tan } from '../../runtime/reference-math/tan.js';
import { Asin, Acos } from '../../runtime/reference-math/asincos.js';
import { Atan } from '../../runtime/reference-math/atan.js';
import { Atan2 } from '../../runtime/reference-math/atan2.js';
import { doubleBits, fromBits } from '../../tools/wire.mjs';
import { referenceMathCorpus } from '../../tools/reference-math-corpus.mjs';

test('geometry runtime uses the pinned native trigonometric implementations', () => {
  for (const [name, method] of Object.entries({ Sin, Cos, Tan, Asin, Acos, Atan, Atan2 })) assert.equal(DotNetMath[name], method);
});
test('reference word storage uses explicit word order and preserves all binary64 bits', () => {
  const word = new Words();
  for (const value of ['8000000000000000','7FF8000000001234','FFF8000000005678','0000000000000001','7FEFFFFFFFFFFFFF']) {
    word.x = fromBits(value);
    assert.equal((word.i[1] >>> 0).toString(16).padStart(8,'0') + (word.i[0] >>> 0).toString(16).padStart(8,'0'), value.toLowerCase());
    assert.equal(doubleBits(word.d), value);
  }
  word.i[1] = 0x3ff00000; word.i[0] = 0; assert.equal(word.x, 1);
});
test('fused arithmetic retains cancellation that separately rounded multiplication loses', () => {
  const epsilon = 2 ** -52;
  assert.equal((1 + epsilon) * (1 - epsilon) - 1, 0);
  assert.equal(fma(1 + epsilon, 1 - epsilon, -1), -(2 ** -104));
  assert.equal(fma(Number.MAX_VALUE, 2, -Number.MAX_VALUE), Number.MAX_VALUE);
  assert.equal(fma(Number.MAX_VALUE, 2, -Infinity), -Infinity);
});
test('fused arithmetic rounds halfway ties and subnormals to nearest even', () => {
  assert.equal(fma(1, 1, 2 ** -53), 1);
  assert.equal(fma(1, 1 + 2 ** -52, 2 ** -53), 1 + 2 ** -51);
  assert.ok(Object.is(fma(Number.MIN_VALUE, 0.5, 0), 0));
  assert.ok(Object.is(fma(-Number.MIN_VALUE, 0.5, 0), -0));
  assert.equal(fma(Number.MIN_VALUE, 0.5, Number.MIN_VALUE), 2 * Number.MIN_VALUE);
});
test('fused arithmetic preserves signed zero and the selected native NaN operand', () => {
  assert.ok(Object.is(fma(-0, 1, -0), -0));
  assert.ok(Object.is(fma(-0, 1, 0), 0));
  assert.ok(Object.is(fma(1, 1, -1), 0));
  const x = fromBits('7FF8000000001234'), y = fromBits('FFF8000000005678');
  assert.equal(doubleBits(fma(x, y, 0)), 'FFF8000000005678');
  assert.equal(doubleBits(fma(x, 1, y)), '7FF8000000001234');
  assert.equal(doubleBits(fma(1, 1, y)), 'FFF8000000005678');
});
test('double-length helpers preserve residual components before final rounding', () => {
  assert.deepEqual(add2(1, 2 ** -54), [1, 2 ** -54]);
  assert.deepEqual(sub2(1, 2 ** -54), [1, -(2 ** -54)]);
  assert.deepEqual(mul2(1 + 2 ** -52, 1 - 2 ** -52), [1, -(2 ** -104)]);
  const [value, error] = div2(1, 0, 3, 0);
  assert.equal(value, 1 / 3); assert.ok(error > 0);
});
test('trigonometric endpoints and signed zeros retain their exact values', () => {
  for (const fn of [Sin, Tan, Asin, Atan]) { assert.ok(Object.is(fn(-0), -0)); assert.ok(Object.is(fn(0), 0)); }
  assert.equal(Cos(-0), 1); assert.equal(Acos(1), 0);
  assert.equal(Atan2(0, -1), Math.PI); assert.equal(Atan2(-0, -1), -Math.PI);
  assert.ok(Object.is(Atan2(-0, 1), -0));
  assert.equal(doubleBits(Sin(fromBits('BFC9CA2AA6000000'))), 'BFC99D96849865DD');
});
test('direct math corpus is deterministic, nonempty and contains unique original input identities', () => {
  const a = referenceMathCorpus(), b = referenceMathCorpus();
  assert.equal(a.length, 61876); assert.deepEqual(a, b);
  assert.equal(new Set(a.map(call => call.id)).size, a.length);
  for (const call of a) for (const value of call.args) assert.match(value, /^[A-F0-9]{16}$/);
});
test('bundled preferred math source and licenses match the pinned provenance manifest', () => {
  const root = new URL('../../', import.meta.url), manifest = JSON.parse(fs.readFileSync(new URL('tools/ReferenceMath/source-manifest.json', root)));
  assert.equal(manifest.ref, 'f94f6d8a3572840d3ba42ab9ace3ea522c99c0c2');
  for (const [name, expected] of Object.entries(manifest.files)) {
    assert.equal(createHash('sha256').update(fs.readFileSync(new URL('third_party/glibc-math/' + name, root))).digest('hex'), expected, name);
  }
  assert.equal(fs.readFileSync(new URL('runtime/reference-math/LICENSE.LGPL-2.1', root), 'utf8'), fs.readFileSync(new URL('third_party/glibc-math/COPYING.LIB', root), 'utf8'));
  const pkg = JSON.parse(fs.readFileSync(new URL('package.json', root)));
  assert.equal(pkg.license, 'MIT AND LGPL-2.1-or-later'); assert.equal(pkg.private, true);
});
