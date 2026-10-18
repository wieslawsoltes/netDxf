import { test } from 'node:test';
import assert from 'node:assert/strict';
import { AgentTranscript, configuration, defaultConfiguration, endpointUrl, parseArguments, parseSession, preferences, redact, safeFilePath, toolTemplate } from '../../samples/netDxf.Shadow.Studio/wwwroot/agents/contracts.mjs';
import { AgentClient, ChatGptCompanion } from '../../samples/netDxf.Shadow.Studio/wwwroot/agents/client.mjs';
import { StudioMcpBridge } from '../../samples/netDxf.Shadow.Studio/wwwroot/agents/mcp-bridge.mjs';
import { installStudioControl } from '../../samples/netDxf.Shadow.Studio/wwwroot/agents/studio-control.mjs';

const gate = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
const tick = () => new Promise(resolve => setImmediate(resolve));
const status = (events = [], rest = {}) => ({ sessionId: 'session', events, running: false, status: 'idle', contextTokens: 1200, contextLimit: 128000, ...rest });
const event = (sequence, type, message = '', data = null) => ({ sequence, type, message, data });
function fixture(callback) {
  const calls = [], scheduled = [];
  const client = new AgentClient({ getDocument: () => 'drawing', invoke: async (method, ...args) => {
    calls.push([method, ...args]); const result = await callback(method, ...args);
    return result === undefined && method === 'AgentPoll' ? status() : result;
  }, delay: (fn, ms) => { scheduled.push({ fn, ms }); return scheduled.length; }, clearDelay: () => {} });
  return { client, calls, scheduled };
}

test('configuration preserves editable providers and enforces endpoint and context boundaries', () => {
  const value = configuration({ ...defaultConfiguration, apiKey: 'test-key', model: 'account-model-id', permissionMode: 'fullAccess', endpoint: 'https://proxy.example/v1/' });
  assert.equal(value.model, 'account-model-id'); assert.equal(value.endpoint, 'https://proxy.example/v1');
  assert.equal(value.permissionMode, 'fullAccess');
  for (const endpoint of ['javascript:alert(1)', 'https://user:password@example.com', 'http://untrusted.example', 'https://example.com?key=secret', 'https://example.com/#token'])
    assert.throws(() => configuration({ ...defaultConfiguration, endpoint }));
  assert.throws(() => configuration({ ...defaultConfiguration, maxOutputTokens: 64000 }));
  assert.throws(() => configuration({ ...defaultConfiguration, permissionMode: 'bypass' }));
  assert.equal(endpointUrl('http://127.0.0.1:5091', { companion: true }), 'http://127.0.0.1:5091');
  assert.throws(() => endpointUrl('https://companion.example', { companion: true }));
});

test('persistent preferences exclude credentials, endpoints and blanket authorization', () => {
  const result = preferences({ ...defaultConfiguration, apiKey: 'private-token', endpoint: 'https://private.example', permissionMode: 'fullAccess', provider: 'google' });
  assert.deepEqual(Object.keys(result).sort(), ['contextWindowTokens', 'maxOutputTokens', 'maxTurns', 'model', 'provider'].sort());
  assert(!JSON.stringify(result).includes('private'));
});

test('conversation export/import redacts nested credentials and occurrences inside strings', () => {
  const result = JSON.parse(parseSession(JSON.stringify({
    apiKey: 'secret-key', nested: { access_token: 'access', refreshToken: 'refresh', authorization: 'Bearer xyz', password: 'p', keep: 'hello secret-key' },
    messages: [{ text: 'token secret-key in request failure' }], model: 'chosen'
  }), ['secret-key']));
  assert.deepEqual(result, { nested: { keep: 'hello [redacted]' }, messages: [{ text: 'token [redacted] in request failure' }], model: 'chosen' });
  assert.throws(() => parseSession('[]')); assert.throws(() => parseSession('{broken')); assert.throws(() => parseSession('x'.repeat(16 * 1024 * 1024 + 1)));
  assert.equal(redact({ '__proto__': { x: 1 }, constructor: 'unsafe', safe: true }).safe, true);
});

test('tool console accepts object arguments, builds required inputs and never coerces executable text', () => {
  assert.deepEqual(parseArguments('{"path":"file.cs","value":"<script>"}'), { path: 'file.cs', value: '<script>' });
  for (const value of ['[]', 'null', 'true', '(()=>42)()']) assert.throws(() => parseArguments(value));
  assert.deepEqual(JSON.parse(toolTemplate({ type: 'object', properties: { name: { type: 'string' }, count: { type: 'integer', minimum: 1 }, ignored: { type: 'string' } }, required: ['name', 'count'] })), { name: '', count: 1 });
});

test('workspace paths reject traversal and absolute or colon-bearing names', () => {
  for (const path of ['', '../x', '/etc/passwd', 'a//b', 'a/./b', 'C:\\file', 'file:stream', 'file\0name']) assert.throws(() => safeFilePath(path));
  assert.equal(safeFilePath('src\\drawing.cs'), 'src/drawing.cs');
});

test('streaming events are cursor deduplicated and one completed message replaces its deltas', () => {
  const state = new AgentTranscript();
  const batch = [event(1, 'user', 'Inspect'), event(2, 'assistantDelta', 'Hel'), event(3, 'assistantDelta', 'lo')];
  state.apply(status(batch, { running: true })); state.apply(status(batch, { running: true }));
  assert.equal(state.items.length, 2); assert.equal(state.items[1].text, 'Hello');
  state.apply(status([event(4, 'assistant', 'Hello.'), event(5, 'completed')]));
  assert.equal(state.items.filter(item => item.kind === 'assistant').length, 1);
  assert.equal(state.items[1].text, 'Hello.'); assert.equal(state.items[1].streaming, false); assert.equal(state.sequence, 5);
});

test('approval, execution and result update the same card using the real .NET event fields', () => {
  const state = new AgentTranscript();
  state.apply(status([event(1, 'approvalRequested', 'Allow property?', { requestId: 'call', tool: 'source_property_set', arguments: { value: 2 } })], { running: true }));
  assert.equal(state.items[0].name, 'source_property_set'); assert.equal(state.items[0].requestId, 'call');
  state.apply(status([event(2, 'toolStarted', '', { callId: 'call', name: 'source_property_set', arguments: { value: 2 } }), event(3, 'toolCompleted', '', { callId: 'call', name: 'source_property_set', result: '{"revision":3}', isError: false, milliseconds: 17 })]));
  assert.equal(state.items.length, 1); assert.equal(state.items[0].status, 'complete'); assert.equal(state.items[0].durationMs, 17);
});

test('retry discards partial inference without dropping completed earlier messages', () => {
  const state = new AgentTranscript();
  state.apply(status([event(1, 'user', 'Task'), event(2, 'assistantDelta', 'Partial'), event(3, 'retrying', 'Retrying inference', { discardPartial: true }), event(4, 'assistantDelta', 'Complete'), event(5, 'assistant', 'Complete')], { running: false }));
  assert.equal(state.items.filter(item => item.kind === 'assistant').length, 1);
  assert.equal(state.items.find(item => item.kind === 'assistant').text, 'Complete');
});

test('stopped or expired approvals cannot remain actionable after the server goes idle', () => {
  const state = new AgentTranscript();
  state.apply(status([event(1, 'approvalRequested', '', { requestId: 'expired', tool: 'write', arguments: {} })], { running: true }));
  state.apply(status([], { running: false })); assert.equal(state.items[0].status, 'interrupted');
});

test('checkpoint hydration ignores provider reasoning and does not duplicate historical event replay', () => {
  const state = new AgentTranscript();
  state.apply(status([event(1, 'user', 'Task'), event(2, 'assistant', 'Done'), event(3, 'completed')], {
    latestSequence: 3, messages: [
      { role: 'user', contents: [{ kind: 'text', text: 'Task' }] },
      { role: 'assistant', contents: [{ kind: 'reasoning', text: 'private reasoning', protectedData: 'opaque' }, { kind: 'text', text: 'Done' }, { kind: 'functionCall', callId: 'c', name: 'inspect', arguments: {} }] },
      { role: 'tool', contents: [{ kind: 'functionResult', callId: 'c', text: 'result' }] }
    ]
  }));
  assert.equal(state.items.filter(item => item.kind === 'user').length, 1);
  assert.equal(state.items.filter(item => item.kind === 'assistant').length, 1);
  assert.equal(state.items.find(item => item.kind === 'tool').status, 'complete');
  assert(!JSON.stringify(state.items).includes('private reasoning')); assert(!JSON.stringify(state.items).includes('opaque'));
  assert.equal(state.sequence, 3);
});

test('reset restores same-ID sessions when a provider change restarts event sequencing', () => {
  const state = new AgentTranscript(); state.apply(status([event(40, 'user', 'Old')]));
  state.apply(status([event(1, 'imported')], { reset: true, latestSequence: 1, messages: [{ role: 'user', contents: [{ kind: 'text', text: 'Retained task' }] }] }));
  assert.equal(state.sequence, 1); assert.equal(state.items[0].text, 'Retained task');
});

test('client binds a document before Start and never retries an uncertain mutation', async () => {
  const f = fixture(async method => {
    if (method === 'AgentStart') throw new Error('connection reset after dispatch');
    if (method === 'AgentPoll') return status();
    return {};
  });
  await f.client.configure({ ...defaultConfiguration, apiKey: 'test-token' });
  await assert.rejects(f.client.start('Draw a line'), /connection reset/);
  assert.equal(f.calls.filter(c => c[0] === 'AgentStart').length, 1);
  const names = f.calls.map(c => c[0]); assert(names.indexOf('AgentSetDocument') < names.indexOf('AgentStart'));
  assert.equal(f.client.transcript.running, true); assert.equal(f.client.transcript.status, 'reconnecting');
  await f.client.poll(); assert.equal(f.client.transcript.running, false);
  assert.equal(f.calls.filter(c => c[0] === 'AgentStart').length, 1); f.client.dispose();
});

test('resume continues server history with no duplicate user prompt', async () => {
  const f = fixture(async method => method === 'AgentPoll' ? status() : {});
  await f.client.configure({ ...defaultConfiguration, apiKey: 'test-token' }); await f.client.retry();
  assert.deepEqual(f.calls.find(c => c[0] === 'AgentRetry'), ['AgentRetry']);
  assert(!f.calls.some(c => c[0] === 'AgentStart')); f.client.dispose();
});

test('companion connection serializes configuration and rejects a turn until the native grant is ready', async () => {
  const native = gate();
  const f = fixture(async method => method === 'AgentCompanionConnect' ? native.promise : method === 'AgentPoll' ? status() : {});
  f.client.configured = true;
  const connecting = f.client.companionConnect('http://127.0.0.1:5091', 'private-companion-token'); await tick();
  assert.equal(f.client.busy, true);
  await assert.rejects(f.client.start('Run a build'), /Stop the active/);
  await assert.rejects(f.client.configure(defaultConfiguration), /Stop the active/);
  await assert.rejects(f.client.disconnect(), /connection or session change/);
  assert(!f.calls.some(call => call[0] === 'AgentStart' || call[0] === 'AgentDisconnect'));
  native.resolve({ connected: true, toolCount: 5 }); await connecting; assert.equal(f.client.busy, false); f.client.dispose();
});

test('disconnecting a required inference companion clears its provider and hydrates retained history', async () => {
  const f = fixture(async method => method === 'AgentCompanionDisconnect' ? { connected: false, providerDisconnected: true }
    : method === 'AgentPoll' ? status([], { latestSequence: 1, messages: [{ role: 'user', contents: [{ kind: 'text', text: 'Retained task' }] }] }) : {});
  f.client.configured = true; f.client.config = { provider: 'anthropic', apiKey: 'test-key' }; f.client.transcript.sequence = 45;
  let notification = '';
  f.client.addEventListener('providerdisconnected', event => { notification = event.detail.reason; });
  await f.client.companionDisconnect();
  assert.equal(f.client.configured, false); assert.equal(f.client.config, null); assert.match(notification, /Anthropic/);
  assert.deepEqual(f.calls.find(call => call[0] === 'AgentPoll'), ['AgentPoll', 0]);
  assert.equal(f.client.transcript.items[0].text, 'Retained task'); f.client.dispose();
});

test('disconnect interrupts a task still binding its drawing without dispatching it', async () => {
  const binding = gate(), clearing = gate();
  const f = fixture(async method => method === 'AgentSetDocument' ? binding.promise : method === 'AgentDisconnect' ? clearing.promise : method === 'AgentPoll' ? status() : {});
  f.client.configured = true;
  const start = f.client.start('Draw a line'); const rejected = assert.rejects(start, /interrupted by a disconnect/); await tick();
  const disconnect = f.client.disconnect(); binding.resolve({ documentId: 'drawing' }); await rejected;
  assert.equal(f.client.busy, true, 'retired work cannot release the disconnect operation');
  assert(!f.calls.some(call => call[0] === 'AgentStart'));
  clearing.resolve({}); await disconnect; assert.equal(f.client.configured, false); assert.equal(f.client.busy, false); f.client.dispose();
});

test('a delayed Start response cannot reactivate a disconnected provider', async () => {
  const response = gate();
  const f = fixture(async method => method === 'AgentStart' ? response.promise : method === 'AgentPoll' ? status() : {});
  f.client.configured = true;
  const start = f.client.start('Analyze'); const rejected = assert.rejects(start, /interrupted by a disconnect/); await tick();
  await f.client.disconnect(); response.resolve({ runId: 'obsolete' }); await rejected;
  assert.equal(f.client.configured, false); assert.equal(f.client.transcript.running, false); assert.equal(f.client.transcript.status, 'idle'); f.client.dispose();
});

test('a drawing change during asynchronous binding prevents a direct MCP mutation', async () => {
  const binding = gate(); let documentId = 'first'; const calls = [];
  const client = new AgentClient({ getDocument: () => documentId, delay: () => 1, clearDelay: () => {}, invoke: async method => { calls.push(method); if (method === 'AgentSetDocument') return binding.promise; return {}; } });
  const call = client.callTool('source_property_set', { value: 2 }, { documentId: 'first' });
  const rejected = assert.rejects(call, /active drawing changed/);
  documentId = 'second'; binding.resolve({ documentId: 'first' }); await rejected;
  assert(!calls.includes('AgentMcpCall')); assert.equal(client.busy, false); client.dispose();
});

test('polls are single-flight and stale replies cannot resurrect reset sessions', async () => {
  const held = gate(); let polls = 0;
  const f = fixture(async method => {
    if (method === 'AgentPoll') { polls++; return held.promise; }
    return {};
  });
  const first = f.client.poll(); await f.client.poll(); assert.equal(polls, 1);
  await f.client.reset(); held.resolve(status([event(1, 'assistant', 'obsolete')], { running: true })); await first;
  assert.equal(f.client.transcript.items.length, 0); assert.equal(f.client.transcript.running, false); assert(f.scheduled.length > 0); f.client.dispose();
});

test('direct MCP calls poll while waiting so approval and UI relay can resolve', async () => {
  const pending = gate();
  const f = fixture(async method => method === 'AgentMcpCall' ? pending.promise : method === 'AgentPoll' ? status([], { running: true }) : {});
  const call = f.client.callTool('write', {}); await tick();
  assert.equal(f.client.busy, true); assert(f.scheduled.some(task => task.ms === 0));
  await f.client.poll(); assert(f.scheduled.some(task => task.ms === 160));
  pending.resolve({ text: 'done', isError: false }); await call; assert.equal(f.client.busy, false); f.client.dispose();
});

test('completed runs still drain paginated events before polling stops', async () => {
  let page = 0;
  const f = fixture(async method => method === 'AgentPoll' ? ++page === 1
    ? status([event(1, 'assistantDelta', 'first')], { latestSequence: 3 })
    : status([event(2, 'assistantDelta', ' last'), event(3, 'assistant', 'first last')], { latestSequence: 3 }) : {});
  await f.client.poll(); assert(f.scheduled.some(task => task.ms === 0)); assert.equal(f.client.transcript.activeAssistant?.text, 'first');
  await f.client.poll(); assert.equal(f.client.transcript.items[0].text, 'first last'); assert.equal(f.client.transcript.activeAssistant, null); f.client.dispose();
});

test('native host relay executes an action once and retries only its acknowledgement', async t => {
  const old = globalThis.shadowStudioAgentUi; t.after(() => { globalThis.shadowStudioAgentUi = old; });
  let edits = 0, replies = 0;
  globalThis.shadowStudioAgentUi = { execute: async (...args) => { edits++; assert.deepEqual(args, ['viewport.pan', { dx: 4 }, 'drawing']); return { x: 4 }; } };
  const request = { id: 'ui-1', action: 'viewport.pan', arguments: { dx: 4 }, expectedDocumentId: 'drawing' };
  const f = fixture(async method => {
    if (method === 'AgentPoll') return status([], { running: true, uiRequests: [request] });
    if (method === 'AgentUiReply' && ++replies === 1) throw new Error('lost acknowledgement');
    return true;
  });
  await assert.rejects(f.client.poll(), /lost acknowledgement/); await f.client.poll();
  assert.equal(edits, 1); assert.equal(replies, 2); f.client.dispose();
});

test('runtime errors are scrubbed using credentials from all configurations', async () => {
  const f = fixture(async method => {
    if (method === 'AgentMcpTools') throw new Error('Error with private-key-value');
    return method === 'AgentPoll' ? status() : {};
  });
  await f.client.configure({ ...defaultConfiguration, apiKey: 'private-key-value' });
  await assert.rejects(f.client.tools(), error => !error.message.includes('private-key-value') && error.message.includes('[redacted]'));
  f.client.dispose();
});

test('HTTP fallback uses explicit typed methods and never puts credentials in a URL', async () => {
  const requests = [];
  const client = new AgentClient({ getDocument: () => 'doc', delay: () => 1, clearDelay: () => {}, fetcher: async (url, options) => {
    requests.push({ url, options }); return new Response(JSON.stringify(url.endsWith('/poll') ? status() : { configured: true }));
  } });
  await client.configure({ ...defaultConfiguration, apiKey: 'private-token' });
  assert.equal(requests[0].url, '/api/agent/configure');
  assert.equal(JSON.parse(requests[0].options.body).arguments[0].apiKey, 'private-token');
  assert(!requests.some(request => request.url.includes('private-token'))); client.dispose();
});

test('ChatGPT companion requires loopback and a separate bearer token', async () => {
  const calls = [];
  const companion = new ChatGptCompanion('http://127.0.0.1:5091', 'companion-token', async (url, options) => {
    calls.push([url, options]); return new Response(JSON.stringify({ authorizationUrl: 'https://auth.openai.com/authorize?state=state' }));
  });
  const result = await companion.login(); assert.equal(new URL(result.authorizationUrl).protocol, 'https:');
  assert.equal(calls[0][0], 'http://127.0.0.1:5091/auth/login');
  assert.equal(calls[0][1].headers.Authorization, 'Bearer companion-token');
  assert(!calls[0][0].includes('companion-token'));
  await assert.rejects(new ChatGptCompanion('http://127.0.0.1:5091', '').status(), /access token/);
  await assert.rejects(new ChatGptCompanion('http://127.0.0.1:5091', 'token', async () => new Response(JSON.stringify({ authorizationUrl: 'http://evil.test' }))).login(), /non-HTTPS/);
});

class Socket extends EventTarget {
  constructor(url) { super(); this.url = url; this.readyState = 0; this.sent = []; }
  send(value) { this.sent.push(JSON.parse(value)); }
  open() { this.readyState = 1; this.dispatchEvent(new Event('open')); }
  receive(value) { const event = new Event('message'); event.data = JSON.stringify(value); this.dispatchEvent(event); }
  close() { this.readyState = 3; const event = new Event('close'); event.code = 1000; this.dispatchEvent(event); }
}
test('external MCP registration has no URL token, duplicates reuse results and tab changes disconnect', async () => {
  let documentId = 'drawing', executions = 0, socket;
  const client = { ensureIdle() {}, secrets: [], bindDocument: async () => {}, tools: async () => [{ name: 'inspect', inputSchema: {} }], clean: value => value,
    callTool: async () => { executions++; return { text: 'result', isError: false }; } };
  const bridge = new StudioMcpBridge({ client, getDocument: () => documentId, socketFactory: url => socket = new Socket(url) });
  const connected = bridge.connect('http://127.0.0.1:5091', 'test-bridge-token'); await tick(); socket.open();
  assert.equal(socket.url, 'ws://127.0.0.1:5091/studio'); assert.equal(socket.sent[0].token, 'test-bridge-token');
  socket.receive({ type: 'registered' }); await connected;
  socket.receive({ type: 'call', id: 'once', name: 'inspect', arguments: {} }); await tick();
  socket.receive({ type: 'call', id: 'once', name: 'inspect', arguments: {} }); await tick();
  assert.equal(executions, 1); assert.equal(socket.sent.filter(message => message.type === 'result').length, 2);
  documentId = 'other'; bridge.documentChanged(); assert.equal(bridge.connected, false); assert.equal(socket.readyState, 3);
});

test('MCP registration cannot finish for a drawing switched while its catalog was loading', async () => {
  const catalog = gate(); let documentId = 'first', sockets = 0;
  const client = { ensureIdle() {}, secrets: [], bindDocument: async () => {}, tools: () => catalog.promise, clean: value => value };
  const bridge = new StudioMcpBridge({ client, getDocument: () => documentId, socketFactory: url => { sockets++; return new Socket(url); } });
  const connecting = bridge.connect('http://127.0.0.1:5091', 'bridge-token'); const rejected = assert.rejects(connecting, /cancelled/); await tick();
  documentId = 'second'; bridge.documentChanged(); catalog.resolve([]); await rejected;
  assert.equal(sockets, 0); assert.equal(bridge.documentId, null); assert.equal(bridge.connected, false);
});

test('explicit MCP disconnect immediately settles a pending registration', async () => {
  let socket;
  const client = { ensureIdle() {}, secrets: [], bindDocument: async () => {}, tools: async () => [], clean: value => value };
  const bridge = new StudioMcpBridge({ client, getDocument: () => 'drawing', socketFactory: url => socket = new Socket(url) });
  const connecting = bridge.connect('http://127.0.0.1:5091', 'bridge-token'); const rejected = assert.rejects(connecting, /cancelled/); await tick();
  socket.open(); bridge.disconnect(); await rejected;
  assert.equal(socket.readyState, 3); assert.equal(bridge.pendingConnect, null);
});

test('queued external tool work is not dispatched after its socket disconnects', async () => {
  let socket, calls = 0;
  const client = { ensureIdle() {}, secrets: [], bindDocument: async () => {}, tools: async () => [], clean: value => value, callTool: async () => { calls++; return { text: 'done' }; } };
  const bridge = new StudioMcpBridge({ client, getDocument: () => 'drawing', socketFactory: url => socket = new Socket(url) });
  const connecting = bridge.connect('http://127.0.0.1:5091', 'bridge-token'); await tick(); socket.open(); socket.receive({ type: 'registered' }); await connecting;
  socket.receive({ type: 'call', id: 'queued', name: 'edit', arguments: {} }); bridge.disconnect(); await tick();
  assert.equal(calls, 0); assert(!socket.sent.some(message => message.type === 'result'));
});

test('a socket error closes pending MCP registration without automatic reconnect', async () => {
  let socket, sockets = 0;
  const client = { ensureIdle() {}, secrets: [], bindDocument: async () => {}, tools: async () => [], clean: value => value };
  const bridge = new StudioMcpBridge({ client, getDocument: () => 'drawing', socketFactory: url => { sockets++; return socket = new Socket(url); } });
  const connecting = bridge.connect('http://127.0.0.1:5091', 'bridge-token'); const rejected = assert.rejects(connecting, /Could not connect/); await tick();
  socket.dispatchEvent(new Event('error')); await rejected;
  assert.equal(socket.readyState, 3); assert.equal(bridge.socket, null); assert.equal(sockets, 1);
});

test('semantic Studio controls validate all settings before mutation and fence target drawings', async t => {
  const previous = globalThis.shadowStudioAgentUi; t.after(() => { globalThis.shadowStudioAgentUi = previous; });
  const settings = { grid: true, ortho: false, polar: false, gridStep: 10, snapModes: 23 };
  const host = { document: { querySelectorAll: () => [], getElementById: () => null }, client: { id: 'a', data: null, busy: false },
    viewport: { camera: { x: 0, y: 0, scale: 1 }, settings, changed() {}, invalidate() {} }, documents: () => [], currentLayer: () => '0', ribbon: () => 'Home', palette: () => 'Properties', retainedSnapshot: () => null, command: () => null, persist() {}, modes() {} };
  const ui = installStudioControl(host);
  await assert.rejects(ui.execute('settings.set', { grid: false, gridStep: -3 }, 'a')); assert.equal(settings.grid, true); assert.equal(settings.gridStep, 10);
  await assert.rejects(ui.execute('viewport.pan', { dx: 10 }, 'other'), /active drawing changed/); assert.equal(host.viewport.camera.x, 0);
  await assert.rejects(ui.execute('ui.action', { name: 'eval' }, 'a'), /Unknown Studio action/);
  await assert.rejects(ui.execute('cad.edit', { path: '../agent/configure', body: {} }, 'a'), /Unsupported/);
  await ui.execute('settings.set', { ortho: true, polar: true }, 'a'); assert.equal(settings.ortho, true); assert.equal(settings.polar, false);
});
