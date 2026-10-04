import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfTag, DxfRawDocument, DxfRawObjectStore, DxfRawObjectStoreOptions, DxfRawSortOrderEntry,
  DxfRawHandleIndex, DxfRawDictionary, DxfRawPlaceholder, DxfRawXRecord } from '../../index.js';
import { ObjectFixture } from '../support/ObjectFixture.js';
import * as E from '../../runtime/Errors.js';
const load = doc => DxfRawObjectStore.Open(DxfRawDocument.Load(doc.ToBytes()));
for (let version=13;version<=18;version++) for (const binary of [false,true]) {
  test(`object transactions/create-clone-delete/${version}/${binary}`, () => {
    const source=ObjectFixture(version,binary), sourceBytes=source.ToBytes(), store=DxfRawObjectStore.Open(source), tx=store.BeginEdit();
    const root=tx.EnsureRootDictionary(), d=tx.CreateDictionary(root,'Test'), leaf=tx.CreatePlaceholder(d,'Leaf');
    const x=tx.CreateXRecord(d,'Payload',[new DxfTag(160,-9223372036854775808n),new DxfTag(10,-0),new DxfTag(330,leaf),new DxfTag(310,Uint8Array.of(0,255))]);
    tx.SetDictionaryEntry(d,'Alias',x,true); tx.CreateVariable(d,'Variable','Żółć Ω 🧪 \\U+0041');
    const buf=tx.CreateIdBuffer(d,'Buffer',['A','0','A','B']), defaults=tx.CreateDictionary(d,'Defaults',true,true);
    tx.RenameEntry(d,'variable','VARIABLE'); const copy=tx.CloneDictionaryTree(d,root,'Copy');
    let sort; if (version>=14) sort=tx.SetDrawOrder('2',[new DxfRawSortOrderEntry('A','0'),new DxfRawSortOrderEntry('B','FFFFFFFFFFFFFFFE')]);
    let doc=tx.Commit(); assert.throws(()=>tx.Commit(),E.ObjectDisposedException);
    for(let n=0;n<3;n++) {
      const current=load(doc), copied=current.Get(copy), cp=copied.Find('Payload').Handle;
      assert.equal(copied.Find('Alias').Handle,cp); assert.notEqual(cp,x);
      assert.equal(current.Get(cp).Data.find(t=>t.Code===330).Value,copied.Find('Leaf').Handle);
      assert.ok(Object.is(current.Get(x).Data.find(t=>t.Code===10).Value,-0));
      assert.deepEqual([...current.Get(buf).Handles],['A','0','A','B']);
      assert.ok(current.Get(current.Get(defaults).DefaultHandle) instanceof DxfRawPlaceholder);
      if(sort) assert.equal(current.Get(sort).Entries[1].SortHandle,'FFFFFFFFFFFFFFFE');
      doc=DxfRawDocument.Load(doc.ToBytes(!doc.IsBinary));
    }
    assert.deepEqual(source.ToBytes(),sourceBytes); assert.equal(store.RootDictionary.Find('Test'),null);
    const edit=load(doc).BeginEdit(); assert.throws(()=>edit.RemoveEntry(d,'Alias',true),E.InvalidOperationException);
    assert.notEqual(edit.Get(d).Find('Alias'),null); assert.equal(edit.RemoveEntry(d,'Alias'),true);
    edit.SetXRecord(x,[new DxfTag(1,'changed')]); edit.SetIdBuffer(buf,[]);
    assert.equal(edit.RemoveEntry(root,'Test',true),true);
    const result=DxfRawObjectStore.Open(edit.Commit()); assert.equal(result.Get(d),null); assert.ok(result.Get(copy) instanceof DxfRawDictionary);
  });
  test(`object transactions/extensions/${version}/${binary}`,()=>{
    const source=ObjectFixture(version,binary), tx=DxfRawObjectStore.Open(source).BeginEdit();
    const ext=tx.EnsureExtensionDictionary('A'); assert.equal(tx.EnsureExtensionDictionary('A'),ext);
    const child=tx.CreateXRecord(ext,'Data',[new DxfTag(330,'B')]), doc=tx.Commit();
    const edit=load(doc).BeginEdit(); assert.equal(edit.RemoveExtensionDictionary('A',true),true);
    const final=edit.Commit(); assert.equal(DxfRawObjectStore.Open(final).Get(child),null);
    const getLine=d=>d.Sections.flatMap(s=>s.Records).find(r=>r.Name==='LINE');
    assert.deepEqual([...getLine(final).Tags].map(t=>[t.Code,t.Value]),[...getLine(source).Tags].map(t=>[t.Code,t.Value]));
  });
}
test('object transactions/payload errors, rollback and commit repair',()=>{
  const store=DxfRawObjectStore.Open(ObjectFixture()),tx=store.BeginEdit(),root=tx.EnsureRootDictionary();
  const rec=tx.CreateXRecord(root,'Dangling',[new DxfTag(330,'100')]);
  assert.notEqual(rec,'100'); assert.throws(()=>tx.Commit(),E.InvalidDataException);
  const valid=tx.CreatePlaceholder(root,'Valid'); assert.notEqual(valid,'100');
  tx.SetXRecord(rec,[new DxfTag(330,valid)]); const doc=tx.Commit();
  assert.equal(load(doc).Get(rec).Data[0].Value,valid);
  const limited=DxfRawObjectStore.Open(ObjectFixture(),new DxfRawObjectStoreOptions(undefined,undefined,1)),edit=limited.BeginEdit();
  assert.throws(()=>edit.CreateDictionary('10','Over'),E.InvalidDataException);
  assert.strictEqual(edit.Commit(),limited.Document);
});
test('object transactions/iterator and reentrancy rollback',()=>{
  const store=DxfRawObjectStore.Open(ObjectFixture()),tx=store.BeginEdit(); let disposed=false;
  function* fail(){try{yield new DxfTag(1,'first');throw new E.IOException('injected');}finally{disposed=true;}}
  assert.throws(()=>tx.CreateXRecord('10','Failed',fail()),E.IOException); assert.equal(disposed,true);
  function* reenter(){yield new DxfTag(1,'first');tx.Dispose();}
  assert.throws(()=>tx.CreateXRecord('10','Failed',reenter()),E.InvalidOperationException);
  const actual=tx.CreatePlaceholder('10','After'), control=store.BeginEdit();
  assert.equal(actual,control.CreatePlaceholder('10','After')); control.Dispose(); tx.Commit();
});
test('object transactions/owned deletion rejects references and can be repaired',()=>{
  const tx=DxfRawObjectStore.Open(ObjectFixture()).BeginEdit(), d=tx.CreateDictionary('10','Tree'), leaf=tx.CreatePlaceholder(d,'Leaf');
  const buffer=tx.CreateIdBuffer('10','External',[leaf]),doc=tx.Commit(),edit=load(doc).BeginEdit();
  assert.throws(()=>edit.RemoveEntry('10','Tree',true),E.InvalidOperationException);
  assert.notEqual(edit.Get('10').Find('Tree'),null); edit.SetIdBuffer(buffer,[]); edit.RemoveEntry('10','Tree',true);
  assert.equal(DxfRawObjectStore.Open(edit.Commit()).Get(leaf),null);
});
test('object transactions/no-op, cancellation and disposal',()=>{
  const store=DxfRawObjectStore.Open(ObjectFixture()), empty=store.BeginEdit(); assert.strictEqual(empty.Commit(),store.Document);
  const controller=new AbortController(),tx=store.BeginEdit(controller.signal); tx.CreatePlaceholder('10','Cancelled');controller.abort();
  assert.throws(()=>tx.Commit(),E.OperationCanceledException);tx.Dispose();tx.Dispose();assert.throws(()=>tx.Get('10'),E.ObjectDisposedException);
  assert.equal(store.RootDictionary.Find('Cancelled'),null);
});
test('object transactions/text preserves one escape layer and rejects invalid UTF-16',()=>{
  for(const text of ['Plain','Żółć Ω','🧪','literal \\U+0041',' spaced ']) assert.equal(DxfRawObjectStore.DecodeText(DxfRawObjectStore.EncodeText(text)),text);
  for(const text of ['\ud800','a\udc00','bad\0','bad\n']) assert.throws(()=>DxfRawObjectStore.EncodeText(text),E.ArgumentException);
});
test('object transactions/deep owned trees are iterative',()=>{
  const tx=DxfRawObjectStore.Open(ObjectFixture()).BeginEdit(),first=tx.CreateDictionary('10','Long');let last=first;
  for(let i=0;i<1200;i++) last=tx.CreateDictionary(last,'next');
  const copy=tx.CloneDictionaryTree(first,'10','Copy'),doc=tx.Commit(),edit=DxfRawObjectStore.Open(doc).BeginEdit();
  edit.RemoveEntry('10','Long',true);const result=DxfRawObjectStore.Open(edit.Commit());
  assert.equal(result.Get(last),null); assert.ok(result.Get(copy) instanceof DxfRawDictionary);
});
