import test from 'node:test';
import assert from 'node:assert/strict';
import { CadClient, DrawingOpenError } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/client.mjs';

const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const receipt = (documentId, entities = 0, revision = 0) => ({ state: {
  serverId: 'server', documentId, workspaceId: 'live', viewVersion: 0,
  changeVersion: revision, revision, entities
} });
const response = value => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const failure = (message, status = 400) => new Response(JSON.stringify({ error: message }), { status });
const drawing = () => Object.assign(new Blob(['0\nEOF\n']), { name: 'Synthetic.dxf' });

function fixture(options = {}) {
  const entries = new Map([['original', receipt('original-source', 1, 1)], ['other', receipt('other-source', 1, 1)]]);
  const calls = [];
  let next = 0;
  const client = new CadClient(async (path, request) => {
    const url = new URL(path, 'https://example.test'), body = typeof request.body === 'string' ? JSON.parse(request.body) : request.body;
    const call = { path: url.pathname, query: url.searchParams, ...request, body };
    calls.push(call);
    if (url.pathname === '/api/cad/documents') {
      if (request.method === 'GET') return response([...entries.keys()].map(id => ({ id, name: `${id}.dxf`, dirty: false })));
      assert.equal(body.demo, false);
      const id = `new-${++next}`;
      entries.set(id, receipt(`empty-${id}`));
      return response({ id, name: body.name });
    }
    const [, , , id, operation] = url.pathname.split('/'), data = entries.get(id);
    assert.ok(data, `Unexpected drawing ${id}`);
    if (operation === 'state') return await options.state?.({ id, data }) ?? response(data);
    if (operation === 'command') {
      data.state.revision++; data.state.changeVersion++;
      return response(data);
    }
    const matches = request.headers['X-Shadow-Document'] === data.state.documentId &&
      request.headers['X-Shadow-Workspace'] === data.state.workspaceId &&
      request.headers['X-Shadow-View'] === String(data.state.viewVersion);
    if (operation === 'load') {
      assert.ok(matches);
      assert.equal(Number(url.searchParams.get('revision')), data.state.revision);
      assert.ok(body instanceof Blob);
      const commit = () => {
        const loaded = receipt(`loaded-${id}`, 3);
        loaded.state.viewVersion = 1; loaded.state.changeVersion = 1;
        entries.set(id, loaded);
        return response(loaded);
      };
      return await options.load?.({ id, data, commit }) ?? commit();
    }
    if (operation === 'close') {
      assert.equal(body.discard, false, 'Failed imports must never force-discard a changed drawing.');
      if (!matches || body.revision !== data.state.revision) return failure('The drawing changed before close.', 409);
      entries.delete(id);
      return response([...entries.keys()].map(id => ({ id })));
    }
    assert.fail(`Unexpected operation ${operation}`);
  });
  return { client, entries, calls };
}

test('a rejected import preserves the displayed drawing and closes only its empty staging document', async () => {
  const f = fixture({ load: () => failure('Invalid drawing record.') });
  await f.client.activate('original');
  const original = f.client.data;
  let resets = 0;
  f.client.addEventListener('reset', () => resets++);
  await assert.rejects(f.client.open(drawing()), error => error instanceof DrawingOpenError && !error.retained && error.cause.status === 400);
  assert.equal(f.client.id, 'original');
  assert.equal(f.client.data, original);
  assert.equal(resets, 0);
  assert.deepEqual([...f.entries.keys()], ['original', 'other']);
  const closes = f.calls.filter(call => call.path.endsWith('/close'));
  assert.equal(closes.length, 1);
  assert.equal(closes[0].headers['X-Shadow-Document'], 'empty-new-1');
});

test('a pending import stays out of the tabs and adopts its loaded receipt only after success', async () => {
  const started = deferred(), release = deferred();
  const f = fixture({ load: async ({ commit }) => { started.resolve(); await release.promise; return commit(); } });
  await f.client.activate('original');
  const opening = f.client.open(drawing());
  await started.promise;
  assert.equal(f.client.id, 'original');
  assert.deepEqual((await f.client.documents()).map(d => d.id), ['original', 'other']);
  await f.client.refresh();
  release.resolve();
  const opened = await opening;
  assert.equal(opened.active, true);
  assert.equal(f.client.id, opened.id);
  assert.equal(f.client.state.documentId, `loaded-${opened.id}`);
  assert.equal(f.client.state.entities, 3);
  assert.equal(f.calls.filter(c => c.path === `/api/cad/${opened.id}/state`).length, 1, 'The successful receipt does not need another state round trip.');
  assert.equal((await f.client.documents()).length, 3);
});

for (const activity of ['switch', 'edit']) test(`a pending import cannot steal focus after an explicit ${activity}`, async () => {
  const started = deferred(), release = deferred();
  const f = fixture({ load: async ({ commit }) => { started.resolve(); await release.promise; return commit(); } });
  await f.client.activate('original');
  const opening = f.client.open(drawing());
  await started.promise;
  if (activity === 'switch') await f.client.activate('other');
  else await f.client.mutate('command', { command: { name: 'LINE' } });
  const foreground = f.client.data;
  release.resolve();
  const opened = await opening;
  assert.equal(opened.active, false);
  assert.equal(f.client.id, activity === 'switch' ? 'other' : 'original');
  assert.equal(f.client.data, foreground);
  assert.equal((await f.client.documents()).length, 3);
});

test('a new command or gesture keeps focus when an earlier import finishes', async () => {
  const started = deferred(), release = deferred();
  const f = fixture({ load: async ({ commit }) => { started.resolve(); await release.promise; return commit(); } });
  await f.client.activate('original');
  const opening = f.client.open(drawing());
  await started.promise;
  const foreground = f.client.data, epoch = f.client.epoch;
  f.client.beginInteraction();
  assert.equal(f.client.epoch, epoch, 'Input ownership must not fence otherwise current drawing reads.');
  release.resolve();
  const opened = await opening;
  assert.equal(opened.active, false);
  assert.equal(f.client.id, 'original');
  assert.equal(f.client.data, foreground);
  assert.equal(f.entries.get(opened.id).state.entities, 3);
  assert.ok((await f.client.documents()).some(document => document.id === opened.id));
});

test('interaction before Open does not prevent the newly requested drawing taking focus', async () => {
  const f = fixture();
  await f.client.activate('original');
  f.client.beginInteraction();
  const opened = await f.client.open(drawing());
  assert.equal(opened.active, true);
  assert.equal(f.client.id, opened.id);
});

test('concurrent imports give foreground ownership to the most recently requested Open', async () => {
  const starts = [deferred(), deferred()], releases = [deferred(), deferred()];
  const f = fixture({ load: async ({ id, commit }) => {
    const index = Number(id.slice(4)) - 1;
    starts[index].resolve(); await releases[index].promise; return commit();
  } });
  await f.client.activate('original');
  const first = f.client.open(drawing()); await starts[0].promise;
  const second = f.client.open(drawing()); await starts[1].promise;
  releases[0].resolve();
  assert.equal((await first).active, false);
  assert.equal(f.client.id, 'original');
  releases[1].resolve();
  assert.equal((await second).active, true);
  assert.equal(f.client.id, 'new-2');
});

test('a committed import with a lost response cannot be deleted by cleanup', async () => {
  const f = fixture({ load: ({ commit }) => { commit(); throw new TypeError('Connection ended.'); } });
  await f.client.activate('original');
  await assert.rejects(f.client.open(drawing()), error => error instanceof DrawingOpenError && error.retained && /tab was kept/.test(error.message));
  assert.equal(f.client.id, 'original');
  assert.equal(f.entries.get('new-1').state.entities, 3);
  assert.equal(f.calls.filter(c => c.path.endsWith('/load')).length, 1);
  assert.equal(f.calls.filter(c => c.path.endsWith('/close')).length, 1);
  assert.equal((await f.client.documents()).length, 3);
});

test('a newly created document that changes before import is neither overwritten nor discarded', async () => {
  const f = fixture({ state: ({ id, data }) => {
    if (id.startsWith('new-')) { data.state.entities = 1; data.state.revision = 1; data.state.changeVersion = 1; }
  } });
  await f.client.activate('original');
  await assert.rejects(f.client.open(drawing()), error => error instanceof DrawingOpenError && error.retained && /changed before import/.test(error.message));
  assert.equal(f.client.id, 'original');
  assert.equal(f.calls.filter(c => /\/(load|close)$/.test(c.path)).length, 0);
  assert.equal(f.entries.get('new-1').state.entities, 1);
});

test('an unavailable initial receipt never authorizes an unguarded close', async () => {
  const f = fixture({ state: ({ id }) => id.startsWith('new-') ? failure('State unavailable.', 503) : undefined });
  await f.client.activate('original');
  await assert.rejects(f.client.open(drawing()), error => error instanceof DrawingOpenError && error.retained && error.cause.status === 503);
  assert.equal(f.client.id, 'original');
  assert.equal(f.calls.filter(c => c.path.endsWith('/close')).length, 0);
});

test('repeated rejected imports release every empty staging document', async () => {
  const f = fixture({ load: () => failure('Invalid drawing record.') });
  await f.client.activate('original');
  for (let i = 0; i < 12; i++) {
    await assert.rejects(f.client.open(drawing()), DrawingOpenError);
    assert.equal(f.entries.size, 2);
    assert.equal(f.client.pendingDocuments.size, 0);
  }
});
