import test from 'node:test';
import assert from 'node:assert/strict';
import { OrdinalIgnoreCaseEquals as equal, OrdinalIgnoreCaseKey as key } from '../../runtime/Collections.js';
import { DxfRawObjectStore } from '../../index.js';
import { ObjectFixture } from '../support/ObjectFixture.js';
test('ordinal names do not expand sharp S or ligatures',()=>{
  assert.equal(equal('ß','SS'),false);assert.equal(equal('ﬃ','FFI'),false);
  assert.equal(equal('straße','STRASSE'),false);assert.equal(equal('QA_OBJECTS','qa_objects'),true);
});
test('ordinal names handle Greek and supplementary single-scalar casing',()=>{
  assert.equal(equal('\u1f80','\u1f88'),true);assert.equal(equal('σ','ς'),true);
  assert.equal(equal('\u{10428}','\u{10400}'),true);assert.equal(key('Zażółć'),key('ZAŻÓŁĆ'));
});
test('dictionary indexed lookup preserves distinct non-expanding names',()=>{
  const tx=DxfRawObjectStore.Open(ObjectFixture()).BeginEdit();
  const a=tx.CreatePlaceholder('10','ß'),b=tx.CreatePlaceholder('10','SS');
  const store=DxfRawObjectStore.Open(tx.Commit());assert.notEqual(a,b);
  assert.equal(store.RootDictionary.Find('ß').Handle,a);assert.equal(store.RootDictionary.Find('ss').Handle,b);
});
