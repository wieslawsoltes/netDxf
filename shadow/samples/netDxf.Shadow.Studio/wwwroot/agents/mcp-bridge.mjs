import { endpointUrl } from './contracts.mjs';

/** An explicitly connected, document-bound bridge. Lost mutations are never reconnected or replayed. */
export class StudioMcpBridge extends EventTarget {
  constructor({ client, getDocument, socketFactory = url => new WebSocket(url) }) {
    super(); this.client = client; this.getDocument = getDocument; this.socketFactory = socketFactory;
    this.socket = null; this.documentId = null; this.connected = false; this.generation = 0; this.calls = new Map(); this.pendingConnect = null;
  }
  emit(name, detail) { const event = new Event(name); event.detail = detail; this.dispatchEvent(event); }
  async connect(endpoint, token) {
    this.client.ensureIdle();
    const base = endpointUrl(endpoint || 'http://127.0.0.1:5091', { companion: true }), credential = String(token || '').trim();
    if (!credential || /\s/.test(credential)) throw new Error('Enter a valid companion access token to connect external MCP.');
    const documentId = this.getDocument(); if (!documentId) throw new Error('Open a drawing before connecting external MCP.');
    this.disconnect(); const generation = this.generation; this.documentId = documentId;
    if (!this.client.secrets.includes(credential)) this.client.secrets.push(credential);
    let tools;
    try {
      await this.client.bindDocument(documentId); tools = await this.client.tools();
      if (generation !== this.generation) throw new Error('MCP registration was cancelled. Reconnect explicitly to start another connection.');
      if (documentId !== this.getDocument()) throw new Error('The active drawing changed. Reconnect MCP to bind the new drawing.');
      this.client.ensureIdle();
    } catch (error) { if (generation === this.generation) this.disconnect(); throw error; }
    const url = new URL(`${base}/studio`); url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    let socket;
    try { socket = this.socketFactory(url.href); } catch (error) { this.disconnect(); throw error; }
    this.socket = socket; this.calls.clear();
    return new Promise((resolve, reject) => {
      let settled = false;
      const timeout = setTimeout(() => fail('The MCP companion did not register this Studio tab within 15 seconds.'), 15000);
      const pending = { reject: message => { clearTimeout(timeout); if (!settled) { settled = true; reject(new Error(message)); } } };
      this.pendingConnect = pending;
      const fail = message => {
        if (generation !== this.generation || socket !== this.socket) return;
        pending.reject(message); this.emit('error', { message }); this.disconnect(message);
      };
      socket.addEventListener('open', () => {
        if (generation !== this.generation || socket !== this.socket) return;
        try { socket.send(JSON.stringify({ type: 'register', token: credential, tools })); }
        catch { fail('The MCP companion closed before this Studio tab could register.'); }
      });
      socket.addEventListener('message', event => {
        if (generation !== this.generation || socket !== this.socket) return;
        let message;
        try { message = JSON.parse(event.data); } catch { fail('The MCP companion sent an invalid message.'); return; }
        if (!message || typeof message !== 'object') { fail('The MCP companion sent an invalid message.'); return; }
        if (message.type === 'registered') {
          if (this.connected) return;
          this.connected = true; settled = true; clearTimeout(timeout); this.emit('connected', { documentId }); resolve({ documentId });
          if (this.pendingConnect === pending) this.pendingConnect = null;
        } else if (message.type === 'call') {
          if (!this.connected) { fail('The MCP companion sent a tool call before registration.'); return; }
          void this.receive(message, socket, generation);
        } else if (message.type === 'error') fail(this.client.clean(message.message || message.error || 'MCP companion connection failed.'));
      });
      socket.addEventListener('error', () => fail('Could not connect to the MCP companion. Check its endpoint, token, and allowed origin.'));
      socket.addEventListener('close', event => {
        clearTimeout(timeout);
        if (generation !== this.generation || socket !== this.socket) return;
        this.disconnect(`MCP companion connection closed (${event.code}).`);
        this.emit('disconnected', { reason: 'Connection closed. Reconnect explicitly to accept further tool calls.' });
      });
    });
  }
  async receive(message, socket, generation) {
    if (typeof message.id !== 'string' || !message.id || typeof message.name !== 'string') return;
    // Hold a settled result for duplicate delivery while this explicit connection remains alive.
    const signature = JSON.stringify([message.name, message.arguments]);
    let entry = this.calls.get(message.id), task;
    if (entry && entry.signature !== signature) {
      task = Promise.resolve({ text: 'A completed MCP call ID was reused with different arguments. No action was executed.', isError: true });
    } else if (entry) task = entry.task;
    else if (this.calls.size >= 4096) {
      task = Promise.resolve({ text: 'This MCP connection has reached its call receipt limit. Reconnect explicitly to start another connection.', isError: true });
    } else {
      const documentId = this.documentId;
      task = Promise.resolve().then(async () => {
        try {
          if (generation !== this.generation || socket !== this.socket) throw new Error('MCP disconnected before this tool call was dispatched.');
          if (documentId !== this.getDocument()) throw new Error('The active drawing changed. Reconnect MCP to bind the new drawing.');
          if (!message.arguments || typeof message.arguments !== 'object' || Array.isArray(message.arguments)) throw new Error('MCP tool arguments must be a JSON object.');
          const result = await this.client.callTool(message.name, message.arguments, { documentId });
          return { text: typeof result === 'string' ? result : typeof result?.text === 'string' ? result.text : JSON.stringify(result), isError: result?.isError === true };
        } catch (error) { return { text: this.client.clean(error.message || String(error)), isError: true }; }
      });
      this.calls.set(message.id, { signature, task });
    }
    const result = await task;
    if (generation === this.generation && socket === this.socket && socket.readyState === 1) {
      try { socket.send(JSON.stringify({ type: 'result', id: message.id, result })); }
      catch { this.disconnect(); this.emit('disconnected', { reason: 'The MCP result connection closed. Reconnect explicitly; completed edits are not replayed.' }); }
    }
  }
  documentChanged() {
    if (this.documentId && this.documentId !== this.getDocument()) {
      this.disconnect(); this.emit('disconnected', { reason: 'The active drawing changed. Reconnect to bind the new drawing.' });
    }
  }
  disconnect(reason = 'MCP registration was cancelled. Reconnect explicitly to start another connection.') {
    this.generation++; const socket = this.socket; this.socket = null; this.connected = false; this.documentId = null;
    const pending = this.pendingConnect; this.pendingConnect = null; pending?.reject(reason);
    if (socket && socket.readyState < 2) socket.close(1000, 'Disconnected by Studio');
    this.calls.clear();
  }
}
