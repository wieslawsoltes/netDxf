import { test } from 'node:test';
import assert from 'node:assert/strict';
import { AgentClient } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/client.mjs';
import { configuration, defaultConfiguration, modelDiscoveryRequest } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/contracts.mjs';
import { ModelDiscovery, modelCatalog, modelOptionLabel, selectModelBudgets } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/model-discovery.mjs';

const gate = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
const model = (limits = {}) => ({ id: 'account-model', displayName: 'Account model', contextWindowTokens: null, inputTokenLimit: null, outputTokenLimit: null, ...limits });
const catalog = (provider = 'openai', models = [model()]) => ({ provider, status: 'available', models, notice: '', truncated: false });

test('discovery requests validate routing without requiring a selected model or session configuration', () => {
  assert.deepEqual(modelDiscoveryRequest({ provider: 'google', apiKey: ' key-token ', endpoint: 'https://proxy.example/v1/' }),
    { provider: 'google', apiKey: 'key-token', endpoint: 'https://proxy.example/v1' });
  assert.throws(() => modelDiscoveryRequest({ provider: 'other' }), /supported/);
  assert.throws(() => modelDiscoveryRequest({ provider: 'openai', apiKey: 'invalid key' }), /whitespace/);
  assert.throws(() => modelDiscoveryRequest({ provider: 'openai', endpoint: 'https://key:secret@proxy.example' }), /credentials/);
});

test('catalog preserves only reported valid limits and distinguishes input from shared context', () => {
  const value = modelCatalog(catalog('google', [model({ inputTokenLimit: 131072, outputTokenLimit: 8192 }), model(), { id: 'broken\nmodel' }, model({ id: 'different', contextWindowTokens: '200000', inputTokenLimit: -1, outputTokenLimit: 1.5 })]), 'google');
  assert.equal(value.models.length, 2); assert.equal(value.models[0].contextWindowTokens, null);
  assert.equal(value.models[0].inputTokenLimit, 131072);
  assert.match(modelOptionLabel(value.models[0]), /context unknown.*input 131,072.*output 8,192/);
  assert.deepEqual([value.models[1].contextWindowTokens, value.models[1].inputTokenLimit, value.models[1].outputTokenLimit], [null, null, null]);
  assert.throws(() => modelCatalog(value, 'anthropic'), /invalid model catalog/);
});

test('selection fills a reported context while retaining an already compatible output reserve', () => {
  const selected = selectModelBudgets(model({ contextWindowTokens: 200000, inputTokenLimit: 180000, outputTokenLimit: 16000 }), '128000', '8192');
  assert.equal(selected.context, '200000'); assert.equal(selected.output, '8192'); assert.match(selected.note, /Context window: 200,000/);
});

test('input-only capacity is a conservative editable budget without claiming shared context metadata', () => {
  const selected = selectModelBudgets(model({ inputTokenLimit: 131072, outputTokenLimit: 8192 }), '32000', '16384');
  assert.equal(selected.context, '131072'); assert.equal(selected.output, '8192'); assert.match(selected.note, /Input limit.*conservative.*output reserve/);
});

test('unknown limits retain manual context and compatible output instead of guessing defaults', () => {
  assert.deepEqual(selectModelBudgets(model(), '65536', '4096'), { context: '65536', output: '4096', note: 'The provider did not report a context window or input limit. Your manual context budget is retained.' });
  assert.equal(selectModelBudgets(model({ contextWindowTokens: 8000000 }), '65536', '4096').context, '65536');
});

test('output-only metadata and small context limits constrain the existing output reserve', () => {
  const outputOnly = selectModelBudgets(model({ outputTokenLimit: 2048 }), '65536', '8192');
  assert.equal(outputOnly.context, '65536'); assert.equal(outputOnly.output, '2048');
  const small = selectModelBudgets(model({ inputTokenLimit: 4096 }), '128000', '8192');
  assert.equal(small.context, '4096'); assert.equal(small.output, '2047');
  const odd = selectModelBudgets(model({ contextWindowTokens: 4097 }), '128000', '8192');
  assert.equal(odd.output, '2047');
  assert.equal(configuration({ ...defaultConfiguration, contextWindowTokens: odd.context, maxOutputTokens: odd.output }).maxOutputTokens, 2047);
  assert.throws(() => configuration({ ...defaultConfiguration, contextWindowTokens: 4097, maxOutputTokens: 2048 }), /less than half/);
  assert.match(selectModelBudgets(model({ outputTokenLimit: 128 }), '65536', '8192').note, /below the supported/);
  assert.match(selectModelBudgets(model(), '', '8192').note, /Enter a local context budget/);
});

test('provider changes discard delayed catalogs and errors without changing the newer state', async () => {
  let identity = { provider: 'openai', apiKey: 'first-key' }; const first = gate(), second = gate();
  const discovery = new ModelDiscovery({ getIdentity: () => identity, load: request => request.provider === 'openai' ? first.promise : second.promise });
  const oldRequest = discovery.discover(); identity = { provider: 'google', apiKey: 'second-key' }; discovery.invalidate();
  const newRequest = discovery.discover(); second.resolve(catalog('google')); await newRequest;
  first.reject(new Error('Old provider failed')); await oldRequest;
  assert.equal(discovery.state.provider, 'google'); assert.equal(discovery.state.status, 'available');
});

test('switching away and back, credential rotation and endpoint edits invalidate pending metadata', async () => {
  for (const change of [value => ({ ...value, apiKey: 'new-key' }), value => ({ ...value, endpoint: 'https://other.example' }), value => value]) {
    let identity = { provider: 'openai', apiKey: 'old-key', endpoint: 'https://proxy.example' }; const pending = gate();
    const discovery = new ModelDiscovery({ getIdentity: () => identity, load: () => pending.promise });
    const request = discovery.discover(); identity = change(identity); discovery.invalidate(); pending.resolve(catalog()); await request;
    assert.equal(discovery.state.status, 'idle'); assert.deepEqual(discovery.state.models, []);
  }
});

test('identity checks reject programmatic changes and disposed views never publish replies', async () => {
  let identity = { provider: 'openai' }, notifications = 0; const pending = gate();
  const discovery = new ModelDiscovery({ getIdentity: () => identity, load: () => pending.promise, onChange: () => notifications++ });
  const request = discovery.discover(); identity = { provider: 'google' }; pending.resolve(catalog()); await request;
  assert.equal(discovery.state.status, 'idle'); assert.equal(notifications, 2);
  const later = gate(); discovery.load = () => later.promise; const next = discovery.discover(); discovery.dispose(); later.resolve(catalog('google')); await next;
  assert.equal(notifications, 3);
});

test('discovery transport posts a private JSON request once and never mutates the active session', async () => {
  const requests = []; const client = new AgentClient({ fetcher: async (url, options) => { requests.push({ url, options }); return new Response(JSON.stringify(catalog())); } });
  client.config = { provider: 'anthropic', model: 'existing' }; client.configured = true; client.transcript.running = true;
  await client.models({ provider: 'openai', apiKey: 'private-discovery-key', endpoint: 'https://proxy.example' });
  assert.equal(requests.length, 1); assert.equal(requests[0].url, '/api/agent/models');
  assert.deepEqual(JSON.parse(JSON.parse(requests[0].options.body).arguments[0]), { provider: 'openai', apiKey: 'private-discovery-key', endpoint: 'https://proxy.example' });
  assert.equal(requests[0].options.headers['X-Shadow-Studio'], '1'); assert.equal(requests[0].options.cache, 'no-store');
  assert.equal(client.config.provider, 'anthropic'); assert.equal(client.configured, true); assert.equal(client.transcript.running, true); assert.equal(client.busy, false);
  assert(!requests[0].url.includes('private-discovery-key')); client.dispose();
});

test('discovery errors redact the entered credential even before provider configuration', async () => {
  const client = new AgentClient({ invoke: async () => { throw new Error('Rejected private-discovery-key'); } });
  await assert.rejects(client.models({ provider: 'openai', apiKey: 'private-discovery-key' }), /Rejected \[redacted\]/); client.dispose();
});
