import test from 'node:test';
import assert from 'node:assert/strict';
import { ReadOnlyReferenceView } from '../../runtime/DatabaseModel.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { DependencyView } from '../../runtime/StoredDependencyCollections.js';
import { TableSnapshot } from '../../runtime/TablePayload.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
import { DxfDocument, DxfVersion, Section, DxfTag } from '../../index.js';

// IList<T>/ICollection<T> adaptation: operations must fail as read-only, even
// when their arguments are invalid. Delegated reads retain the backing list contract.
const factories = {
  reference: list => ReadOnlyReferenceView(list),
  dependency: list => DependencyView(list),
  snapshot: list => TableSnapshot(list)
};
for (const [name, factory] of Object.entries(factories)) {
  for (const [operation, args] of [['Add',[null]],['Clear',[]],['Insert',[-1,null]],['Remove',[null]],['RemoveAt',[-1]],['set_Item',[-1,null]]]) {
    test(`${name} read-only ${operation} rejects before inspecting arguments`, () => {
      const item = {}, list = new ReferenceList([item]), view = factory(list);
      assert.throws(() => view[operation](...args), NotSupportedException);
      assert.equal(view.Count, 1); assert.equal(view.get_Item(0), item);
      assert.equal(list.get_Item(0), item);
    });
  }
  test(`${name} flags and copied output preserve reference identity`, () => {
    const item = {}, list = new ReferenceList([item, null, item]), view = factory(list), output = Array(5).fill('unchanged');
    assert.equal(view.IsReadOnly, true); assert.equal(view.IsFixedSize, true);
    assert.equal(view.IsSynchronized, false); assert.equal(view.SyncRoot, view.SyncRoot);
    assert.equal(view.Contains(item), true); assert.equal(view.IndexOf(item), 0);
    assert.equal(view.IndexOf(null), 1); assert.equal(view.Contains({}), false);
    view.CopyTo(output, 1); assert.deepEqual(output, ['unchanged', item, null, item, 'unchanged']);
    output[1] = {}; assert.equal(view.get_Item(0), item);
    assert.equal(Object.isFrozen(view), true);
  });
  test(`${name} delegated bounds and CopyTo validation`, () => {
    const view = factory(new ReferenceList([{}]));
    assert.throws(() => view.get_Item(-1), ArgumentOutOfRangeException);
    assert.throws(() => view.get_Item(1), ArgumentOutOfRangeException);
    assert.throws(() => view.CopyTo(null, 0), ArgumentNullException);
    assert.throws(() => view.CopyTo([null], -1), ArgumentOutOfRangeException);
    const output = ['unchanged'];
    assert.throws(() => view.CopyTo(output, 1), ArgumentException);
    assert.deepEqual(output, ['unchanged']);
  });
  test(`${name} lookup delegates equality rather than stringifying entries`, () => {
    const first = { Equals(other) { return other?.key === 7; } }, view = factory(new ReferenceList([first]));
    assert.equal(view.Contains({key:7}), true); assert.equal(view.IndexOf({key:7}), 0);
    assert.equal(view.IndexOf({key:8}), -1);
  });
}
for (const name of ['reference','dependency']) {
  test(`${name} is a live view and its nonempty enumeration detects mutation`, () => {
    const one = {}, two = {}, list = new ReferenceList([one]), view = factories[name](list);
    const iterator = view.GetEnumerator(); assert.equal(iterator.MoveNext(), true);
    assert.equal(iterator.Current, one); list.Add(two);
    assert.equal(view.Count, 2); assert.equal(view.length, 2); assert.equal(view.get_Item(1), two);
    assert.throws(() => iterator.MoveNext(), InvalidOperationException);
    assert.deepEqual([...view], [one, two]);
  });
}
test('table snapshot detaches membership but not element identity', () => {
  const item = {}, list = new ReferenceList([item]), view = TableSnapshot(list);
  list.Clear(); assert.equal(view.Count, 1); assert.equal(view.get_Item(0), item);
});
test('all read-only factory methods are present on empty snapshots', () => {
  for (const factory of Object.values(factories)) {
    const view = factory(new ReferenceList()); assert.equal(view.Count, 0);
    assert.equal(view.IndexOf(null), -1); assert.equal(view.Contains(null), false);
    view.CopyTo([],0); assert.deepEqual([...view],[]);
    assert.throws(()=>view.Clear(),NotSupportedException);
  }
});
test('SECTION manager returns read-only snapshots retained across edits', () => {
  const doc = new DxfDocument(DxfVersion.AutoCad2018), first = new Section(), second = new Section();
  doc.Entities.Add(first); doc.Entities.Add(second);
  const manager = doc.Objects.CreateSectionManager([first, second, first], true);
  const sections = manager.Sections, tags = manager.Tags;
  assert.throws(()=>sections.Clear(),NotSupportedException);
  assert.throws(()=>tags.set_Item(1,new DxfTag(70,0)),NotSupportedException);
  manager.ReplaceSections([second],false);
  assert.deepEqual([...sections],[first,second,first]); assert.equal(tags.get_Item(1).Value,1);
  assert.deepEqual([...manager.Sections],[second]); assert.equal(manager.Tags.get_Item(1).Value,0);
});

test('reference views reject a null backing list', () => {
  for (const factory of [ReadOnlyReferenceView, DependencyView])
    assert.throws(() => factory(null), error => error instanceof ArgumentNullException && error.ParamName === 'list');
});
