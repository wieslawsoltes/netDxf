import { createWorkerSource } from './execution-worker.mjs';

function validateReceipt(result, outputLimit) {
  if (!result || !Number.isInteger(result.exitCode) || result.exitCode < -2147483648 || result.exitCode > 2147483647
    || typeof result.standardOutput !== 'string' || result.standardOutput.length > outputLimit
    || typeof result.standardError !== 'string' || result.standardError.length > outputLimit
    || typeof result.truncated !== 'boolean' || typeof result.timedOut !== 'boolean'
    || !Number.isFinite(result.elapsedMilliseconds) || result.elapsedMilliseconds < 0
    || !Array.isArray(result.changes) || result.changes.length > 1024) throw new Error('Browser worker returned an invalid result.');
  const serialized = JSON.stringify(result.result);
  if (serialized === undefined || serialized.length > 200000) throw new Error('Browser worker result exceeds its value budget.');
  const paths = new Set(), encoder = new TextEncoder();
  let bytes = 0;
  for (const change of result.changes) {
    if (!change || typeof change.path !== 'string' || !change.path.trim() || change.path.length > 512
      || change.path.startsWith('/') || /[\\:\x00-\x1f\x7f]/u.test(change.path)
      || change.path.split('/').some(part => !part || part === '.' || part === '..') || paths.has(change.path)
      || change.content !== null && (typeof change.content !== 'string' || change.content.includes('\0'))) throw new Error('Browser worker returned an invalid file change.');
    paths.add(change.path);
    if (change.content !== null) bytes += encoder.encode(change.content).length;
    if (bytes > 16 * 1024 * 1024) throw new Error('Browser worker changes exceed the workspace byte budget.');
  }
}

/** Each run owns a fresh opaque-origin frame and worker, so cancellation can terminate a synchronous loop. */
export class BrowserCodeRuntime {
  constructor({ document: doc = globalThis.document, window: host = globalThis, frameUrl = new URL('./execution-frame.html', import.meta.url) } = {}) {
    this.document = doc; this.host = host; this.frameUrl = frameUrl; this.pending = new Map(); this.disposed = false;
  }
  execute(request) {
    if (this.disposed) return Promise.reject(new Error('The browser code runtime is closed.'));
    if (!request || typeof request.id !== 'string' || !/^[a-zA-Z0-9-]{1,80}$/u.test(request.id) || typeof request.code !== 'string'
      || request.code.length > 1000000 || !Array.isArray(request.files) || request.files.length > 512
      || !Array.isArray(request.arguments) || request.arguments.length > 256 || request.arguments.some(value => typeof value !== 'string' || value.length > 16000)
      || !Number.isInteger(request.maximumOutputCharacters) || request.maximumOutputCharacters < 1024 || request.maximumOutputCharacters > 128000
      || !Number.isInteger(request.timeoutSeconds) || request.timeoutSeconds < 1 || request.timeoutSeconds > 120)
      return Promise.reject(new Error('Invalid browser code request.'));
    if (this.pending.size) return Promise.reject(new Error('Another browser code run is still active.'));
    const started = performance.now(), id = request.id;
    return new Promise((resolve, reject) => {
      const frame = this.document.createElement('iframe');
      frame.hidden = true; frame.title = 'Isolated agent code'; frame.setAttribute('sandbox', 'allow-scripts');
      frame.setAttribute('aria-hidden', 'true'); frame.referrerPolicy = 'no-referrer';
      const url = new URL(this.frameUrl); url.hash = id; frame.src = url.href;
      const channel = new MessageChannel();
      let timer, stopTimer, settled = false, stopping = null, stdout = '', stderr = '', overflow = false;
      let teardownFinished;
      const teardown = new Promise(resolve => { teardownFinished = resolve; });
      const outputLimit = Math.min(128000, request.maximumOutputCharacters || 128000);
      const cleanup = () => {
        clearTimeout(timer); clearTimeout(stopTimer); this.host.removeEventListener('message', ready);
        channel.port1.close(); channel.port2.close(); frame.remove(); this.pending.delete(id);
      };
      const finish = (result, error) => {
        if (settled) return;
        settled = true;
        try { cleanup(); } finally { teardownFinished(); }
        error ? reject(error) : resolve(result);
      };
      const stopped = () => {
        if (stopping === 'cancel') finish(null, new DOMException('Browser execution cancelled; staged files were discarded.', 'AbortError'));
        else finish({ exitCode: 124, standardOutput: stdout, standardError: stderr + '\nExecution timed out; staged files were discarded.',
          truncated: overflow, timedOut: true, elapsedMilliseconds: performance.now() - started, result: null, changes: [] });
      };
      const stop = reason => {
        if (settled || stopping) return teardown;
        stopping = reason; clearTimeout(timer); channel.port1.postMessage({ type: 'stop' });
        // Removing the owning frame also destroys its worker if a startup failure prevents an acknowledgement.
        stopTimer = setTimeout(stopped, 1500);
        return teardown;
      };
      const ready = event => {
        if (event.source !== frame.contentWindow || event.data?.type !== 'shadow-code-ready' || event.data.id !== id) return;
        this.host.removeEventListener('message', ready);
        frame.contentWindow.postMessage({ type: 'shadow-code-connect', id }, '*', [channel.port2]);
      };
      channel.port1.onmessage = event => {
        const value = event.data;
        if (settled || !value || typeof value.type !== 'string') return;
        if (value.type === 'stopped') { if (stopping) stopped(); return; }
        if (stopping) return;
        if (value.type === 'connected') {
          channel.port1.postMessage({ type: 'run', source: createWorkerSource(), request });
        } else if (value.type === 'output') {
          if (typeof value.text !== 'string') return;
          const error = value.stream === 'stderr'; const used = error ? stderr.length : stdout.length;
          const text = value.text.slice(0, Math.max(0, outputLimit - used)); overflow ||= text.length < value.text.length;
          if (error) stderr += text; else stdout += text;
          if (text) this.host.dispatchEvent(new CustomEvent('shadow-agent-code-output', { detail: { id, stream: error ? 'stderr' : 'stdout', text } }));
        } else if (value.type === 'completed') {
          const result = value.result;
          try { validateReceipt(result, outputLimit); finish(result); }
          catch (error) { finish(null, error); }
        } else if (value.type === 'failure') finish(null, new Error(String(value.error || 'Browser worker failed.').slice(0, 16000)));
      };
      channel.port1.start(); this.pending.set(id, { stop });
      this.host.addEventListener('message', ready);
      timer = setTimeout(() => stop('timeout'), request.timeoutSeconds * 1000);
      this.document.body.append(frame);
    });
  }
  cancel(id) { return this.pending.get(id)?.stop('cancel') ?? Promise.resolve(); }
  dispose() { this.disposed = true; return Promise.all([...this.pending.values()].map(task => task.stop('cancel'))); }
}

export function installBrowserCodeRuntime() {
  const runtime = new BrowserCodeRuntime();
  globalThis.shadowStudioAgentCode = Object.freeze({ execute: request => runtime.execute(request), cancel: id => runtime.cancel(id) });
  globalThis.addEventListener('pagehide', event => { if (!event.persisted) runtime.dispose(); });
  return runtime;
}
