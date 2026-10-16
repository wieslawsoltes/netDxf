import { identityHeaders, viewKey } from '../studio-state.mjs';

export class ApiError extends Error {
  constructor(message, status) { super(message); this.name = 'ApiError'; this.status = status; }
}
const scope = s => s ? JSON.stringify([s.serverId, s.documentId, s.workspaceId, s.viewVersion]) : '';
/** Serialized writes, generation-fenced reads, and no implicit retries of non-idempotent edits. */
export class CadClient extends EventTarget {
  constructor(fetcher = (...args) => fetch(...args)) {
    super(); this.fetcher = fetcher; this.id = null; this.data = null; this.epoch = 0; this.busy = 0; this.tail = Promise.resolve();
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
  async documents() { return (await this.request('/api/cad/documents')).json(); }
  async create(name, demo = false) { return (await this.request('/api/cad/documents', { body: { name, demo } })).json(); }
  async activate(id) {
    if (this.busy) throw new Error('Finish the current drawing operation before switching documents.');
    const generation = ++this.epoch; this.id = id; this.data = null; this.emit('reset');
    const data = await (await this.request(this.url('state', id))).json();
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
    const id = this.id, generation = ++this.epoch;
    const data = await (await this.request(this.url('state', id))).json();
    if (generation === this.epoch && id === this.id && !this.busy && viewKey(data.state) !== viewKey(this.state)) this.adopt(data);
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
  async read(path, { body, signal, blob = false } = {}) {
    const key = this.key, generation = this.epoch, state = this.state, id = this.id;
    const response = await this.request(this.url(path, id), { body, signal, headers: identityHeaders(state) });
    const result = blob ? await response.blob() : await response.json();
    return generation === this.epoch && key === this.key && !this.busy ? result : null;
  }
}
