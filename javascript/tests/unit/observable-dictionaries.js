import test from 'node:test';
import assert from 'node:assert/strict';
import { ObservableDictionary, ObservableDictionaryEventArgs, Vector2, BoxedString } from '../../index.js';
import { KeyValuePair } from '../../runtime/GenericDictionary.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException,
  InvalidOperationException, KeyNotFoundException, NotSupportedException } from '../../runtime/Errors.js';
const fresh = () => new ObservableDictionary(0, null, 'string', 'int');
const contents = d => Array.from(d, p => [p.Key, p.Value]);

test('dictionary constructor overloads and typed missing-value defaults', () => {
  for (const d of [new ObservableDictionary(), new ObservableDictionary(7), new ObservableDictionary(null), new ObservableDictionary(3, null)]) assert.equal(d.Count, 0);
  assert.throws(() => new ObservableDictionary(-1), { name: 'ArgumentOutOfRangeException', ParamName: 'capacity' });
  const d = fresh(), output = { value: 99 };
  assert.equal(d.TryGetValue('absent', output), false); assert.equal(output.value, 0);
  const vectors = new ObservableDictionary(0, null, 'string', Vector2), a = {}, b = {};
  vectors.TryGetValue('a', a); vectors.TryGetValue('b', b); a.value.X = 9;
  assert.deepEqual(b.value.ToArray(), [0, 0]);
});
test('dictionary replacement requires an existing key and reports no callbacks on missing keys', () => {
  const d = fresh(), log = []; d.BeforeAddItem.Add(() => log.push('add')); d.BeforeRemoveItem.Add(() => log.push('remove'));
  assert.throws(() => d.set_Item('missing', 3), KeyNotFoundException); assert.deepEqual(log, []);
  assert.throws(() => d.RemovePair({ Key: 'missing', Value: 3 }), KeyNotFoundException);
});
test('dictionary checks before-add cancellation before null and duplicate validation', () => {
  const d = fresh(); d.Add('a', 1); d.BeforeAddItem.Add((_, e) => { e.Cancel = true; });
  for (const key of [null, 'a', 'new']) assert.throws(() => d.Add(key, 2), { name: 'ArgumentException', ParamName: 'value' });
  assert.deepEqual(contents(d), [['a', 1]]);
  const other = fresh(); assert.throws(() => other.Add(null, 1), ArgumentNullException);
});
test('dictionary replacement has source before-remove before-add add remove order', () => {
  const d = fresh(), log = []; d.Add('a', 1);
  for (const event of ['BeforeRemoveItem', 'BeforeAddItem', 'AddItem', 'RemoveItem']) d[event].Add((sender, e) => log.push([event, e.Item.Value, sender.get_Item('a')]));
  d.set_Item('a', 2);
  assert.deepEqual(log, [['BeforeRemoveItem', 1, 1], ['BeforeAddItem', 2, 1], ['AddItem', 2, 2], ['RemoveItem', 1, 2]]);
});
test('dictionary cancellation stops replacement in source order and a later subscriber may uncancel', () => {
  const d = fresh(), log = []; d.Add('a', 1);
  d.BeforeRemoveItem.Add((_, e) => { e.Cancel = true; }); d.BeforeAddItem.Add(() => log.push('add'));
  d.set_Item('a', 2); assert.equal(d.get_Item('a'), 1); assert.deepEqual(log, []);
  d.BeforeRemoveItem.Add((_, e) => { assert.equal(e.Cancel, true); e.Cancel = false; });
  d.set_Item('a', 3); assert.equal(d.get_Item('a'), 3); assert.deepEqual(log, ['add']);
});
test('dictionary event subscriptions use a multicast snapshot and remove the last duplicate', () => {
  const d = fresh(), log = [], late = () => log.push('late'), second = () => log.push('second');
  d.BeforeAddItem.Add(() => { log.push('first'); d.BeforeAddItem.Remove(second); d.BeforeAddItem.Add(late); });
  d.BeforeAddItem.Add(second); d.BeforeAddItem.Add(second);
  d.Add('a', 1); assert.deepEqual(log, ['first', 'second', 'second']); log.length = 0;
  d.Add('b', 2); assert.deepEqual(log, ['first', 'second', 'late']);
});
test('dictionary event item copies structs but preserves referenced objects', () => {
  const key = new Vector2(1, 2), value = new Vector2(3, 4), e = new ObservableDictionaryEventArgs(KeyValuePair(key, value));
  key.X = 99; e.Item.Key.Y = 99; e.Item.Value.X = 99;
  assert.deepEqual(e.Item.Key.ToArray(), [1, 2]); assert.deepEqual(e.Item.Value.ToArray(), [3, 4]);
  const reference = {}; assert.equal(new ObservableDictionaryEventArgs({ Key: 'a', Value: reference }).Item.Value, reference);
  assert.equal(e.Cancel, false); e.Cancel = true; assert.equal(e.Cancel, true);
});
test('dictionary after-add exceptions retain the added or replaced value and prevent later events', () => {
  const d = fresh(); d.Add('a', 1); let removed = false;
  d.AddItem.Add(() => { throw new NotSupportedException(); }); d.RemoveItem.Add(() => { removed = true; });
  assert.throws(() => d.set_Item('a', 2), NotSupportedException); assert.equal(d.get_Item('a'), 2); assert.equal(removed, false);
  assert.throws(() => d.Add('b', 3), NotSupportedException); assert.equal(d.get_Item('b'), 3);
});
test('dictionary reentrant removal retains the outer success and duplicate post-removal callback', () => {
  const d = fresh(); d.Add('a', 1); let nested = false, events = 0;
  d.BeforeRemoveItem.Add(() => { if (!nested) { nested = true; assert.equal(d.Remove('a'), true); } });
  d.RemoveItem.Add(() => events++); assert.equal(d.Remove('a'), true); assert.equal(events, 2); assert.equal(d.Count, 0);
});
test('dictionary Clear snapshots keys and permits cancelled removal or callback insertion', () => {
  const d = fresh(); d.Add('a', 1); d.Add('b', 2);
  d.BeforeRemoveItem.Add((_, e) => { if (e.Item.Key === 'a') { e.Cancel = true; d.Add('c', 3); } });
  d.Clear(); assert.deepEqual(contents(d), [['a', 1], ['c', 3]]);
});
test('dictionary pair containment uses value equality while pair removal uses reference identity', () => {
  const d = new ObservableDictionary(), a = new BoxedScalar('Int32', 12), b = new BoxedScalar('Int32', 12);
  d.Add('a', a); assert.equal(d.ContainsPair({ Key: 'a', Value: b }), true);
  assert.equal(d.RemovePair({ Key: 'a', Value: b }), false); assert.equal(d.RemovePair({ Key: 'a', Value: a }), true);
  const numbers = fresh(); numbers.Add('a', 12); assert.equal(numbers.RemovePair({ Key: 'a', Value: 12 }), false);
});
test('dictionary string identity uses shared adapters and shared empty strings', () => {
  const d = new ObservableDictionary(0, null, 'string', 'string'), a = new BoxedString('a'), b = new BoxedString('a');
  d.Add('a', a); assert.equal(d.ContainsValue(b), true); assert.equal(d.RemovePair({ Key: 'a', Value: b }), false);
  assert.equal(d.RemovePair({ Key: 'a', Value: a }), true); d.Add('empty', '');
  assert.equal(d.RemovePair({ Key: 'empty', Value: new BoxedString('') }), true);
});
test('dictionary copied struct keys and values cannot be changed through inputs outputs or events', () => {
  const d = new ObservableDictionary(0, null, Vector2, Vector2), key = new Vector2(1, 2), value = new Vector2(3, 4);
  d.BeforeAddItem.Add((_, e) => { e.Item.Key.X = 100; e.Item.Value.X = 100; }); d.Add(key, value);
  key.X = 5; value.Y = 6; d.get_Item(new Vector2(1, 2)).X = 50;
  const p = d.GetEnumerator(); p.MoveNext(); p.Current.Key.Y = 7; p.Current.Value.X = 8;
  assert.deepEqual(contents(d).map(([k, v]) => [k.ToArray(), v.ToArray()]), [[[1, 2], [3, 4]]]);
});
test('dictionary missing lookups do not hash before first allocation but always reject null keys', () => {
  let calls = 0; const comparer = { Equals: (a, b) => a === b, GetHashCode() { calls++; throw new InvalidOperationException(); } };
  const d = new ObservableDictionary(comparer);
  assert.equal(d.ContainsKey('a'), false); assert.equal(d.Remove('a'), false);
  assert.throws(() => d.get_Item('a'), KeyNotFoundException); assert.equal(calls, 0);
  assert.throws(() => d.ContainsKey(null), ArgumentNullException); assert.equal(calls, 0);
  assert.throws(() => d.Add('a', 1), InvalidOperationException); assert.equal(calls, 1);
  assert.throws(() => d.ContainsKey('a'), InvalidOperationException); assert.equal(calls, 2);
});
test('dictionary collision chains honor custom equality and reuse freed slots in LIFO order', () => {
  const comparer = { GetHashCode: () => 0, Equals: (a, b) => a.toLowerCase() === b.toLowerCase() };
  const d = new ObservableDictionary(8, comparer); for (const k of ['a', 'b', 'c', 'd']) d.Add(k, k);
  assert.throws(() => d.Add('A', 'x'), ArgumentException); d.Remove('b'); d.Remove('c'); d.Add('e', 'e'); d.Add('f', 'f');
  assert.deepEqual(Array.from(d.Keys), ['a', 'f', 'e', 'd']); assert.equal(d.get_Item('E'), 'e');
});
test('dictionary does not shortcut non-reflexive Equals or rehash mutated reference keys', () => {
  const key = { GetHashCode() { return this.hash; }, hash: 1, Equals(other) { return other === this; } }, d = new ObservableDictionary();
  d.Add(key, 1); key.hash = 2; assert.equal(d.ContainsKey(key), false); d.Clear(); assert.equal(d.Count, 1);
  const bad = { GetHashCode: () => 0, Equals: () => false }, other = new ObservableDictionary();
  other.Add(bad, bad); other.Add(bad, bad); assert.equal(other.Count, 2); assert.equal(other.ContainsValue(bad), false);
});
test('dictionary key and value views remain live cached and read-only', () => {
  const d = fresh(), keys = d.Keys, values = d.Values; assert.equal(keys, d.Keys); assert.equal(values, d.Values);
  d.Add('a', 1); assert.equal(keys.Count, 1); assert.equal(values.Contains(1), true); assert.equal(keys.Contains('a'), true);
  for (const view of [keys, values]) { assert.equal(view.IsReadOnly, true); for (const method of ['Add', 'Remove', 'Clear']) assert.throws(() => view[method]('a'), NotSupportedException); }
  d.set_Item('a', 2); assert.deepEqual([...values], [2]); d.Remove('a'); assert.equal(keys.Count, 0);
});
test('dictionary enumerators retain Current snapshots and allow overwrite removal and clear', () => {
  const d = fresh(); d.Add('a', 1); d.Add('b', 2); const e = d.GetEnumerator(); assert.equal(e.Current.Value, 0);
  e.MoveNext(); d.set_Item('a', 7); assert.equal(e.Current.Value, 1); d.Remove('b'); assert.equal(e.MoveNext(), false);
  e.Reset(); assert.equal(e.MoveNext(), true); assert.equal(e.Current.Value, 7); d.Clear(); assert.equal(e.MoveNext(), false);
});
test('dictionary successful insertion invalidates enumerators but duplicate and cancellation do not', () => {
  const d = fresh(); d.Add('a', 1); const e = d.GetEnumerator();
  assert.throws(() => d.Add('a', 2), ArgumentException); assert.equal(e.MoveNext(), true);
  d.Add('b', 2); assert.throws(() => e.MoveNext(), InvalidOperationException); assert.throws(() => e.Reset(), InvalidOperationException);
});
test('dictionary empty view enumerators keep singleton Current semantics even after insertion', () => {
  const d = fresh(), key = d.Keys.GetEnumerator(), value = d.Values.GetEnumerator(), pairs = d.GetEnumerator();
  for (const e of [key, value]) { assert.throws(() => e.Current, InvalidOperationException); assert.equal(e.MoveNext(), false); }
  d.Add('a', 1); for (const e of [key, value]) { e.Reset(); assert.equal(e.MoveNext(), false); assert.throws(() => e.Current, InvalidOperationException); }
  assert.throws(() => pairs.MoveNext(), InvalidOperationException);
});
test('dictionary CopyTo preserves destination gaps and validates null index and capacity in order', () => {
  const d = fresh(); d.Add('a', 1); d.Add('b', 2); const dest = [null, null, null, null]; d.CopyTo(dest, 1);
  assert.equal(dest[0], null); assert.equal(dest[3], null); assert.equal(dest[1].Key, 'a'); assert.equal(dest[2].Value, 2);
  assert.throws(() => d.CopyTo(null, -1), { name: 'ArgumentNullException', ParamName: 'array' });
  assert.throws(() => d.CopyTo([], -1), ArgumentOutOfRangeException); assert.throws(() => d.CopyTo([], 0), ArgumentException);
});
test('dictionary input corpus is deterministic complete and contains no expected output tables', async () => {
  const { observableDictionaryCorpus } = await import('../../tools/observable-dictionary-corpus.mjs'); const c = observableDictionaryCorpus();
  assert.deepEqual(c, observableDictionaryCorpus()); assert.equal(c.length, 427); assert.equal(new Set(c.map(p => p.name)).size, 427);
  assert.equal(c.reduce((n, p) => n + p.request.steps.length, 0), 15799); assert.ok(c.every(p => !Object.hasOwn(p, 'expected')));
});
test('dictionary native transport rejects malformed evidence and retains constructor failures distinctly', async () => {
  const { ObservableDictionaryOracleSession } = await import('../../tools/ObservableDictionaryOracleSession.mjs');
  const request = { op: 'observable-dictionary', steps: [{ method: 'Clear' }] }; let factories = 0;
  const s = new ObservableDictionaryOracleSession(() => { factories++; return { request: async () => factories === 1 ? [] : { constructorError: 'ArgumentOutOfRangeException', param: 'capacity' }, close: async () => {} }; });
  assert.equal((await s.observe(request)).ok, false); const observed = await s.observe(request);
  assert.equal(observed.ok, true); assert.equal(observed.value.constructorError, 'ArgumentOutOfRangeException'); assert.equal(factories, 2); await s.close();
});
test('dictionary value equality accepts mixed primitive and explicit string adapters symmetrically', () => {
  const d = new ObservableDictionary(); d.Add('a', 'value'); d.Add(new BoxedString('b'), new BoxedString('other'));
  assert.equal(d.ContainsKey(new BoxedString('a')), true); assert.equal(d.ContainsKey('b'), true);
  assert.equal(d.ContainsValue(new BoxedString('value')), true); assert.equal(d.ContainsValue('other'), true);
});
