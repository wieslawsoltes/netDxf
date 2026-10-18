import { parseSession, redact } from './contracts.mjs';

const databaseName = 'netdxf-shadow-agents-v1';
const metadataStoreName = 'sessionMetadata';
const maximumRecords = 10, maximumBytes = 64 * 1024 * 1024;
const bytes = value => new TextEncoder().encode(value).byteLength;
const newId = () => globalThis.crypto?.randomUUID?.() || `session-${Date.now()}-${Math.random().toString(36).slice(2)}`;

export function storageError(error) {
  if (error?.name === 'QuotaExceededError') return 'Browser storage is full. Download a checkpoint, then remove older saved sessions or free space before saving again.';
  if (['SecurityError', 'InvalidStateError', 'NotSupportedError'].includes(error?.name)) return 'This browser session does not allow durable storage. Download a checkpoint to keep your work.';
  return String(error?.message || error || 'The browser could not save this session.');
}

/** An allowlisted record prevents provider settings or browser objects reaching durable storage. */
export function checkpointRecord({ id, checkpoint, draft = null, label = '', savedAt = new Date().toISOString() }, secrets = []) {
  const clean = parseSession(checkpoint, secrets), data = JSON.parse(clean);
  const safeDraft = draft && typeof draft.content === 'string' ? {
    path: String(draft.path || '').slice(0, 512), content: redact(draft.content, secrets),
    baseVersion: Number.isSafeInteger(draft.baseVersion) ? draft.baseVersion : null,
    basePath: typeof draft.basePath === 'string' ? draft.basePath.slice(0, 512) : null,
    baseContent: typeof draft.baseContent === 'string' ? redact(draft.baseContent, secrets) : null
  } : null;
  const record = { id: String(id || newId()), checkpoint: clean, draft: safeDraft,
    label: redact(String(label || data.activeTask || 'Agent workspace').replace(/\s+/g, ' ').slice(0, 100), secrets),
    savedAt, files: Array.isArray(data.files) ? data.files.length : 0, messages: Array.isArray(data.messages) ? data.messages.length : 0 };
  record.bytes = bytes(clean) + (safeDraft ? bytes(JSON.stringify(safeDraft)) : 0);
  if (record.bytes > 20 * 1024 * 1024) throw new Error('The checkpoint and editor draft exceed the 20 MiB browser save limit. Export the session and reduce its workspace size.');
  return record;
}

function meaningful(record) {
  const data = JSON.parse(record.checkpoint);
  return !!(record.draft || record.files || record.messages || data.summary || data.activeTask || data.pins?.length || data.plan?.length);
}

function metadataRecord(record) {
  return { id: record.id, label: record.label, savedAt: record.savedAt, files: record.files,
    messages: record.messages, bytes: record.bytes, hasDraft: !!record.draft };
}

export class IndexedDbAgentStore {
  constructor(indexedDb = globalThis.indexedDB, name = databaseName) { this.indexedDb = indexedDb; this.name = name; this.pending = null; }
  open() {
    if (this.pending) return this.pending;
    this.pending = new Promise((resolve, reject) => {
      if (!this.indexedDb) { reject(new Error('IndexedDB is unavailable. Download a checkpoint to keep your work.')); return; }
      let request;
      try { request = this.indexedDb.open(this.name, 2); } catch (error) { reject(error); return; }
      let failed = false;
      request.onupgradeneeded = event => {
        const database = request.result;
        if (!database.objectStoreNames.contains('sessions')) database.createObjectStore('sessions', { keyPath: 'id' });
        const metadata = database.objectStoreNames.contains(metadataStoreName) ? request.transaction.objectStore(metadataStoreName) : database.createObjectStore(metadataStoreName, { keyPath: 'id' });
        if (event.oldVersion < 2) {
          // Migration reads one old record at a time and retains its checkpoint
          // unchanged. Future listings and pruning never clone checkpoint data.
          const cursor = request.transaction.objectStore('sessions').openCursor();
          cursor.onsuccess = () => { const entry = cursor.result; if (entry) { metadata.put(metadataRecord(entry.value)); entry.continue(); } };
        }
      };
      request.onerror = () => { failed = true; reject(request.error); };
      request.onblocked = () => { failed = true; reject(new Error('Browser storage is locked by another Studio tab. Close that tab and retry.')); };
      request.onsuccess = () => { if (failed) { request.result.close(); return; } request.result.onversionchange = () => { request.result.close(); this.pending = null; }; resolve(request.result); };
    }).catch(error => { this.pending = null; throw error; });
    return this.pending;
  }
  async transaction(mode, action, names = ['sessions']) {
    const database = await this.open();
    return new Promise((resolve, reject) => {
      const transaction = database.transaction(names, mode), store = transaction.objectStore(names[0]); let result;
      transaction.oncomplete = () => resolve(result);
      transaction.onerror = transaction.onabort = () => reject(transaction.error || new Error('Browser storage transaction was cancelled.'));
      try { action(store, value => { result = value; }, transaction); } catch (error) { transaction.abort(); reject(error); }
    });
  }
  list() {
    return this.transaction('readonly', (store, done) => {
      const request = store.getAll(); request.onsuccess = () => done(request.result.sort((a, b) => b.savedAt.localeCompare(a.savedAt)));
    }, [metadataStoreName]);
  }
  get(id) { return this.transaction('readonly', (store, done) => { const request = store.get(id); request.onsuccess = () => done(request.result || null); }); }
  save(record) {
    return this.transaction('readwrite', (store, done, transaction) => {
      const metadata = transaction.objectStore(metadataStoreName);
      store.put(record);
      metadata.put(metadataRecord(record));
      const request = metadata.getAll();
      request.onsuccess = () => {
        const records = request.result.sort((a, b) => b.savedAt.localeCompare(a.savedAt));
        let total = record.bytes, kept = 1;
        for (const entry of records) {
          if (entry.id === record.id) continue;
          if (kept >= maximumRecords || total + entry.bytes > maximumBytes) { store.delete(entry.id); metadata.delete(entry.id); }
          else { kept++; total += entry.bytes; }
        }
        done(record);
      };
    }, ['sessions', metadataStoreName]);
  }
  delete(id) { return this.transaction('readwrite', (store, done, transaction) => { store.delete(id); transaction.objectStore(metadataStoreName).delete(id); }, ['sessions', metadataStoreName]); }
  async close() { const pending = this.pending; this.pending = null; if (pending) (await pending.catch(() => null))?.close(); }
}

/** Single-flight, bounded and idle-only autosaves. Restore always forks a new slot, so tabs cannot overwrite one another. */
export class BrowserSessionPersistence extends EventTarget {
  constructor({ client, store = new IndexedDbAgentStore(), getDraft = () => null, restoreDraft = () => {}, label = () => '',
    delay = (callback, ms) => setTimeout(callback, ms), clearDelay = timer => clearTimeout(timer) }) {
    super(); Object.assign(this, { client, store, getDraft, restoreDraft, label, delay, clearDelay });
    this.id = newId(); this.timer = null; this.pending = false; this.saving = null; this.disposed = false; this.suspended = false;
    this.lastSignature = null; this.status = { state: 'ready', message: 'Autosave on this device · credentials stay in memory.' };
    this.listener = () => this.schedule();
    for (const event of ['change', 'workspacechanged', 'busy']) client.addEventListener(event, this.listener);
  }
  emit(state, message, extra = {}) { this.status = { state, message, ...extra }; const event = new Event('change'); event.detail = this.status; this.dispatchEvent(event); }
  schedule() {
    if (this.disposed) return;
    this.pending = true;
    if (this.timer !== null) this.clearDelay(this.timer);
    this.timer = this.delay(() => { this.timer = null; void this.flush().catch(() => {}); }, 1000);
  }
  async flush() {
    if (this.disposed || this.suspended || !this.pending) return;
    if (this.saving) return this.saving;
    if (!this.client.connected || this.client.busy || this.client.transcript.running) {
      this.emit('waiting', 'Autosave waits until the agent is idle. Your last completed save remains available.'); return;
    }
    this.pending = false;
    this.saving = (async () => {
      const id = this.id, draft = this.getDraft(), label = this.label();
      const checkpoint = await this.client.exportSession();
      if (this.disposed) return;
      const record = checkpointRecord({ id, checkpoint, draft, label }, this.client.secrets);
      if (!meaningful(record)) { this.emit('ready', 'Autosave on this device · credentials stay in memory.'); return; }
      const signature = record.checkpoint + JSON.stringify(record.draft);
      if (signature === this.lastSignature) return;
      this.emit('saving', 'Saving to this browser…');
      await this.store.save(record); if (id === this.id) this.lastSignature = signature;
      this.emit('saved', `Saved on this device at ${new Date(record.savedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`, { savedAt: record.savedAt, id: record.id });
    })().catch(error => { this.pending = true; this.emit('error', storageError(error)); throw error; })
      .finally(() => { this.saving = null; if (this.pending && !this.disposed && this.status.state !== 'error') this.schedule(); });
    return this.saving;
  }
  async restore(id) {
    this.client.ensureIdle();
    if (this.suspended) throw new Error('A browser recovery operation is already in progress.');
    this.suspended = true;
    try {
      if (this.saving) await this.saving;
      const record = await this.store.get(id);
      if (!record) throw new Error('That saved workspace no longer exists. Refresh the saved session list.');
      const validated = checkpointRecord(record, this.client.secrets);
      await this.client.importSession(validated.checkpoint);
      this.id = newId(); this.lastSignature = null; this.restoreDraft(validated.draft);
      this.emit('restored', 'Restored a saved workspace. Current provider credentials and permissions are retained; credentials were not saved.');
      this.schedule(); return validated;
    } finally { this.suspended = false; if (this.pending && !this.disposed) this.schedule(); }
  }
  newSession() { this.id = newId(); this.lastSignature = null; this.schedule(); }
  async remove(id) {
    if (this.suspended) throw new Error('A browser recovery operation is already in progress.');
    this.suspended = true;
    try {
      if (this.saving) await this.saving;
      await this.store.delete(id);
      if (id === this.id) { this.id = newId(); this.lastSignature = null; }
    } finally { this.suspended = false; if (this.pending && !this.disposed) this.schedule(); }
  }
  dispose() {
    this.disposed = true; if (this.timer !== null) this.clearDelay(this.timer);
    for (const event of ['change', 'workspacechanged', 'busy']) this.client.removeEventListener(event, this.listener);
    void this.store.close();
  }
}
