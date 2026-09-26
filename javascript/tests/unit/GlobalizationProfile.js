import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { validateOrdinalProfile } from '../../tools/globalization.mjs';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
const pairs = [[97,65],[98,66]];
const profile = { profile: 'unit-reference', scalarMappings: pairs.length,
  scalarMapSha256: createHash('sha256').update(JSON.stringify(pairs)).digest('hex') };
test('globalization evidence accepts only the exact pinned production mapping', () => {
  assert.equal(validateOrdinalProfile(pairs,profile).profile,'unit-reference');
  assert.ok(Object.isFrozen(validateOrdinalProfile(pairs,profile)));
});
test('globalization evidence rejects data or count drift without a fallback', () => {
  assert.throws(()=>validateOrdinalProfile([[97,65],[98,67]],profile),/differs/);
  assert.throws(()=>validateOrdinalProfile(pairs,{...profile,scalarMappings:3}),/differs/);
});
test('globalization evidence requires an explicit profile', () => {
  assert.throws(()=>validateOrdinalProfile(pairs,null),/required/);
});
test('reference comparer does not adopt newer host ICU Unicode case additions', () => {
  // These were unequal in the actual .NET 8 / Ubuntu 22 differential run.
  for (const [a,b] of [['\u019b','\ua7dc'],['\u0264','\ua7cb'],['\u1c8a','\u1c89'],['\ua7cd','\ua7cc'],['\ua7db','\ua7da']]) {
    assert.equal(OrdinalIgnoreCaseEquals(a,b),false);
    assert.equal(OrdinalIgnoreCaseEquals(b,a),false);
  }
});
