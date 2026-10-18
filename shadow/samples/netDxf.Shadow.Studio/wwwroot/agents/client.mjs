import { AgentTranscript, configuration, endpointUrl, parseSession, redact } from './contracts.mjs';

const routes = Object.freeze({
  AgentCapabilities: 'capabilities', AgentConfigure: 'configure', AgentDisconnect: 'disconnect', AgentSetDocument: 'set-document',
  AgentStart: 'start', AgentRetry: 'retry', AgentPoll: 'poll', AgentUiReply: 'ui-reply', AgentCancel: 'cancel', AgentApprove: 'approve',
  AgentCompact: 'compact', AgentReset: 'reset', AgentExport: 'export', AgentImport: 'import',
  AgentMcpTools: 'mcp-tools', AgentMcpCall: 'mcp-call', AgentWorkspaceFiles: 'workspace-files',
  AgentWorkspaceRead: 'workspace-read', AgentWorkspaceWrite: 'workspace-write', AgentPin: 'pin', AgentUnpin: 'unpin',
  AgentWorkspaceExport: 'workspace-export', AgentWorkspaceApply: 'workspace-apply',
  AgentMcpSessionOpen: 'mcp-session-open', AgentMcpSessionProcess: 'mcp-session-process', AgentMcpSessionClose: 'mcp-session-close',
  AgentCompanionConnect: 'companion-connect', AgentCompanionDisconnect: 'companion-disconnect'
});

/** Mutations are dispatched once. Only status reads use reconnect/backoff. */
export class AgentClient extends EventTarget {
  constructor({ invoke, fetcher = (...args) => fetch(...args), getDocument = () => null, delay = (fn, ms) => setTimeout(fn, ms), clearDelay = id => clearTimeout(id) } = {}) {
    super(); this.customInvoke = invoke; this.fetcher = fetcher; this.getDocument = getDocument;
    this.delay = delay; this.clearDelay = clearDelay; this.transcript = new AgentTranscript(); this.secrets = [];
    this.config = null; this.configured = false; this.connected = false; this.disposed = false;
    this.generation = 0; this.polling = false; this.timer = null; this.failures = 0; this.busy = false; this.operation = null; this.uiReplies = new Map();
  }
  emit(name, detail) { const event = new Event(name); event.detail = detail; this.dispatchEvent(event); }
  clean(value) { return redact(value, this.secrets); }
  async invoke(method, ...args) {
    if (!Object.hasOwn(routes, method)) throw new Error(`Unknown agent method: ${method}`);
    try {
      if (this.customInvoke) return await this.customInvoke(method, ...args);
      if (globalThis.shadowAgentRuntime?.invoke) return await globalThis.shadowAgentRuntime.invoke(method, ...args);
      if (globalThis.DotNet?.invokeMethodAsync) return await globalThis.DotNet.invokeMethodAsync('netDxf.Shadow.Studio.Browser', method, ...args);
      const response = await this.fetcher(`/api/agent/${routes[method]}`, {
        method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-Shadow-Studio': '1' },
        body: JSON.stringify({ arguments: args })
      });
      let value;
      try { value = await response.json(); } catch { throw new Error(`Agent service returned HTTP ${response.status} without a JSON response.`); }
      if (!response.ok) throw new Error(value.error || value.message || `Agent service returned HTTP ${response.status}.`);
      return value;
    } catch (error) {
      const result = new Error(this.clean(error?.message || String(error)));
      result.name = error?.name || 'Error'; throw result;
    }
  }
  async connect() {
    const capabilities = await this.invoke('AgentCapabilities'); this.connected = true;
    this.emit('connected', capabilities); await this.poll(); return capabilities;
  }
  async configure(input) {
    this.ensureIdle();
    const config = configuration({ ...input, documentId: this.getDocument() });
    // Keep previous keys in the redaction set until the view closes, including during key rotation.
    if (config.apiKey && !this.secrets.includes(config.apiKey)) this.secrets.push(config.apiKey);
    const operation = this.beginOperation('lifecycle');
    try {
      const result = await this.invoke('AgentConfigure', config); this.assertOperation(operation);
      this.config = config; this.configured = result?.configured !== false; this.connected = true; this.emit('configured', this.clean(config));
      this.generation++; this.transcript.sequence = 0;
      await this.poll(); return result;
    } finally { this.finishOperation(operation); }
  }
  async disconnect() {
    if (this.disposed) throw new Error('This agent workspace has closed.');
    if (this.operation && this.operation.kind !== 'work') throw new Error('Wait for the current connection or session change before disconnecting.');
    // Disconnect may interrupt an active turn. Its operation replaces pending work so
    // a delayed Start/Retry reply cannot put the disconnected client back into running state.
    const operation = { kind: 'disconnect' }; this.operation = operation; this.busy = true; this.generation++; this.emit('busy');
    try {
      const result = await this.invoke('AgentDisconnect'); this.assertOperation(operation);
      this.configured = false; this.config = null; this.transcript.running = false;
      this.transcript.sequence = 0; this.transcript.status = 'idle'; this.emit('configured', null); this.emit('disconnected');
      await this.poll(); return result;
    } finally { this.finishOperation(operation); this.schedule(0); }
  }
  ensureIdle() {
    if (this.disposed) throw new Error('This agent workspace has closed.');
    if (this.busy || this.transcript.running) throw new Error('Stop the active agent turn before changing its session, document, or configuration.');
  }
  beginOperation(kind = 'work') {
    this.ensureIdle(); const operation = { kind }; this.operation = operation; this.busy = true; this.emit('busy'); return operation;
  }
  assertOperation(operation) {
    if (operation.cancelled) throw new Error('The agent operation was cancelled.');
    if (this.disposed || operation !== this.operation) throw new Error('The operation was interrupted by a disconnect.');
  }
  finishOperation(operation) {
    if (this.operation !== operation) return;
    this.operation = null; this.busy = false; this.emit('busy');
  }
  async bindDocument(documentId = this.getDocument()) {
    if (!documentId) throw new Error('Open a drawing before starting an agent turn.');
    if (documentId !== this.getDocument()) throw new Error('The active drawing changed. Start again to bind the new drawing.');
    await this.invoke('AgentSetDocument', documentId);
    if (documentId !== this.getDocument()) throw new Error('The active drawing changed. Start again to bind the new drawing.');
    return documentId;
  }
  async start(prompt) {
    this.ensureIdle();
    const text = String(prompt ?? '').trim();
    if (!text) throw new Error('Enter a task for the agent.');
    if (text.length > 200000) throw new Error('Prompts must be smaller than 200,000 characters. Pin or attach a workspace file for larger inputs.');
    if (!this.configured) throw new Error('Connect a provider in Agent settings first.');
    const operation = this.beginOperation(); let dispatched = false;
    try {
      await this.bindDocument(); this.assertOperation(operation);
      dispatched = true;
      const result = await this.invoke('AgentStart', text); this.assertOperation(operation);
      this.transcript.running = true; this.transcript.status = 'running'; this.lastPrompt = text;
      this.emit('change', this.transcript); this.schedule(0); return result;
    } catch (error) {
      // A transport error after dispatch cannot prove that a turn failed to start.
      if (dispatched && this.operation === operation && !this.disposed) { this.transcript.running = true; this.transcript.status = 'reconnecting'; this.schedule(0); }
      throw error;
    } finally { this.finishOperation(operation); }
  }
  async retry() {
    this.ensureIdle(); if (!this.configured) throw new Error('Connect a model provider first.');
    const operation = this.beginOperation();
    try { const result = await this.invoke('AgentRetry'); this.assertOperation(operation); this.transcript.running = true; this.transcript.status = 'running'; this.schedule(0); return result; }
    catch (error) { if (this.operation === operation && !this.disposed) { this.transcript.running = true; this.transcript.status = 'reconnecting'; this.schedule(0); } throw error; }
    finally { this.finishOperation(operation); }
  }
  schedule(ms = 200) {
    if (this.disposed) return;
    if (this.timer !== null) this.clearDelay(this.timer);
    this.timer = this.delay(() => { this.timer = null; void this.poll().catch(() => {}); }, ms);
  }
  async poll() {
    if (this.disposed || this.polling) return;
    const generation = this.generation; this.polling = true;
    try {
      const result = await this.invoke('AgentPoll', this.transcript.sequence);
      if (generation !== this.generation || this.disposed) return;
      this.transcript.apply(this.clean(result)); this.failures = 0; this.connected = true;
      await this.processUiRequests(result.uiRequests || [], generation);
      if (generation !== this.generation || this.disposed) return;
      this.emit('change', this.transcript);
      if (Number.isSafeInteger(result.latestSequence) && result.latestSequence > this.transcript.sequence) this.schedule(0);
      else if (this.transcript.running || this.busy) this.schedule(160);
    } catch (error) {
      if (generation !== this.generation || this.disposed) return;
      this.failures++; this.connected = false;
      this.emit('connectionerror', { error, attempt: this.failures, retryMs: Math.min(15000, 500 * 2 ** Math.min(this.failures, 5)) });
      // Reading the event cursor is safe to retry; a lost Start response is never replayed.
      if (this.transcript.running || this.failures < 4) this.schedule(Math.min(15000, 500 * 2 ** Math.min(this.failures, 5)));
      throw error;
    } finally { this.polling = false; if (!this.disposed && generation !== this.generation) this.schedule(0); }
  }
  async processUiRequests(requests, generation) {
    for (const request of requests) {
      if (generation !== this.generation || this.disposed) return;
      if (!request || typeof request.id !== 'string') continue;
      let reply = this.uiReplies.get(request.id);
      if (!reply) {
        // Store the in-flight promise before execution. A repeated poll can resend a reply, never replay the edit.
        reply = Promise.resolve().then(async () => {
          try {
            const ui = globalThis.shadowStudioAgentUi;
            if (!ui) throw new Error('The Studio UI automation boundary is not available.');
            const result = request.action === 'snapshot' ? await ui.snapshot() : await ui.execute(request.action, request.arguments || {}, request.expectedDocumentId);
            return { result, error: null };
          } catch (error) { return { result: null, error: this.clean(error.message || String(error)) }; }
        });
        this.uiReplies.set(request.id, reply);
      }
      const value = await reply;
      await this.invoke('AgentUiReply', request.id, value.result, value.error);
    }
  }
  async cancel() {
    if (this.disposed) throw new Error('This agent workspace has closed.');
    if (this.operation && this.operation.kind !== 'work') throw new Error('Wait for the current connection, session change, or stop request to finish.');
    if (this.operation) this.operation.cancelled = true;
    const operation = { kind: 'cancel' }; this.operation = operation; this.busy = true; this.emit('busy');
    try { const result = await this.invoke('AgentCancel'); await this.poll(); return result; }
    finally { this.finishOperation(operation); this.schedule(0); }
  }
  async approve(requestId, approved) { const result = await this.invoke('AgentApprove', String(requestId), !!approved); this.schedule(0); return result; }
  async compact() {
    const operation = this.beginOperation(); this.transcript.status = 'compacting'; this.schedule(0);
    try { return await this.invoke('AgentCompact'); }
    finally { this.finishOperation(operation); this.schedule(0); }
  }
  async reset() {
    const operation = this.beginOperation('lifecycle'); this.generation++;
    try { const result = await this.invoke('AgentReset'); this.assertOperation(operation); this.transcript.reset(); this.emit('change', this.transcript); this.schedule(0); return result; }
    finally { this.finishOperation(operation); }
  }
  async exportSession() {
    const value = await this.invoke('AgentExport');
    return parseSession(typeof value === 'string' ? value : JSON.stringify(value), this.secrets);
  }
  async importSession(text) {
    this.ensureIdle(); const clean = parseSession(text, this.secrets), operation = this.beginOperation('lifecycle'); this.generation++;
    try { const result = await this.invoke('AgentImport', clean); this.assertOperation(operation); this.transcript.reset(); this.emit('change', this.transcript); await this.poll(); return result; }
    finally { this.finishOperation(operation); }
  }
  async tools() { const result = await this.invoke('AgentMcpTools'); return result?.tools || result || []; }
  async companionConnect(endpoint, token) {
    this.ensureIdle(); const url = endpointUrl(endpoint || 'http://127.0.0.1:5091', { companion: true });
    const credential = String(token || '').trim(); if (!credential) throw new Error('Enter the companion access token.');
    if (!this.secrets.includes(credential)) this.secrets.push(credential);
    const operation = this.beginOperation('lifecycle');
    try {
      await this.bindDocument(); this.assertOperation(operation);
      const result = await this.invoke('AgentCompanionConnect', url, credential); this.assertOperation(operation);
      await this.poll(); return result;
    }
    finally { this.finishOperation(operation); }
  }
  async companionDisconnect() {
    const operation = this.beginOperation('lifecycle');
    try {
      const result = await this.invoke('AgentCompanionDisconnect'); this.assertOperation(operation);
      await this.poll(); return result;
    }
    finally { this.finishOperation(operation); }
  }
  async callTool(name, args, { documentId = null } = {}) {
    const operation = this.beginOperation();
    try {
      if (documentId && documentId !== this.getDocument()) throw new Error('The active drawing changed. Reconnect MCP to bind the new drawing.');
      await this.bindDocument(documentId || this.getDocument()); this.assertOperation(operation);
      this.schedule(0); return this.clean(await this.invoke('AgentMcpCall', name, args));
    }
    finally { this.finishOperation(operation); this.schedule(0); }
  }
  async files() { const result = await this.invoke('AgentWorkspaceFiles'); return result?.files || result || []; }
  async readFile(path) { return this.invoke('AgentWorkspaceRead', path); }
  async writeFile(path, content, expectedVersion = null) {
    const operation = this.beginOperation('lifecycle');
    try {
      const result = await this.invoke('AgentWorkspaceWrite', path, content, expectedVersion); this.assertOperation(operation);
      this.emit('workspacechanged'); return result;
    } finally { this.finishOperation(operation); }
  }
  async exportFiles() { const result = await this.invoke('AgentWorkspaceExport'); return this.clean(result?.files || result || []); }
  async applyFiles(changes) {
    const operation = this.beginOperation('lifecycle');
    try {
      const result = await this.invoke('AgentWorkspaceApply', changes); this.assertOperation(operation);
      this.emit('workspacechanged'); return result;
    } finally { this.finishOperation(operation); }
  }
  async pin(text) { this.ensureIdle(); const result = await this.invoke('AgentPin', String(text)); this.schedule(0); return result; }
  async unpin(id) { this.ensureIdle(); const result = await this.invoke('AgentUnpin', String(id)); this.schedule(0); return result; }
  dispose() { this.disposed = true; this.generation++; this.operation = null; this.busy = false; if (this.timer !== null) this.clearDelay(this.timer); this.secrets.length = 0; this.config = null; this.uiReplies.clear(); }
}

/** Sign-in is performed by the localhost companion; browser storage never receives OAuth tokens. */
export class ChatGptCompanion {
  constructor(endpoint, token, fetcher = (...args) => fetch(...args)) {
    this.endpoint = endpointUrl(endpoint || 'http://127.0.0.1:5091', { companion: true });
    this.token = String(token || '').trim(); this.fetcher = fetcher;
  }
  async request(path, method = 'GET') {
    if (!this.token) throw new Error('Enter the companion access token printed by AgentHost.');
    let response;
    try {
      response = await this.fetcher(`${this.endpoint}/auth/${path}`, {
        method, cache: 'no-store', headers: { Authorization: `Bearer ${this.token}`, 'X-Shadow-Studio': '1' },
        signal: AbortSignal.timeout(15000)
      });
    } catch (error) {
      throw new Error(`Could not reach the local sign-in companion. Start AgentHost with this site's origin and check its access token. ${error.name === 'TimeoutError' ? 'The request timed out.' : ''}`);
    }
    const result = await response.json();
    if (!response.ok) throw new Error(String(result.error || result.message || `Companion returned HTTP ${response.status}.`).split(this.token).join('[redacted]'));
    return result;
  }
  async login() {
    const result = await this.request('login', 'POST');
    const url = new URL(result.authorizationUrl);
    if (url.protocol !== 'https:') throw new Error('The companion returned a non-HTTPS authorization URL.');
    return { ...result, authorizationUrl: url.href };
  }
  status() { return this.request('status'); }
  logout() { return this.request('logout', 'POST'); }
}
