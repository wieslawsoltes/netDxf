const protocolVersion = '2025-11-25';
const maxMessageCharacters = 1_048_576;
const requestTimeoutMs = 12 * 60 * 1000;
const idleTimeoutMs = 15 * 60 * 1000;
const disconnectMethod = 'notifications/shadow/disconnect';

function failure(id, code, message) { return { jsonrpc: '2.0', id: id ?? null, error: { code, message } }; }
function abortError() { return new DOMException('MCP request cancelled. Inspect Agent activity before retrying a change.', 'AbortError'); }
function modelContext() {
  const current = globalThis.document?.modelContext;
  if (typeof current?.registerTool === 'function') return current;
  const legacy = globalThis.navigator?.modelContext;
  return typeof legacy?.registerTool === 'function' ? legacy : null;
}
function assertMessagePort(port) {
  if (!port || typeof port.postMessage !== 'function' || typeof port.addEventListener !== 'function' || typeof port.close !== 'function')
    throw new TypeError('Supply a MessagePort created by MessageChannel.');
}

/** Browser-side transport for the official .NET MCP server. Enabling it is an explicit Studio UI action. */
export class BrowserMcpBridge extends EventTarget {
  constructor(client, getDocument, { globalObject = globalThis, getModelContext = modelContext, createChannel = () => new MessageChannel() } = {}) {
    super(); this.client = client; this.getDocument = getDocument; this.globalObject = globalObject;
    this.getModelContext = getModelContext; this.createChannel = createChannel;
    this.enabled = false; this.disposed = false; this.generation = 0; this.documentId = null;
    this.ports = new Map(); this.attachedPorts = new WeakSet(); this.pendingPorts = 0; this.registration = null; this.activationPending = false;
    this.error = null; this.exposure = null;
  }
  get status() {
    return { enabled: this.enabled, activating: this.activationPending, webMcpAvailable: !!this.getModelContext(), webMcpRegistered: this.registration?.names.length || 0,
      messagePorts: this.ports.size, documentId: this.documentId, error: this.error };
  }
  emit() { const event = new Event('change'); event.detail = this.status; this.dispatchEvent(event); }
  assertActive(generation = this.generation) {
    if (this.disposed || !this.enabled || generation !== this.generation) throw new Error('Browser MCP is disabled. Enable it in the MCP panel first.');
    if (this.documentId !== this.getDocument()) throw new Error('The active drawing changed. Enable browser MCP again to bind the new drawing.');
  }
  async enable() {
    if (this.activationPending) throw new Error('Browser MCP activation is already in progress.');
    this.activationPending = true;
    try { return await this.activate(); }
    finally { this.activationPending = false; this.emit(); }
  }
  async activate() {
    if (this.disposed) throw new Error('This browser MCP workspace has closed.');
    if (this.enabled && this.documentId === this.getDocument()) return this.status;
    await this.disable();
    const generation = this.generation;
    this.client.ensureIdle?.();
    const documentId = await this.client.bindDocument();
    if (this.disposed || generation !== this.generation) throw new Error('Browser MCP activation was cancelled.');
    if (documentId !== this.getDocument()) throw new Error('The active drawing changed while enabling browser MCP. Enable it again to bind the new drawing.');
    this.documentId = documentId; this.enabled = true; this.error = null;
    this.exposure = Object.freeze({
      status: () => this.status,
      connect: port => this.connect(port),
      createConnection: () => this.createConnection()
    });
    // No cross-origin window-message listener, wildcard origin grant, endpoint, token,
    // or provider credential is exposed. The user grants this same-page port capability.
    try { Object.defineProperty(this.globalObject, 'shadowStudioBrowserMcp', { configurable: true, value: this.exposure }); }
    catch (error) { this.enabled = false; this.documentId = null; this.exposure = null; throw error; }
    this.emit();
    const context = this.getModelContext();
    if (context) {
      const registration = { context, controller: new AbortController(), names: [], rpc: null };
      this.registration = registration;
      try {
        const rpc = await this.openSession(generation);
        registration.rpc = rpc;
        this.assertActive(generation);
        await rpc.request('initialize', { protocolVersion, capabilities: {}, clientInfo: { name: 'netDxf.Shadow.WebMCP', version: '1.0.0' } });
        await rpc.notify('notifications/initialized');
        let cursor;
        do {
          const page = await rpc.request('tools/list', cursor ? { cursor } : {});
          if (!Array.isArray(page.tools) || page.tools.length > 100 || registration.names.length + page.tools.length > 1024)
            throw new Error('The browser MCP tool catalog exceeded its registration budget.');
          for (const tool of page.tools) {
            this.assertActive(generation);
            const name = tool.name;
            await context.registerTool({
              name, description: tool.description || name, inputSchema: tool.inputSchema,
              annotations: { readOnlyHint: tool.annotations?.readOnlyHint === true,
                consequentialHint: tool.annotations?.readOnlyHint !== true, untrustedContentHint: true },
              execute: (args, options = {}) => {
                this.assertActive(generation);
                return rpc.request('tools/call', { name, arguments: args }, options.signal);
              }
            }, { signal: registration.controller.signal });
            if (generation !== this.generation || !this.enabled) {
              try { context.unregisterTool?.(name); } catch { /* Registration signal handles the current API. */ }
              throw new Error('Browser MCP activation was cancelled.');
            }
            registration.names.push(name);
          }
          cursor = page.nextCursor;
          if (cursor != null && (typeof cursor !== 'string' || cursor.length > 256)) throw new Error('The MCP catalog returned an invalid cursor.');
        } while (cursor);
      } catch (error) {
        await this.clearRegistration(registration);
        if (generation !== this.generation || !this.enabled) throw error;
        this.error = this.client.clean(error?.message || String(error));
      }
    }
    this.assertActive(generation); this.emit(); return this.status;
  }
  async openSession(generation) {
    this.assertActive(generation);
    const value = await this.client.invoke('AgentMcpSessionOpen', this.documentId);
    const rpc = new BrowserRpcSession(this.client, value.sessionId);
    try { this.assertActive(generation); return rpc; }
    catch (error) { await rpc.close(); throw error; }
  }
  async createConnection() {
    this.assertActive();
    const channel = this.createChannel();
    try {
      const connection = await this.connect(channel.port1);
      return { port: channel.port2, close: connection.close, sessionId: connection.sessionId };
    } catch (error) { channel.port1.close(); channel.port2.close(); throw error; }
  }
  async connect(port) {
    assertMessagePort(port); this.assertActive();
    if (this.attachedPorts.has(port)) throw new Error('This MessagePort is already connected or awaiting a browser MCP connection.');
    if (this.ports.size + this.pendingPorts >= 4) throw new Error('At most four browser MCP MessagePorts may be connected.');
    const generation = this.generation; this.pendingPorts++; this.attachedPorts.add(port);
    let rpc;
    try { rpc = await this.openSession(generation); }
    catch (error) { this.attachedPorts.delete(port); throw error; }
    finally { this.pendingPorts--; }
    try { this.assertActive(generation); }
    catch (error) { this.attachedPorts.delete(port); await rpc.close(); throw error; }
    const id = rpc.sessionId;
    let closed = false, pending = 0, idle;
    const close = async () => {
      if (closed) return;
      try { port.postMessage({ jsonrpc: '2.0', method: disconnectMethod }); } catch { /* The peer may already have closed. */ }
      closed = true; clearTimeout(idle); this.attachedPorts.delete(port);
      port.removeEventListener('message', receive); port.removeEventListener('messageerror', onError); port.removeEventListener('close', onClose);
      port.close(); this.ports.delete(id); this.emit(); await rpc.close();
    };
    const renew = () => { clearTimeout(idle); idle = setTimeout(() => { void close(); }, idleTimeoutMs); idle.unref?.(); };
    const send = value => { if (!closed) { try { port.postMessage(value); } catch { void close(); } } };
    const receive = event => {
      if (closed) return;
      let message, json;
      try {
        this.assertActive(generation);
        json = typeof event.data === 'string' ? event.data : JSON.stringify(event.data);
        if (typeof json !== 'string' || json.length > maxMessageCharacters) throw new Error('MCP messages must contain at most one million characters.');
        message = JSON.parse(json);
        if (!message || typeof message !== 'object' || Array.isArray(message)) throw new Error('MCP expects one JSON-RPC object per message.');
      } catch (error) { send(failure(null, -32600, this.client.clean(error.message))); return; }
      if (message.jsonrpc === '2.0' && message.method === disconnectMethod && !Object.hasOwn(message, 'id')) { void close(); return; }
      if (pending >= 32) { if (Object.hasOwn(message, 'id')) send(failure(message.id, -32001, 'Too many pending MCP messages. No action was executed.')); return; }
      pending++; renew();
      void rpc.process(json).then(response => { if (response !== null) send(JSON.parse(response)); }, error => {
        if (Object.hasOwn(message, 'id')) send(failure(message.id, -32000, this.client.clean(error.message)));
      }).catch(() => { void close(); }).finally(() => { pending--; if (!closed) renew(); });
    };
    const onError = () => { void close(); };
    const onClose = () => { void close(); };
    this.ports.set(id, { close });
    port.addEventListener('message', receive); port.addEventListener('messageerror', onError); port.addEventListener('close', onClose);
    port.start?.(); renew(); this.emit();
    return { sessionId: id, close };
  }
  async clearRegistration(registration = this.registration) {
    if (!registration) return;
    const { context, names, rpc } = registration;
    if (this.registration === registration) this.registration = null;
    registration.controller.abort();
    // Current document.modelContext uses the registration AbortSignal; legacy
    // navigator.modelContext implementations may still require unregisterTool.
    for (const name of names) { try { context?.unregisterTool?.(name); } catch { /* Other site registrations are untouched. */ } }
    if (rpc) await rpc.close();
  }
  async disable() {
    this.generation++; this.enabled = false; this.documentId = null;
    if (this.exposure && this.globalObject.shadowStudioBrowserMcp === this.exposure) delete this.globalObject.shadowStudioBrowserMcp;
    this.exposure = null;
    const pending = [...this.ports.values()].map(value => value.close());
    pending.push(this.clearRegistration());
    await Promise.allSettled(pending); this.emit(); return this.status;
  }
  documentChanged() {
    if (this.activationPending || this.enabled && this.documentId !== this.getDocument()) {
      this.error = 'The active drawing changed. Enable browser MCP again to bind the new drawing.';
      return this.disable();
    }
    return Promise.resolve(this.status);
  }
  async dispose() { if (this.disposed) return; this.disposed = true; await this.disable(); }
}

class BrowserRpcSession {
  constructor(client, sessionId) { this.client = client; this.sessionId = sessionId; this.closed = false; this.nextId = 0; }
  async process(json) {
    if (this.closed) throw new Error('This browser MCP connection is closed.');
    this.client.schedule?.(0);
    try { return await this.client.invoke('AgentMcpSessionProcess', this.sessionId, json); }
    finally { this.client.schedule?.(0); }
  }
  notify(method, params) { return this.process(JSON.stringify({ jsonrpc: '2.0', method, ...(params === undefined ? {} : { params }) })); }
  async request(method, params, signal) {
    if (signal?.aborted) throw abortError();
    const id = ++this.nextId;
    let rejectCancellation;
    const cancellation = new Promise((_, reject) => { rejectCancellation = reject; });
    const cancel = () => {
      void this.notify('notifications/cancelled', { requestId: id, reason: 'Browser caller cancelled' }).catch(() => {});
      rejectCancellation(abortError());
    };
    const timeout = setTimeout(cancel, requestTimeoutMs); timeout.unref?.();
    signal?.addEventListener('abort', cancel, { once: true });
    try {
      const response = await Promise.race([this.process(JSON.stringify({ jsonrpc: '2.0', id, method, params })), cancellation]);
      if (signal?.aborted) throw abortError();
      const value = JSON.parse(response);
      if (value.error) throw new Error(this.client.clean(value.error.message || 'The MCP request failed.'));
      return value.result;
    } finally { clearTimeout(timeout); signal?.removeEventListener('abort', cancel); }
  }
  async close() {
    if (this.closed) return; this.closed = true;
    try { await this.client.invoke('AgentMcpSessionClose', this.sessionId); }
    catch { /* Disconnect stays final even when the underlying application already closed. */ }
  }
}

/** Implements the MCP JavaScript SDK Transport shape over an explicitly granted browser MessagePort. */
export class MessagePortMcpTransport {
  constructor(port) { assertMessagePort(port); this.port = port; this.started = false; this.closed = false; }
  async start() {
    if (this.started || this.closed) throw new Error('This MCP transport cannot be started again.');
    this.started = true;
    this.receive = event => {
      try {
        const message = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        if (message?.jsonrpc === '2.0' && message.method === disconnectMethod && !Object.hasOwn(message, 'id')) this.closeCore(false);
        else this.onmessage?.(message);
      }
      catch (error) { this.onerror?.(error); }
    };
    this.error = () => this.onerror?.(new Error('The browser MCP MessagePort could not decode a message.'));
    this.peerClosed = () => this.closeCore(false);
    this.port.addEventListener('message', this.receive); this.port.addEventListener('messageerror', this.error);
    this.port.addEventListener('close', this.peerClosed); this.port.start?.();
  }
  async send(message) {
    if (!this.started || this.closed) throw new Error('The browser MCP transport is not open.');
    const json = JSON.stringify(message);
    if (json.length > maxMessageCharacters) throw new Error('MCP message exceeds one million characters.');
    this.port.postMessage(message);
  }
  async close() { this.closeCore(true); }
  closeCore(notifyPeer) {
    if (this.closed) return; this.closed = true;
    if (notifyPeer) { try { this.port.postMessage({ jsonrpc: '2.0', method: disconnectMethod }); } catch { /* Peer may already have closed. */ } }
    if (this.receive) this.port.removeEventListener('message', this.receive);
    if (this.error) this.port.removeEventListener('messageerror', this.error);
    if (this.peerClosed) this.port.removeEventListener('close', this.peerClosed);
    this.port.close(); this.onclose?.();
  }
}
