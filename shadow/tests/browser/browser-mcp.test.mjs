import test from 'node:test';
import assert from 'node:assert/strict';
import { MessageChannel } from 'node:worker_threads';
import { BrowserMcpBridge, MessagePortMcpTransport } from '../../samples/netDxf.Shadow.Studio/wwwroot/agents/browser-mcp.mjs';

const catalog = [
  { name: 'shadow_state', description: 'Read the drawing', inputSchema: { type: 'object', properties: {} }, annotations: { readOnlyHint: true } },
  { name: 'fixture_change', description: 'Edit the drawing', inputSchema: { type: 'object', properties: {} }, annotations: { readOnlyHint: false } }
];
function fixture() {
  let next = 0;
  const calls = [], closed = [], sessions = new Set();
  const client = {
    ensureIdle() {}, async bindDocument() { return 'demo'; }, clean: value => value, schedule() {},
    async invoke(method, ...args) {
      calls.push([method, ...args]);
      if (method === 'AgentMcpSessionOpen') { const sessionId = `fixture-${++next}`; sessions.add(sessionId); return { sessionId }; }
      if (method === 'AgentMcpSessionClose') { closed.push(args[0]); sessions.delete(args[0]); return; }
      assert.equal(method, 'AgentMcpSessionProcess'); assert(sessions.has(args[0]));
      const request = JSON.parse(args[1]);
      if (!Object.hasOwn(request, 'id')) return null;
      const result = request.method === 'initialize' ? { protocolVersion: '2025-11-25', capabilities: { tools: {} }, serverInfo: { name: 'fixture', version: '1.0.0' } }
        : request.method === 'tools/list' ? { tools: catalog }
          : { content: [{ type: 'text', text: JSON.stringify({ tool: request.params.name, args: request.params.arguments }) }], isError: false };
      return JSON.stringify({ jsonrpc: '2.0', id: request.id, result });
    }
  };
  return { client, calls, closed, sessions };
}
function webContext({ legacy = false, failAt = null } = {}) {
  const tools = new Map(), removed = [];
  return {
    tools, removed,
    async registerTool(tool, options = {}) {
      if (tool.name === failAt) throw new Error('Tool registration fixture failed.');
      tools.set(tool.name, tool);
      if (!legacy) options.signal?.addEventListener('abort', () => tools.delete(tool.name), { once: true });
    },
    ...(legacy ? { unregisterTool(name) { tools.delete(name); removed.push(name); } } : {})
  };
}
function bridgeFor(value, context = null, getDocument = () => 'demo') {
  const globalObject = {};
  const bridge = new BrowserMcpBridge(value.client, getDocument, { globalObject, getModelContext: () => context, createChannel: () => new MessageChannel() });
  return { bridge, globalObject };
}
function receive(port, request) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { port.removeEventListener('message', onMessage); reject(new Error('MessagePort fixture timed out.')); }, 3000);
    const onMessage = event => { clearTimeout(timeout); port.removeEventListener('message', onMessage); resolve(event.data); };
    port.addEventListener('message', onMessage); port.start(); port.postMessage(request);
  });
}

test('browser MCP is explicitly enabled and disabled without any companion or provider credential', async () => {
  const value = fixture(), { bridge, globalObject } = bridgeFor(value);
  assert.equal(globalObject.shadowStudioBrowserMcp, undefined);
  await assert.rejects(bridge.createConnection(), /disabled/);
  const status = await bridge.enable();
  assert.equal(status.enabled, true); assert.equal(status.webMcpAvailable, false);
  assert.deepEqual(Object.keys(globalObject.shadowStudioBrowserMcp).sort(), ['connect', 'createConnection', 'status']);
  assert.equal(value.calls.length, 0);
  await bridge.disable(); assert.equal(globalObject.shadowStudioBrowserMcp, undefined);
  await assert.rejects(bridge.createConnection(), /disabled/);
  await bridge.dispose();
});

test('MessagePort forwards JSON-RPC to the .NET session and provides a client SDK transport', async () => {
  const value = fixture(), { bridge, globalObject } = bridgeFor(value);
  await bridge.enable();
  const connection = await globalObject.shadowStudioBrowserMcp.createConnection();
  const transport = new MessagePortMcpTransport(connection.port);
  await transport.start();
  const reply = new Promise(resolve => { transport.onmessage = resolve; });
  await transport.send({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'test', version: '1.0' } } });
  assert.equal((await reply).result.serverInfo.name, 'fixture');
  assert.equal(bridge.status.messagePorts, 1);
  assert(value.calls.some(call => call[0] === 'AgentMcpSessionProcess'));
  await transport.close();
  await new Promise(resolve => setImmediate(resolve));
  await connection.close(); assert.equal(bridge.status.messagePorts, 0);
  assert.equal(value.sessions.size, 0); await bridge.dispose();
});

test('invalid and oversized MessagePort payloads do not reach the .NET tool boundary', async () => {
  const value = fixture(), { bridge } = bridgeFor(value);
  await bridge.enable(); const connection = await bridge.createConnection();
  const before = value.calls.length;
  assert.equal((await receive(connection.port, [])).error.code, -32600);
  assert.equal((await receive(connection.port, 'x'.repeat(1_048_577))).error.code, -32600);
  assert.equal(value.calls.length, before);
  connection.port.close(); await bridge.dispose();
});

test('WebMCP registers the official catalog with permission annotations and forwards execution', async () => {
  const value = fixture(), context = webContext(), { bridge } = bridgeFor(value, context);
  const status = await bridge.enable();
  assert.equal(status.webMcpRegistered, 2); assert.equal(status.error, null);
  assert.equal(context.tools.get('shadow_state').annotations.readOnlyHint, true);
  assert.equal(context.tools.get('fixture_change').annotations.consequentialHint, true);
  const result = await context.tools.get('fixture_change').execute({ value: 12 });
  assert.deepEqual(JSON.parse(result.content[0].text), { tool: 'fixture_change', args: { value: 12 } });
  const request = value.calls.filter(call => call[0] === 'AgentMcpSessionProcess').map(call => JSON.parse(call[2])).find(call => call.method === 'tools/call');
  assert.equal(request.params.name, 'fixture_change');
  await bridge.disable(); assert.equal(context.tools.size, 0); assert.equal(value.sessions.size, 0);
});

test('legacy WebMCP cleanup removes only tools registered by this Studio connection', async () => {
  const value = fixture(), context = webContext({ legacy: true }), { bridge } = bridgeFor(value, context);
  context.tools.set('unrelated', { name: 'unrelated' });
  await bridge.enable(); await bridge.disable();
  assert.deepEqual([...context.tools.keys()], ['unrelated']);
  assert.deepEqual(context.removed.sort(), ['fixture_change', 'shadow_state']);
});

test('partial WebMCP registration failure rolls back its tools but keeps MessagePort available', async () => {
  const value = fixture(), context = webContext({ failAt: 'fixture_change' }), { bridge, globalObject } = bridgeFor(value, context);
  const status = await bridge.enable();
  assert.equal(status.enabled, true); assert.equal(status.webMcpRegistered, 0); assert.match(status.error, /fixture failed/);
  assert.equal(context.tools.size, 0); assert.equal(value.sessions.size, 0);
  const connection = await globalObject.shadowStudioBrowserMcp.createConnection();
  assert.equal(bridge.status.messagePorts, 1);
  connection.port.close(); await bridge.dispose(); assert.equal(value.sessions.size, 0);
});

test('drawing changes remove WebMCP registrations and close all MessagePorts', async () => {
  const value = fixture(), context = webContext(); let documentId = 'demo';
  const { bridge, globalObject } = bridgeFor(value, context, () => documentId);
  await bridge.enable(); const oldTool = context.tools.get('fixture_change'); const connection = await bridge.createConnection();
  documentId = 'second'; await bridge.documentChanged();
  assert.equal(bridge.status.enabled, false); assert.equal(context.tools.size, 0); assert.equal(value.sessions.size, 0);
  assert.equal(globalObject.shadowStudioBrowserMcp, undefined);
  assert.throws(() => oldTool.execute({}), /disabled/);
  connection.port.close(); await bridge.dispose();
});

test('WebMCP AbortSignal sends targeted MCP cancellation and rejects promptly', async () => {
  const value = fixture(), context = webContext(), originalInvoke = value.client.invoke;
  let started;
  const waiting = new Promise(resolve => { started = resolve; });
  value.client.invoke = async (method, ...args) => {
    if (method === 'AgentMcpSessionProcess') {
      const request = JSON.parse(args[1]);
      if (request.method === 'tools/call') { value.calls.push([method, ...args]); started(); return new Promise(() => {}); }
    }
    return originalInvoke(method, ...args);
  };
  const { bridge } = bridgeFor(value, context); await bridge.enable();
  const controller = new AbortController();
  const pending = context.tools.get('fixture_change').execute({}, { signal: controller.signal });
  await waiting; controller.abort(); await assert.rejects(pending, { name: 'AbortError' });
  const requests = value.calls.filter(call => call[0] === 'AgentMcpSessionProcess').map(call => JSON.parse(call[2]));
  const cancel = requests.find(request => request.method === 'notifications/cancelled');
  assert.equal(cancel.params.requestId, requests.find(request => request.method === 'tools/call').id);
  await bridge.dispose();
});

test('an already aborted WebMCP call never reaches the SDK', async () => {
  const value = fixture(), context = webContext(), { bridge } = bridgeFor(value, context);
  await bridge.enable(); const before = value.calls.length;
  const signal = AbortSignal.abort();
  await assert.rejects(context.tools.get('fixture_change').execute({}, { signal }), { name: 'AbortError' });
  assert.equal(value.calls.length, before); await bridge.dispose();
});

test('disconnect during session opening disposes the late connection and cannot restore exposure', async () => {
  const value = fixture(), context = webContext(), originalInvoke = value.client.invoke;
  let release, entered;
  const gate = new Promise(resolve => { release = resolve; }), started = new Promise(resolve => { entered = resolve; });
  value.client.invoke = async (method, ...args) => {
    if (method === 'AgentMcpSessionOpen') { entered(); await gate; }
    return originalInvoke(method, ...args);
  };
  const { bridge, globalObject } = bridgeFor(value, context);
  const enabling = bridge.enable(); await started; await bridge.disable(); release();
  await assert.rejects(enabling, /disabled/);
  assert.equal(globalObject.shadowStudioBrowserMcp, undefined); assert.equal(context.tools.size, 0); assert.equal(value.sessions.size, 0);
  await bridge.dispose();
});

test('browser MCP bounds concurrent MessagePort grants and releases capacity on close', async () => {
  const value = fixture(), { bridge } = bridgeFor(value);
  await bridge.enable(); const connections = await Promise.all(Array.from({ length: 4 }, () => bridge.createConnection()));
  await assert.rejects(bridge.createConnection(), /At most four/);
  await connections[0].close(); connections[0].port.close();
  const replacement = await bridge.createConnection(); assert.equal(bridge.status.messagePorts, 4);
  for (const connection of [...connections, replacement]) connection.port.close();
  await bridge.dispose(); assert.equal(value.sessions.size, 0);
});

test('a disposed browser MCP bridge cannot be enabled or reused', async () => {
  const { bridge } = bridgeFor(fixture()); await bridge.dispose();
  await assert.rejects(bridge.enable(), /closed/); await assert.rejects(bridge.createConnection(), /disabled/);
});

test('the same MessagePort cannot be attached twice, including during an asynchronous grant', async () => {
  const value = fixture(), { bridge } = bridgeFor(value), originalInvoke = value.client.invoke;
  let release, entered;
  const gate = new Promise(resolve => { release = resolve; }), started = new Promise(resolve => { entered = resolve; });
  value.client.invoke = async (method, ...args) => {
    if (method === 'AgentMcpSessionOpen') { entered(); await gate; }
    return originalInvoke(method, ...args);
  };
  await bridge.enable(); const channel = new MessageChannel();
  const opening = bridge.connect(channel.port1); await started;
  await assert.rejects(bridge.connect(channel.port1), /already connected/);
  release(); const connection = await opening;
  await assert.rejects(bridge.connect(channel.port1), /already connected/);
  await receive(channel.port2, { jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} });
  assert.equal(value.calls.filter(call => call[0] === 'AgentMcpSessionOpen').length, 1);
  assert.equal(value.calls.filter(call => call[0] === 'AgentMcpSessionProcess').length, 1);
  await connection.close(); channel.port2.close(); await bridge.dispose();
});

test('drawing events cancel a pending activation even if the drawing changes back before binding completes', async () => {
  const value = fixture(); let release, entered, documentId = 'demo';
  const gate = new Promise(resolve => { release = resolve; }), started = new Promise(resolve => { entered = resolve; });
  value.client.bindDocument = async () => { entered(); await gate; return 'demo'; };
  const { bridge, globalObject } = bridgeFor(value, null, () => documentId);
  const enabling = bridge.enable(); await started;
  documentId = 'second'; await bridge.documentChanged(); documentId = 'demo'; release();
  await assert.rejects(enabling, /cancelled/);
  assert.equal(bridge.enabled, false); assert.equal(globalObject.shadowStudioBrowserMcp, undefined);
  assert.equal(value.calls.length, 0); await bridge.dispose();
});

test('server revocation closes a MessagePort SDK transport promptly and exactly once', async () => {
  const value = fixture(), { bridge } = bridgeFor(value); await bridge.enable();
  const connection = await bridge.createConnection(); const transport = new MessagePortMcpTransport(connection.port);
  let closeCount = 0, closed;
  const stopped = new Promise(resolve => { closed = resolve; });
  transport.onclose = () => { closeCount++; closed(); };
  transport.onmessage = () => assert.fail('Transport disconnect notifications must not be forwarded as ordinary MCP messages.');
  await transport.start(); await bridge.disable();
  await Promise.race([stopped, new Promise((_, reject) => { const timer = setTimeout(() => reject(new Error('Revoked transport did not close.')), 1000); timer.unref(); })]);
  assert.equal(transport.closed, true); assert.equal(closeCount, 1);
  await transport.close(); assert.equal(closeCount, 1); await bridge.dispose();
});
