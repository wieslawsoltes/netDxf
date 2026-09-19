import test from 'node:test';
import assert from 'node:assert/strict';
import { compareCaseCoverage } from '../../tools/evidence.mjs';
const pass = name => ({ name, passed: true });
test('completion reports every absent original case instead of counting a subset as full parity', () => {
  const result = compareCaseCoverage([pass('A'),pass('B')],[pass('A')]);
  assert.equal(result.complete,false); assert.deepEqual(result.missing,['B']);
  assert.equal(result.mirroredCases,1); assert.equal(result.originalCases,2);
});
test('completion rejects empty evidence', () => {
  assert.throws(()=>compareCaseCoverage([],[pass('A')]));
  assert.throws(()=>compareCaseCoverage([pass('A')],[]));
});
test('completion rejects duplicate test identities', () => {
  assert.throws(()=>compareCaseCoverage([pass('A'),pass('A')],[pass('A')]));
  assert.throws(()=>compareCaseCoverage([pass('A')],[pass('A'),pass('A')]));
});
test('completion rejects failing, skipped, and TODO cases', () => {
  for(const bad of [{name:'A',passed:false},{...pass('A'),skipped:true},{...pass('A'),todo:true}]) {
    assert.throws(()=>compareCaseCoverage([pass('A')],[bad]));
    assert.throws(()=>compareCaseCoverage([bad],[pass('A')]));
  }
});
test('completion reports renamed or unexpected test identities', () => {
  const result=compareCaseCoverage([pass('A')],[pass('Renamed')]);
  assert.deepEqual(result.unexpected,['Renamed']);assert.deepEqual(result.missing,['A']);assert.equal(result.complete,false);
});
test('case parity requires the same complete identity set', () => {
  const result=compareCaseCoverage([pass('A'),pass('B')],[pass('B'),pass('A')]);
  assert.equal(result.complete,true);assert.equal(result.missingCount,0);
});
