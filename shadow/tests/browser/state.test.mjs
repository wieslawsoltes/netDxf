import { test } from 'node:test';
import assert from 'node:assert/strict';
import { StateGate, identityHeaders, sameReceipt, viewKey, presentationKey } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/studio-state.mjs';

const state = (changeVersion = 0, extra = {}) => ({ serverId: 'server-a', documentId: 'document-a',
  workspaceId: 'live', viewVersion: 0, revision: 0, layout: 'Model', fontRevision: 0, changeVersion, ...extra });
test('bootstrap accepts a coherent state', () => { const g = new StateGate(); assert(g.accept(state(), g.stamp(), true)); assert(g.current(state())); });
test('notice fences an older in-flight state even before refresh', () => {
  const g = new StateGate(); g.accept(state(), g.stamp()); const request = g.stamp(); g.observe(state(2));
  assert(!g.current(state())); assert(!g.accept(state(1), request)); assert(g.accept(state(2), request));
});
test('delayed notices cannot move the floor backwards', () => {
  const g = new StateGate(); g.observe(state(5)); assert(!g.observe(state(2))); assert(!g.accept(state(3), g.stamp()));
});
test('an action receipt cannot replace a newer snapshot', () => {
  const g = new StateGate(); g.accept(state(4), g.stamp()); assert(!g.accept(state(3), g.stamp()));
});
test('equal event IDs do not require duplicate refresh', () => {
  const g = new StateGate(); g.accept(state(4), g.stamp()); assert(!g.observe(state(4))); assert(g.current(state(4)));
});
test('server restart fences requests from the prior process', () => {
  const g = new StateGate(); g.accept(state(9), g.stamp()); const old = g.stamp();
  g.observe(state(0, { serverId: 'server-b' }));
  assert(!g.accept(state(10), old, true)); assert(g.accept(state(0, { serverId: 'server-b' }), g.stamp()));
});
test('authoritative state GET recovers a restarted server without SSE', () => {
  const g = new StateGate(); g.accept(state(9), g.stamp()); const old = g.stamp();
  assert(g.accept(state(0, { serverId: 'server-b' }), old, true)); assert(!g.accept(state(10), old, true));
});
test('a mutation receipt cannot change server authority', () => {
  const g = new StateGate(); g.accept(state(9), g.stamp()); assert(!g.accept(state(0, { serverId: 'server-b' }), g.stamp()));
});
test('empty or unsafe receipts fail closed', () => {
  const g = new StateGate();
  for (const bad of [null, {}, state(-1), state(NaN), state(1.5), state(Number.MAX_SAFE_INTEGER + 1), state(0, { serverId: '' })])
    assert.throws(() => g.observe(bad));
});
test('headers carry all workspace identity fields', () => {
  assert.deepEqual(identityHeaders(state()), { 'X-Shadow-Studio': '1', 'X-Shadow-Document': 'document-a',
    'X-Shadow-Workspace': 'live', 'X-Shadow-View': '0' }); assert.throws(() => identityHeaders());
});
for (const [field, value] of [['serverId', 'b'], ['documentId', 'b'], ['workspaceId', 'b'], ['viewVersion', 1],
  ['revision', 1], ['layout', 'Paper'], ['fontRevision', 1], ['changeVersion', 1]])
  test(`render identity includes ${field}`, () => assert.notEqual(viewKey(state()), viewKey(state(0, { [field]: value }))));
test('inspector receipt rejects equal-revision wrong documents and branches', () => {
  assert(sameReceipt(state(), state()));
  for (const field of ['documentId', 'workspaceId', 'viewVersion', 'revision']) assert(!sameReceipt(state(0, { [field]: 'wrong' }), state()));
});
test('graph receipt additionally fences layouts and resources', () => {
  assert(sameReceipt(state(), state(), true)); assert(!sameReceipt(state(0, { layout: 'Paper' }), state(), true));
  assert(!sameReceipt(state(0, { fontRevision: 1 }), state(), true));
});

const camera = { x: 1, y: 2, scale: 3 }, viewport = { w: 600, h: 400 };
const imageKey = (c = camera, v = viewport, backend = 'skia', snapshot = '', merge = null) =>
  presentationKey(state(), c, v, backend, snapshot, merge);
for (const [field, value] of [['x', 9], ['y', 9], ['scale', 9]])
  test(`display receipt includes camera ${field}`, () => assert.notEqual(imageKey(), imageKey({ ...camera, [field]: value })));
for (const [field, value] of [['w', 800], ['h', 500]])
  test(`display receipt includes pixel ${field}`, () => assert.notEqual(imageKey(), imageKey(camera, { ...viewport, [field]: value })));
test('display receipt includes backend', () => assert.notEqual(imageKey(), imageKey(camera, viewport, 'svg')));
test('display receipt includes retained revision', () => assert.notEqual(imageKey(), imageKey(camera, viewport, 'skia', '0')));
test('display receipt includes merge preview', () => assert.notEqual(imageKey(), imageKey(camera, viewport, 'skia', '', 'plan')));
test('display receipt includes immutable view identity', () => assert.notEqual(imageKey(), presentationKey(state(1), camera, viewport, 'skia')));
