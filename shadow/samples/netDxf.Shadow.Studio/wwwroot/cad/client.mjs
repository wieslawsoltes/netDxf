import { identityHeaders, viewKey } from '../studio-state.mjs';

export class ApiError extends Error {
  constructor(message, status) { super(message); this.name = 'ApiError'; this.status = status; }
}
export class DrawingOpenError extends Error {
  constructor(error, retained = false) {
    super(`${error.message || String(error)}${retained ? ' The new drawing tab was kept because it could not be closed safely.' : ''}`, { cause: error });
    this.name = 'DrawingOpenError'; this.retained = retained;
  }
}
const scope = s => s ? JSON.stringify([s.serverId, s.documentId, s.workspaceId, s.viewVersion]) : '';
/** Serialized writes, generation-fenced reads, and no implicit retries of non-idempotent edits. */
export class CadClient extends EventTarget {
  constructor(fetcher = (...args) => fetch(...args)) {
    super(); this.fetcher = fetcher; this.id = null; this.data = null; this.epoch = 0; this.refreshGeneration = 0; this.busy = 0; this.tail = Promise.resolve();
    this.openGeneration = 0; this.interactionGeneration = 0; this.pendingDocuments = new Set();
  }
  get state() { return this.data?.state; }
  get key() { return `${this.id}:${viewKey(this.state)}`; }
  emit(name) { this.dispatchEvent(new Event(name)); }
  async request(path, { body, headers = {}, signal, method } = {}) {
    const options = { method: method || (body === undefined ? 'GET' : 'POST'), headers: { ...headers }, signal, cache: 'no-store' };
    if (body !== undefined) {
      options.headers['X-Shadow-Studio'] = '1';
      if (typeof Blob !== 'undefined' && body instanceof Blob) options.body = body;
      else { options.headers['Content-Type'] = 'application/json'; options.body = JSON.stringify(body); }
    }
    const response = await this.fetcher(path, options);
    if (!response.ok) {
      let message = `Studio request failed (${response.status}).`;
      try { message = (await response.json()).error || message; } catch { /* Keep the HTTP error, not a parser error. */ }
      throw new ApiError(message, response.status);
    }
    return response;
  }
  url(path, id = this.id) { if (!id) throw new Error('No active drawing.'); return `/api/cad/${encodeURIComponent(id)}/${path}`; }
  async documents() {
    const documents = await (await this.request('/api/cad/documents')).json();
    return documents.filter(document => !this.pendingDocuments.has(document.id));
  }
  async create(name, demo = false) { return (await this.request('/api/cad/documents', { body: { name, demo } })).json(); }
  /** Keep a newly started command or gesture in front without invalidating drawing reads. */
  beginInteraction() { this.interactionGeneration++; }
  /** Import in a detached document, keeping the visible drawing intact until success. */
  async open(file, name = file.name) {
    const id = this.id, epoch = this.epoch, interaction = this.interactionGeneration, generation = ++this.openGeneration;
    let created, loader, emptyReceipt;
    try {
      created = await this.create(name, false);
      this.pendingDocuments.add(created.id);
      loader = new CadClient(this.fetcher);
      await loader.activate(created.id);
      const state = loader.state;
      if (state.entities !== 0 || state.revision !== 0 || state.changeVersion !== 0 || state.workspaceId !== 'live')
        throw new Error('The new drawing changed before import. Repeat Open.');
      emptyReceipt = state;
      const result = await loader.mutate('load', file, { binary: true, query: { name } });
      if (!result?.state) throw new Error('The drawing loader did not return a drawing state.');
    } catch (error) {
      let retained = !!created;
      if (created && emptyReceipt) {
        try {
          // The original empty receipt is deliberate. A lost import response or
          // another editor's changes must never be deleted by failure cleanup.
          await this.request(this.url('close', created.id), {
            headers: identityHeaders(emptyReceipt), body: { revision: emptyReceipt.revision, discard: false }
          });
          retained = false;
        } catch { /* A changed or unavailable document remains accessible. */ }
      }
      throw new DrawingOpenError(error, retained);
    } finally {
      if (created) this.pendingDocuments.delete(created.id);
    }
    // A later tab selection, edit, command, gesture or Open owns focus. The completed import still
    // appears in the tabs when it no longer owns the foreground operation.
    const active = id === this.id && epoch === this.epoch && generation === this.openGeneration &&
      interaction === this.interactionGeneration && !this.busy;
    if (active) await this.activate(created.id, loader.data);
    return { ...created, active };
  }
  async activate(id, prepared = null) {
    if (this.busy) throw new Error('Finish the current drawing operation before switching documents.');
    const generation = ++this.epoch; this.id = id; this.data = null; this.emit('reset');
    const data = prepared ?? await (await this.request(this.url('state', id))).json();
    if (generation === this.epoch && this.id === id) this.adopt(data);
  }
  adopt(data) {
    const s = data?.state;
    if (!s || typeof s.documentId !== 'string' || typeof s.serverId !== 'string' || typeof s.workspaceId !== 'string' ||
        ![s.revision, s.viewVersion, s.changeVersion].every(n => Number.isSafeInteger(n) && n >= 0)) throw new Error('Invalid CAD state receipt.');
    const old = this.state;
    if (old && old.serverId === s.serverId && old.documentId === s.documentId && s.changeVersion < old.changeVersion) return;
    this.data = data; this.emit('state');
  }
  async refresh() {
    if (!this.id || !this.state || this.busy) return;
    // Order polls independently of document changes. An unchanged background poll
    // must not invalidate a slow render: no replacement frame would be requested.
    const id = this.id, epoch = this.epoch, generation = ++this.refreshGeneration;
    const current = () => generation === this.refreshGeneration && epoch === this.epoch && id === this.id && !this.busy;
    try {
      const query = new URLSearchParams({ knownServer: this.state.serverId, knownChange: this.state.changeVersion });
      const data = await (await this.request(this.url(`state?${query}`, id))).json();
      if (data !== null && current() && viewKey(data.state) !== viewKey(this.state)) this.adopt(data);
    } catch (error) {
      if (current()) throw error;
    }
  }
  mutate(path, payload = {}, { binary = false, query = null, expectedKey = null } = {}) {
    if (!this.state) return Promise.reject(new Error('Load a drawing first.'));
    const id = this.id, expectedScope = scope(this.state); this.busy++; this.epoch++; this.emit('busy');
    const execute = async () => {
      if (id !== this.id || expectedScope !== scope(this.state)) throw new ApiError('The drawing or workspace changed before this operation. Repeat the command in the current view.', 409);
      if (expectedKey !== null && expectedKey !== this.key) throw new ApiError('The authoring receipt changed. Reopen the dialog or repeat the command.', 409);
      const state = this.state, headers = identityHeaders(state);
      const route = query ? `${path}?${new URLSearchParams({ ...query, revision: state.revision })}` : path;
      const response = await this.request(this.url(route, id), { body: binary ? payload : { ...payload, revision: state.revision }, headers });
      if (binary || path === 'save') {
        const result = path === 'save' ? await response.blob() : await response.json();
        if (result?.state) this.adopt(result);
        else if (path === 'save') this.adopt(await (await this.request(this.url('state', id))).json());
        return result;
      }
      const result = await response.json(); if (result?.state) this.adopt(result); return result;
    };
    const result = this.tail.then(execute);
    this.tail = result.catch(() => {});
    return result.finally(() => { this.busy--; this.epoch++; this.emit('busy'); });
  }
  async read(path, { body, signal, blob = false, buffer = false } = {}) {
    const key = this.key, generation = this.epoch, state = this.state, id = this.id;
    const current = () => generation === this.epoch && key === this.key && !this.busy;
    try {
      const response = await this.request(this.url(path, id), { body, signal, headers: identityHeaders(state) });
      const result = buffer ? await response.arrayBuffer() : blob ? await response.blob() : await response.json();
      return current() ? result : null;
    } catch (error) {
      // Errors belong to the same receipt as successful reads. An obsolete render or
      // graph must not cancel a new command after a document/workspace switch.
      // Current failures still propagate, and writes are never retried or suppressed.
      if (!current()) return null;
      throw error;
    }
  }
}
