import test from 'node:test';
import assert from 'node:assert/strict';
import { CadClient, ApiError } from '../../samples/netDxf.Shadow.Studio/wwwroot/cad/client.mjs';

const receipt = (revision = 0) => ({ state: {
  serverId: 'server', documentId: 'document', workspaceId: 'live',
  viewVersion: 0, changeVersion: revision, revision
} });
const response = value => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const deferred = () => {
  let resolve;
  const promise = new Promise(complete => { resolve = complete; });
  return { promise, resolve };
};

test('unchanged background refreshes preserve a pending drawing frame', async () => {
  const frame = deferred();
  const client = new CadClient(async path => path.includes('/render') ? frame.promise : response(receipt()));
  await client.activate('drawing');
  const rendering = client.read('render', { blob: true });
  await client.refresh();
  await client.refresh();
  frame.resolve(new Response('synthetic frame', { headers: { 'Content-Type': 'image/svg+xml' } }));
  const result = await rendering;
  assert.ok(result instanceof Blob, 'An unchanged receipt must not discard the only requested drawing frame.');
  assert.equal(await result.text(), 'synthetic frame');
});

test('a changed background receipt still fences an obsolete drawing frame', async () => {
  const frame = deferred();
  let revision = 0;
  const client = new CadClient(async path => path.includes('/render') ? frame.promise : response(receipt(revision)));
  await client.activate('drawing');
  const rendering = client.read('render', { blob: true });
  revision = 1;
  await client.refresh();
  frame.resolve(new Response('obsolete frame'));
  assert.equal(await rendering, null);
  assert.equal(client.state.revision, 1);
});

test('overlapping refreshes apply only the latest request without invalidating matching reads', async () => {
  const first = deferred(), second = deferred(), frame = deferred();
  let states = 0;
  const client = new CadClient(async path => {
    if (path.includes('/render')) return frame.promise;
    return ++states === 1 ? response(receipt()) : states === 2 ? first.promise : second.promise;
  });
  await client.activate('drawing');
  const rendering = client.read('render', { blob: true });
  const older = client.refresh(), newer = client.refresh();
  second.resolve(response(receipt()));
  await newer;
  first.resolve(response(receipt(1)));
  await older;
  assert.equal(client.state.revision, 0);
  frame.resolve(new Response('current frame'));
  assert.equal(await (await rendering).text(), 'current frame');
});

test('an obsolete background refresh failure cannot affect a reactivated drawing', async () => {
  const pending = deferred();
  let calls = 0;
  const client = new CadClient(async () => ++calls === 2 ? pending.promise : response(receipt()));
  await client.activate('drawing');
  const refreshing = client.refresh();
  await client.activate('drawing');
  pending.resolve(new Response('{"error":"stale state"}', { status: 409 }));
  await refreshing;
  assert.equal(client.state.revision, 0);
});

test('a current background refresh failure retains its status', async () => {
  let calls = 0;
  const client = new CadClient(async () => ++calls === 1 ? response(receipt()) : new Response('{"error":"state unavailable"}', { status: 503 }));
  await client.activate('drawing');
  await assert.rejects(client.refresh(), error => error instanceof ApiError && error.status === 503 && error.message === 'state unavailable');
});

test('conditional background refresh sends the exact receipt and accepts an unchanged null response', async () => {
  const paths = [];
  const client = new CadClient(async path => { paths.push(path); return response(paths.length === 1 ? receipt(7) : null); });
  await client.activate('drawing'); const before = client.data, epoch = client.epoch;
  let changes = 0; client.addEventListener('state', () => changes++);
  await client.refresh();
  const query = new URLSearchParams(paths[1].split('?')[1]);
  assert.equal(query.get('knownServer'), 'server'); assert.equal(query.get('knownChange'), '7');
  assert.equal(client.data, before); assert.equal(client.epoch, epoch); assert.equal(changes, 0);
});
