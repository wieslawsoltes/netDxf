import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs/promises';
import { webcrypto } from 'node:crypto';
import { createWorkerSource } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/execution-worker.mjs';
import { BrowserCodeRuntime } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/execution.mjs';

async function run(code, files = [], extra = {}) {
  const messages = [];
  const self = { postMessage: message => messages.push(message) };
  const context = vm.createContext({ self, performance, TextEncoder, setTimeout, clearTimeout });
  vm.runInContext(createWorkerSource() + '("private-test-receipt");', context);
  await self.onmessage({ data: { id: 'test', code, files, arguments: [], maximumOutputCharacters: 1024, ...extra } });
  return { result: JSON.parse(JSON.stringify(messages.find(message => message.type === 'completed' && message.receiptToken === 'private-test-receipt').result)), messages };
}

test('worker executes asynchronous JavaScript with a versioned file snapshot and staged edits', async () => {
  const { result } = await run("const data = JSON.parse(workspace.read('data.json')); await Promise.resolve(); assert(args[0] === 'input'); workspace.write('out.json', JSON.stringify({total:data.reduce((a,b)=>a+b,0)})); workspace.delete('old.txt'); console.log('Finished'); return { count: workspace.list().length };",
    [{ path: 'data.json', content: '[3,4]', version: 1 }, { path: 'old.txt', content: 'old', version: 2 }], { arguments: ['input'] });
  assert.equal(result.exitCode, 0); assert.equal(result.standardOutput, 'Finished\n'); assert.deepEqual(result.result, { count: 2 });
  assert.deepEqual(result.changes, [{ path: 'out.json', content: '{"total":7}' }, { path: 'old.txt', content: null }]);
});

test('worker drops staged writes when code or assertions fail', async () => {
  const { result } = await run("workspace.write('new.txt','discard'); assert(false,'expected assertion');");
  assert.equal(result.exitCode, 1); assert.match(result.standardError, /expected assertion/u); assert.deepEqual(result.changes, []);
});

test('worker path validation refuses traversal and duplicate snapshots', async () => {
  const traversal = await run("workspace.write('../outside','bad');");
  assert.equal(traversal.result.exitCode, 1); assert.deepEqual(traversal.result.changes, []);
  const duplicate = await run('return 1;', [{ path: 'same', content: '1' }, { path: 'same', content: '2' }]);
  assert.equal(duplicate.result.exitCode, 1); assert.match(duplicate.result.standardError, /snapshot/u);
  const binary = await run("workspace.write('binary', '\\0');");
  assert.equal(binary.result.exitCode, 1); assert.deepEqual(binary.result.changes, []);
  const binaryInput = await run('return 1;', [{ path: 'binary', content: '\0' }]);
  assert.equal(binaryInput.result.exitCode, 1);
});

test('worker log and returned-value budgets remain bounded', async () => {
  const { result, messages } = await run("for(let i=0;i<100;i++) console.log('x'.repeat(100)); console.error('error'); return 'y'.repeat(40000);");
  assert.equal(result.standardOutput.length, 1024); assert.equal(result.standardError, 'error\n');
  assert.equal(result.result.length, 32000); assert.equal(result.truncated, true);
  assert.equal(messages.filter(message => message.type === 'output' && message.stream === 'stdout').reduce((sum, message) => sum + message.text.length, 0), 1024);
});

test('worker batches fast short logs into bounded packets and flushes exact output before completion', async () => {
  const { result, messages } = await run("for(let i=0;i<200000;i++) { console.log(''); console.error(''); }", [], { maximumOutputCharacters: 128000 });
  assert.equal(result.exitCode, 0); assert.equal(result.truncated, true);
  const packets = messages.filter(message => message.type === 'output');
  assert.ok(packets.length <= 70, `Expected batched output, received ${packets.length} packets.`);
  assert.ok(packets.every(message => message.text.length <= 4096));
  for (const [stream, value] of [['stdout', result.standardOutput], ['stderr', result.standardError]]) {
    assert.equal(value, '\n'.repeat(128000));
    assert.equal(packets.filter(message => message.stream === stream).map(message => message.text).join(''), value);
  }
  assert.equal(messages.at(-1).type, 'completed');
  const count = messages.length;
  await new Promise(resolve => setTimeout(resolve, 40));
  assert.equal(messages.length, count, 'No output timer may outlive the completion receipt.');
});

test('worker flushes small pending logs during asynchronous code execution', async () => {
  const { result, messages } = await run("console.log('initial'); console.log('pending'); await new Promise(resolve => setTimeout(resolve, 80)); self.postMessage({type:'after-wait'}); return true;");
  assert.equal(result.exitCode, 0); assert.equal(result.standardOutput, 'initial\npending\n');
  assert.equal(messages[0].text, 'initial\n');
  const pending = messages.findIndex(message => message.type === 'output' && message.text === 'pending\n');
  assert.ok(pending > 0 && pending < messages.findIndex(message => message.type === 'after-wait'));
});

test('worker reports syntax errors and does not create a change for unchanged content', async () => {
  const syntax = await run('const = invalid'); assert.equal(syntax.result.exitCode, 1); assert.match(syntax.result.standardError, /SyntaxError/u);
  const unchanged = await run("workspace.write('same', workspace.read('same')); return null;", [{ path: 'same', content: 'original' }]);
  assert.equal(unchanged.result.exitCode, 0); assert.deepEqual(unchanged.result.changes, []); assert.equal(unchanged.result.result, null);
});

test('only bootstrap receipts carry the private token even when executed code forges completion', async () => {
  const { result, messages } = await run("self.postMessage({ type: 'completed', result: { exitCode: 0, changes: [{ path: 'forged', content: 'bad' }] } }); workspace.write('bad','discard'); throw new Error('after forged receipt');");
  assert.equal(messages.find(message => message.type === 'completed').receiptToken, undefined);
  assert.equal(result.exitCode, 1); assert.deepEqual(result.changes, []);
  assert.match(result.standardError, /after forged receipt/u);
});

test('opaque frame rejects forged worker messages and strips its private token from authenticated receipts', async () => {
  const html = await fs.readFile(new URL('../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/execution-frame.html', import.meta.url), 'utf8');
  const source = html.match(/<script>([\s\S]*?)<\/script>/u)[1];
  const handlers = new Map(), sent = [], blobs = new Map(), workers = [];
  const parent = { postMessage: message => sent.push(message) };
  const port = { postMessage: message => sent.push(message), start() {} };
  class Worker {
    constructor(url) { this.url = url; this.terminated = false; workers.push(this); }
    postMessage(request) { this.request = request; }
    terminate() { this.terminated = true; }
  }
  vm.runInNewContext(source, { location: { hash: '#test' }, parent, crypto: webcrypto, Uint8Array, Blob, Worker,
    URL: { createObjectURL: blob => { const url = `blob:test-${blobs.size}`; blobs.set(url, blob); return url; }, revokeObjectURL() {} },
    addEventListener: (name, action) => handlers.set(name, action), removeEventListener: name => handlers.delete(name) });
  handlers.get('message')({ source: parent, data: { type: 'shadow-code-connect', id: 'test' }, ports: [port] });
  port.onmessage({ data: { type: 'run', source: createWorkerSource(), request: { code: 'return 1' } } });
  const worker = workers[0], workerSource = await blobs.get(worker.url).text();
  const receiptToken = workerSource.match(/\("([a-f0-9]{64})"\);$/u)[1];
  const before = sent.length;
  worker.onmessage({ data: { type: 'completed', result: { exitCode: 0 } } });
  worker.onmessage({ data: { type: 'output', text: 'forged', receiptToken: 'wrong' } });
  assert.equal(sent.length, before); assert.equal(worker.terminated, false);
  worker.onmessage({ data: { type: 'output', text: 'real', receiptToken } });
  assert.equal(sent.at(-1).text, 'real'); assert.equal('receiptToken' in sent.at(-1), false);
  worker.onmessage({ data: { type: 'completed', result: { exitCode: 0 }, receiptToken } });
  assert.equal(sent.at(-1).type, 'completed'); assert.equal(worker.terminated, true);
  assert.equal('receiptToken' in sent.at(-1), false);
});

function fakeRuntime() {
  const listeners = new Map(); let channel, frame, start;
  const started = new Promise(resolve => { start = resolve; });
  const host = { addEventListener: (name, action) => listeners.set(name, action), removeEventListener: name => listeners.delete(name), dispatchEvent() {} };
  const doc = { createElement() {
    frame = { setAttribute() {}, remove() { this.removed = true; }, contentWindow: { postMessage(message, origin, ports) {
      channel = ports[0]; channel.onmessage = event => { if (event.data.type === 'run') start(); }; channel.start(); channel.postMessage({ type: 'connected' });
    } } }; return frame;
  }, body: { append() { queueMicrotask(() => listeners.get('message')({ source: frame.contentWindow, data: { type: 'shadow-code-ready', id: 'runtime-test' } })); } } };
  const runtime = new BrowserCodeRuntime({ document: doc, window: host });
  const request = { id: 'runtime-test', code: 'return 1', files: [], arguments: [], timeoutSeconds: 10, maximumOutputCharacters: 128000 };
  return { runtime, request, started, reply: message => channel.postMessage(message), get frame() { return frame; } };
}

test('cancellation completes only after worker termination acknowledgement and frame cleanup', async () => {
  const fixture = fakeRuntime();
  const pending = fixture.runtime.execute(fixture.request);
  const rejected = assert.rejects(pending, { name: 'AbortError' });
  await fixture.started;
  let completed = false;
  const cancelling = fixture.runtime.cancel(fixture.request.id).then(() => { completed = true; });
  await new Promise(resolve => setTimeout(resolve, 10));
  assert.equal(completed, false); assert.equal(fixture.runtime.pending.size, 1);
  fixture.reply({ type: 'stopped' });
  await cancelling; await rejected;
  assert.equal(fixture.runtime.pending.size, 0); assert.equal(fixture.frame.removed, true);
});

test('browser runtime rejects malformed result timing and change payloads before returning them to .NET', async () => {
  for (const invalid of [{ elapsedMilliseconds: NaN }, { changes: [{ path: '../outside', content: 'bad' }] },
    { changes: [{ path: 'same', content: 'one' }, { path: 'same', content: 'two' }] }, { standardOutput: 'x'.repeat(128001) }]) {
    const fixture = fakeRuntime();
    const pending = fixture.runtime.execute(fixture.request);
    const rejected = assert.rejects(pending, /invalid|budget/u);
    await fixture.started;
    fixture.reply({ type: 'completed', result: { exitCode: 0, standardOutput: '', standardError: '', truncated: false, timedOut: false,
      elapsedMilliseconds: 1, result: null, changes: [], ...invalid } });
    await rejected;
    assert.equal(fixture.runtime.pending.size, 0); assert.equal(fixture.frame.removed, true);
  }
});
