import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deflateRawSync } from 'node:zlib';
import { BrowserFolder, importPlan, lineDiff, normalizeFiles, parseWorkspaceBundle, readSelectedFiles, readWorkspaceZip, workspaceBundle, workspaceZip } from '../../samples/netDxf.Shadow.Studio/wwwroot/agents/file-transfer.mjs';
import { BrowserSessionPersistence, IndexedDbAgentStore, checkpointRecord, storageError } from '../../samples/netDxf.Shadow.Studio/wwwroot/agents/persistence.mjs';
import { AgentClient } from '../../samples/netDxf.Shadow.Studio/wwwroot/agents/client.mjs';

const gate = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
const tick = () => new Promise(resolve => setImmediate(resolve));
const files = [{ path: 'src/main.mjs', content: 'console.log("hello");\n', version: 8 }, { path: 'notes/日本語.md', content: 'Hello 🦊\n', version: 4 }];
const checkpoint = (text = 'Task') => JSON.stringify({ formatVersion: 1, sessionId: 'fixture', messages: [{ role: 'user', contents: [{ kind: 'text', text }] }], files: [], pins: [], plan: [] });

test('project bundle round trips Unicode and versions are refreshed by import rather than serialized into the bundle', () => {
  const encoded = workspaceBundle(files), decoded = parseWorkspaceBundle(encoded);
  assert.deepEqual(decoded, normalizeFiles(files).map(({ path, content }) => ({ path, content })));
  assert(!encoded.includes('"version": 8')); assert(encoded.includes('"format": "netdxf.shadow.workspace"'));
  for (const invalid of ['{}', '[]', '{', JSON.stringify({ format: 'netdxf.shadow.workspace', version: 2, files: [] })]) assert.throws(() => parseWorkspaceBundle(invalid));
});

test('project validation rejects traversal, duplicate paths, binary input and UTF-8 byte budget overflow', () => {
  for (const path of ['../host', '/host', 'a/../b', 'a//b', 'C:host', 'a\0b']) assert.throws(() => normalizeFiles([{ path, content: '' }]));
  assert.throws(() => normalizeFiles([{ path: 'a', content: 'a' }, { path: 'a', content: 'b' }]), /Duplicate/);
  assert.throws(() => normalizeFiles([{ path: 'a', content: '\0binary' }]), /UTF-8/);
  assert.throws(() => normalizeFiles(Array.from({ length: 513 }, (_, index) => ({ path: `${index}.txt`, content: '' }))), /512/);
  assert.throws(() => normalizeFiles([{ path: 'a', content: '🦊'.repeat(4 * 1024 * 1024 + 1) }]), /16 MiB/);
});

test('reviewed imports preserve omitted files and carry original CAS versions for every selected change', () => {
  const plan = importPlan([{ path: 'a', content: 'old', version: 2 }, { path: 'b', content: 'same', version: 4 }, { path: 'retained', content: 'keep', version: 9 }],
    [{ path: 'a', content: 'new' }, { path: 'b', content: 'same' }, { path: 'c', content: 'created' }]);
  assert.equal(plan.added, 1); assert.equal(plan.modified, 1); assert.equal(plan.unchanged, 1);
  assert.deepEqual(plan.changes, [{ path: 'a', content: 'new', expectedVersion: 2 }, { path: 'c', content: 'created', expectedVersion: 0 }]);
  assert(!plan.changes.some(change => change.path === 'retained'));
});

test('diff output is bounded and shows both removal and addition without quadratic matching', () => {
  const diff = lineDiff(Array.from({ length: 20000 }, () => 'old').join('\n'), Array.from({ length: 20000 }, () => 'new').join('\n'));
  assert(diff.lines.length <= 180); assert(diff.omitted > 39000);
  assert(diff.lines.some(line => line.kind === 'removed')); assert(diff.lines.some(line => line.kind === 'added'));
  assert.equal(lineDiff('same', 'same').lines.length, 0);
  assert.deepEqual(lineDiff('a\nb\nc', 'a\nnew\nc').lines.map(line => line.kind), ['context', 'removed', 'added', 'context']);
});

test('portable ZIP round trips Unicode with CRC validation and no compression dependency', async () => {
  const zip = workspaceZip(files), result = await readWorkspaceZip(zip);
  assert.deepEqual(result.files, normalizeFiles(files).map(({ path, content }) => ({ path, content })));
  assert.equal(result.skipped.length, 0);
  const corrupted = zip.slice(); corrupted[30 + new DataView(zip.buffer).getUint16(26, true)] ^= 1;
  await assert.rejects(readWorkspaceZip(corrupted), /checksum/);
  assert.deepEqual((await readWorkspaceZip(workspaceZip([]))).files, []);
});

test('ZIP directory traversal, symlinks, encryption and size bombs are rejected before file writes', async () => {
  const initial = workspaceZip([{ path: 'safe.txt', content: 'contents' }]);
  const directory = new DataView(initial.buffer).getUint32(initial.length - 6, true);
  for (const mutate of [
    value => value.set(new TextEncoder().encode('../x.txt'), directory + 46),
    value => new DataView(value.buffer).setUint32(directory + 38, 0xa000 << 16, true),
    value => new DataView(value.buffer).setUint16(directory + 8, 1, true),
    value => new DataView(value.buffer).setUint32(directory + 24, 32 * 1024 * 1024, true)
  ]) { const value = initial.slice(); mutate(value); await assert.rejects(readWorkspaceZip(value)); }
});

test('ZIP deflated entries use a bounded decompression contract and still verify their expected CRC', async () => {
  const content = 'compressible\n'.repeat(100), initial = workspaceZip([{ path: 'test.txt', content }]);
  const localSize = 30 + 8, raw = new TextEncoder().encode(content), compressed = deflateRawSync(raw);
  const oldDirectory = localSize + raw.length, central = initial.slice(oldDirectory, initial.length - 22), end = initial.slice(-22);
  const local = initial.slice(0, localSize), lv = new DataView(local.buffer), cv = new DataView(central.buffer), ev = new DataView(end.buffer);
  lv.setUint16(8, 8, true); lv.setUint32(18, compressed.length, true); cv.setUint16(10, 8, true); cv.setUint32(20, compressed.length, true); ev.setUint32(16, localSize + compressed.length, true);
  const zip = new Uint8Array(local.length + compressed.length + central.length + end.length); let offset = 0;
  for (const part of [local, compressed, central, end]) { zip.set(part, offset); offset += part.length; }
  let called = false;
  const result = await readWorkspaceZip(zip, { inflateEntry: async (data, expected) => { called = true; assert.equal(expected, raw.length); assert.deepEqual(data, new Uint8Array(compressed)); return raw; } });
  assert(called); assert.equal(result.files[0].content, content);
  await assert.rejects(readWorkspaceZip(zip, { inflateEntry: async () => new Uint8Array(raw.length + 1) }), /checksum/);
});

test('folder upload preserves relative paths, skips generated directories and reports invalid UTF-8', async () => {
  const makeFile = (name, relative, content) => ({ name, webkitRelativePath: relative, size: content.byteLength,
    arrayBuffer: async () => content.buffer.slice(content.byteOffset, content.byteOffset + content.byteLength) });
  const result = await readSelectedFiles([
    makeFile('main.cs', 'project/src/main.cs', new TextEncoder().encode('class Main {}')),
    makeFile('generated.cs', 'project/obj/generated.cs', new TextEncoder().encode('generated')),
    makeFile('image.bin', 'project/image.bin', Uint8Array.of(0xff, 0xff))
  ], { directory: true });
  assert.deepEqual(result.files, [{ path: 'src/main.cs', content: 'class Main {}' }]); assert.equal(result.skipped.length, 2);
});

function folderFixture(initial) {
  const data = new Map(Object.entries(initial)), writes = [], directories = new Set(['']); let permission = 'granted', failPath = null;
  const missing = () => new DOMException('Missing file', 'NotFoundError');
  const file = path => ({ kind: 'file', name: path.split('/').at(-1), getFile: async () => {
    if (!data.has(path)) throw missing(); const value = new TextEncoder().encode(data.get(path)); return { size: value.length, arrayBuffer: async () => value.buffer };
  }, createWritable: async () => {
    let next;
    return { write: async value => { if (path === failPath) throw new Error('Disk full'); next = value; }, close: async () => { data.set(path, next); writes.push(path); }, abort: async () => {} };
  } });
  const directory = prefix => ({ kind: 'directory', name: prefix || 'project', queryPermission: async () => permission, requestPermission: async () => permission,
    getDirectoryHandle: async (name, { create } = {}) => { const path = `${prefix}${name}/`; if (create || [...data.keys()].some(key => key.startsWith(path))) directories.add(path); if (!directories.has(path)) throw missing(); return directory(path); },
    getFileHandle: async (name, { create } = {}) => { const path = `${prefix}${name}`; if (!data.has(path) && !create) throw missing(); return file(path); },
    async *entries() {
      const yielded = new Set();
      for (const path of data.keys()) if (path.startsWith(prefix)) {
        const part = path.slice(prefix.length).split('/')[0]; if (yielded.has(part)) continue; yielded.add(part);
        const nested = path.slice(prefix.length).includes('/'); yield [part, nested ? directory(`${prefix}${part}/`) : file(`${prefix}${part}`)];
      }
    }
  });
  return { data, writes, folder: new BrowserFolder(directory('')), permission: value => { permission = value; }, fail: path => { failPath = path; } };
}

test('folder import ignores generated directories and captures a baseline for external edit detection', async () => {
  const f = folderFixture({ 'src/a.cs': 'old', 'b.txt': 'same', 'obj/cache': 'ignored' });
  const result = await f.folder.read(); assert.equal(result.files.length, 2); assert.equal(result.skipped.length, 1);
  f.data.set('src/a.cs', 'changed outside');
  const plan = await f.folder.planWrite([{ path: 'src/a.cs', content: 'agent change' }, { path: 'b.txt', content: 'same' }, { path: 'new.txt', content: 'new' }]);
  assert.equal(plan.length, 2); assert.equal(plan.find(row => row.path === 'src/a.cs').conflict, true); assert.equal(plan.find(row => row.path === 'new.txt').conflict, false);
  assert.equal(f.writes.length, 0);
});

test('folder publishing checks all reviewed contents before any writes and detects races before each file', async () => {
  const f = folderFixture({ a: 'old', b: 'old' }); await f.folder.read();
  const plan = await f.folder.planWrite([{ path: 'a', content: 'new' }, { path: 'b', content: 'new' }]); f.data.set('b', 'external');
  await assert.rejects(f.folder.write(plan), /changed after review/); assert.equal(f.writes.length, 0); assert.equal(f.data.get('a'), 'old');
});

test('folder write failures return completed file receipts without replaying earlier writes', async () => {
  const f = folderFixture({ a: 'old', b: 'old' }); await f.folder.read();
  const plan = await f.folder.planWrite([{ path: 'a', content: 'new' }, { path: 'b', content: 'new' }]); f.fail('b');
  await assert.rejects(f.folder.write(plan), error => error.written?.join() === 'a' && /already saved/.test(error.message));
  assert.deepEqual(f.writes, ['a']); assert.equal(f.data.get('a'), 'new'); assert.equal(f.data.get('b'), 'old');
});

test('folder permission denial is explicit and performs no read or write operation', async () => {
  const f = folderFixture({ a: 'private' }); f.permission('denied');
  await assert.rejects(f.folder.read(), /permission was not granted/);
  await assert.rejects(f.folder.write([{ path: 'a', content: 'new', expectedContent: 'private' }]), /permission was not granted/);
  assert.equal(f.data.get('a'), 'private');
});

test('saved checkpoint records allowlist fields and redact keys in conversation, draft, metadata and nested data', () => {
  const record = checkpointRecord({ id: 'save', checkpoint: JSON.stringify({ messages: [{ text: 'key=private-token' }], apiKey: 'secret', provider: { access_token: 'token' }, files: [] }),
    draft: { path: 'src/key.txt', content: 'private-token', baseContent: 'private-token', baseVersion: 2, apiKey: 'unknown-secret' }, label: 'private-token', apiKey: 'unknown-secret', endpoint: 'https://private.example' }, ['private-token']);
  const json = JSON.stringify(record);
  assert(!json.includes('private-token')); assert(!json.includes('unknown-secret')); assert(!json.includes('apiKey')); assert(!json.includes('access_token')); assert(!json.includes('private.example'));
  assert.equal(record.draft.baseVersion, 2); assert(record.bytes > 0);
});

test('browser checkpoints retain compacted tool receipts without retaining nested credentials', () => {
  const receipts = [{ callId: 'write-1', signature: 'workspace_write:{"path":"result.txt","content":"done"}', isError: false },
    { callId: 'write-2', signature: 'workspace_write:{"path":"result.txt","content":"next"}', isError: true, authorization: 'Bearer excluded' }];
  const record = checkpointRecord({ id: 'receipts', checkpoint: JSON.stringify({ messages: [], files: [], receipts }) });
  assert.deepEqual(JSON.parse(record.checkpoint).receipts, receipts.map(({ authorization, ...receipt }) => receipt));
  assert(!record.checkpoint.includes('Bearer excluded'));
});

function persistenceFixture({ text = 'Task', draft = null, exportGate = null, saveGate = null } = {}) {
  class Client extends EventTarget {
    constructor() { super(); this.busy = false; this.connected = true; this.transcript = { running: false }; this.secrets = ['private-token']; this.exports = 0; this.imports = []; }
    ensureIdle() { if (this.busy || this.transcript.running) throw new Error('Active run'); }
    async exportSession() { this.exports++; if (exportGate) await exportGate.promise; return checkpoint(text); }
    async importSession(value) { this.imports.push(value); }
  }
  const client = new Client(), records = new Map(), saves = [], timers = [], restored = [];
  const store = { async save(record) { if (saveGate) await saveGate.promise; records.set(record.id, record); saves.push(record); }, async get(id) { return records.get(id); }, async delete(id) { records.delete(id); }, async list() { return [...records.values()]; }, async close() {} };
  const persistence = new BrowserSessionPersistence({ client, store, getDraft: () => draft, restoreDraft: value => restored.push(value), delay: fn => { timers.push(fn); return timers.length; }, clearDelay() {} });
  return { client, records, saves, timers, restored, persistence };
}

test('autosave coalesces changes, is single-flight and skips unchanged checkpoints', async () => {
  const held = gate(), f = persistenceFixture({ exportGate: held });
  for (let i = 0; i < 20; i++) f.persistence.schedule();
  const saving = f.persistence.flush(); const other = f.persistence.flush(); assert.equal(f.client.exports, 1);
  held.resolve(); await Promise.all([saving, other]); assert.equal(f.saves.length, 1);
  f.persistence.schedule(); await f.persistence.flush(); assert.equal(f.saves.length, 1); f.persistence.dispose();
});

test('autosave never exports a running session and resumes after the idle event', async () => {
  const f = persistenceFixture(); f.client.transcript.running = true; f.persistence.schedule(); await f.persistence.flush();
  assert.equal(f.client.exports, 0); assert.equal(f.persistence.status.state, 'waiting'); assert.equal(f.persistence.pending, true);
  f.client.transcript.running = false; f.client.dispatchEvent(new Event('change')); await f.persistence.flush();
  assert.equal(f.saves.length, 1); f.persistence.dispose();
});

test('restoring forks a fresh durable slot and preserves the source checkpoint and editor draft', async () => {
  const f = persistenceFixture(), record = checkpointRecord({ id: 'previous-tab', checkpoint: checkpoint('Recovered'), draft: { path: 'a', content: 'unsaved', baseVersion: 5, baseContent: 'before' } });
  f.records.set(record.id, record); const result = await f.persistence.restore(record.id);
  assert.equal(result.id, 'previous-tab'); assert.notEqual(f.persistence.id, record.id); assert.equal(f.client.imports.length, 1);
  assert.equal(f.restored[0].content, 'unsaved'); assert(f.records.has('previous-tab'));
  await f.persistence.flush(); assert.equal(f.records.size, 2); f.persistence.dispose();
});

test('recovery supplies the validated checkpoint for staging task settings without applying provider configuration', async () => {
  const f = persistenceFixture();
  const record = checkpointRecord({ id: 'goal', checkpoint: JSON.stringify({ messages: [], files: [], run: { mode: 'Goal', successCriteria: 'Tests pass', apiKey: 'excluded' } }) });
  let restored;
  f.persistence.restoreDraft = (draft, value) => { restored = value; };
  f.records.set(record.id, record); await f.persistence.restore(record.id);
  assert.equal(JSON.parse(restored.checkpoint).run.successCriteria, 'Tests pass');
  assert(!restored.checkpoint.includes('excluded')); assert.equal(f.client.imports.length, 1);
  f.persistence.dispose();
});

test('a pending save retains its original slot when a new conversation starts', async () => {
  const held = gate(), f = persistenceFixture({ exportGate: held }), oldId = f.persistence.id;
  f.persistence.schedule(); const saving = f.persistence.flush(); f.persistence.newSession(); const newId = f.persistence.id;
  held.resolve(); await saving;
  assert.equal(f.saves[0].id, oldId); assert.notEqual(newId, oldId); assert.equal(f.persistence.lastSignature, null);
  await f.persistence.flush(); assert.equal(f.saves[1].id, newId); f.persistence.dispose();
});

test('storage quota failures retain the pending save and expose an actionable recovery message', async () => {
  const f = persistenceFixture(); f.persistence.store.save = async () => { throw new DOMException('Full', 'QuotaExceededError'); };
  f.persistence.schedule(); await assert.rejects(f.persistence.flush());
  assert.equal(f.persistence.pending, true); assert.equal(f.persistence.status.state, 'error'); assert.match(f.persistence.status.message, /Download a checkpoint/); f.persistence.dispose();
});

test('recovery rejection does not destroy current state and unavailable browser storage is reported', async () => {
  const f = persistenceFixture(); await assert.rejects(f.persistence.restore('missing'), /no longer exists/); assert.equal(f.client.imports.length, 0);
  f.client.busy = true; await assert.rejects(f.persistence.restore('missing'), /Active run/); f.persistence.dispose();
  await assert.rejects(new IndexedDbAgentStore(null).open(), /IndexedDB is unavailable/);
  assert.match(storageError(new DOMException('Denied', 'SecurityError')), /does not allow durable storage/);
});

test('workspace batch writes serialize with provider changes and never retry uncertain mutations', async () => {
  const held = gate(), calls = [], client = new AgentClient({ invoke: async (method, ...args) => { calls.push([method, ...args]); if (method === 'AgentWorkspaceApply') return held.promise; return {}; }, getDocument: () => 'drawing' });
  const applying = client.applyFiles([{ path: 'a', content: 'new', expectedVersion: 2 }]);
  await assert.rejects(client.writeFile('b', 'new', 0), /Stop the active/); assert.equal(client.busy, true);
  held.reject(new Error('Connection lost after applying')); await assert.rejects(applying, /Connection lost/);
  assert.equal(calls.filter(call => call[0] === 'AgentWorkspaceApply').length, 1); assert.equal(client.busy, false); client.dispose();
});

test('disposed recovery cannot write a delayed export or keep listeners attached', async () => {
  const held = gate(), f = persistenceFixture({ exportGate: held });
  f.persistence.schedule(); const saving = f.persistence.flush(); f.persistence.dispose(); held.resolve(); await saving;
  assert.equal(f.saves.length, 0); const count = f.timers.length; f.client.dispatchEvent(new Event('change')); assert.equal(f.timers.length, count);
});
