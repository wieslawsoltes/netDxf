import test from 'node:test';
import assert from 'node:assert/strict';
import { evidenceProblems } from '../../tools/verification-report.mjs';
const proof = { runtimeFingerprint: 'runtime', verificationFingerprint: 'verifier' };
const valid = { ...proof, completed: true, stats: { failures: 0, scenarios: 10 } };

test('exact evidence rejects numeric mismatches even when every case executed', () => {
  assert.match(evidenceProblems({ ...valid, stats: { failures: 166 } }, proof).join(), /166/);
});
test('exact evidence rejects browser policy errors and missing fingerprints', () => {
  assert.ok(evidenceProblems({ completed: false, fatal: 'ERR_BLOCKED_BY_ADMINISTRATOR' }, proof).length >= 3);
});
test('exact evidence requires all scenarios rather than a passing subset', () => {
  assert.ok(evidenceProblems(valid, proof, { equal: { 'stats.scenarios': 11 } }).length);
  assert.deepEqual(evidenceProblems(valid, proof, { equal: { 'stats.scenarios': 10 } }), []);
});
test('exact evidence requires matching runtime and verifier identities', () => {
  assert.ok(evidenceProblems({ ...valid, verificationFingerprint: 'old' }, proof).length);
  assert.ok(evidenceProblems(null, proof).length);
});
