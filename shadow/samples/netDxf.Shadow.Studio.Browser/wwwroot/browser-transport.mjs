const ASSEMBLY = 'netDxf.Shadow.Studio.WebAssembly';
const UPLOAD_LIMIT = 20 * 1024 * 1024;

/** Resolve only this application's API. Static assets and other origins are untouched. */
export function apiPath(value, baseURI) {
  const base = new URL(baseURI), url = new URL(value, base);
  if (url.origin !== base.origin) return null;
  const prefix = new URL('api/', base).pathname;
  const path = url.pathname.startsWith(prefix) ? `/api/${url.pathname.slice(prefix.length)}` :
    url.pathname.startsWith('/api/') ? url.pathname : null;
  return path === null ? null : path + url.search;
}

/** Adapter over the same C# commands used by the local host. No request is retried. */
export function installBrowserTransport({ nativeFetch = globalThis.fetch.bind(globalThis),
  invoke = (method, ...args) => DotNet.invokeMethodAsync(ASSEMBLY, method, ...args),
  baseURI = document.baseURI } = {}) {
  const originalFetch = globalThis.fetch, NativeEventSource = globalThis.EventSource;
  const subscriptions = new Set();
  let tail = Promise.resolve(), disposed = false, possiblyDirty = false;
  const ensureOpen = () => { if (disposed) throw new Error('The browser Studio session has closed.'); };
  const abort = signal => { if (signal?.aborted) throw signal.reason || new DOMException('The operation was aborted.', 'AbortError'); };

  function subscribe(lastEventId, onNotice, onError, onOpen = () => {}) {
    let id = null, closed = false;
    const handle = {
      close() {
        if (closed) return;
        closed = true; subscriptions.delete(handle);
        if (id) void invoke('Unsubscribe', id).catch(onError);
      }
    };
    subscriptions.add(handle);
    void (async () => {
      try {
        id = await invoke('Subscribe', lastEventId || null);
        if (closed || disposed) { await invoke('Unsubscribe', id); return; }
        onOpen();
        while (!closed && !disposed) {
          const notice = await invoke('ReadEvent', id);
          if (closed || disposed) break;
          if (notice === null) { handle.close(); break; }
          onNotice(notice);
        }
      } catch (error) { if (!closed && !disposed) onError(error); handle.close(); }
    })();
    return handle;
  }

  function eventResponse(request) {
    abort(request.signal);
    let handle, finish;
    const stream = new ReadableStream({
      start(controller) {
        let closed = false;
        const encoder = new TextEncoder();
        finish = (error, cancelled = false) => {
          if (closed) return;
          closed = true; handle?.close(); request.signal.removeEventListener('abort', onAbort);
          // The Streams algorithm closes the controller before calling cancel().
          // Closing it again would turn a normal reader cancellation into an error.
          if (!cancelled) { if (error) controller.error(error); else controller.close(); }
        };
        const onAbort = () => finish(request.signal.reason || new DOMException('Aborted', 'AbortError'));
        request.signal.addEventListener('abort', onAbort, { once: true });
        handle = subscribe(request.headers.get('Last-Event-ID'), notice => {
          if (!closed) controller.enqueue(encoder.encode(`id: ${notice.eventId}\nevent: state\ndata: ${notice.data}\n\n`));
        }, finish);
      },
      cancel() { finish?.(undefined, true); }
    });
    return new Response(stream, { headers: { 'Content-Type': 'text/event-stream; charset=utf-8', 'Cache-Control': 'no-cache' } });
  }

  function localFetch(input, init) {
    let request, path;
    try {
      const source = input instanceof Request ? input.url : String(input);
      path = apiPath(source, baseURI);
      if (path === null) return nativeFetch(input, init);
      ensureOpen();
      request = new Request(input instanceof Request ? input : new URL(source, baseURI), init);
      abort(request.signal);
      if (request.method === 'GET' && path.split('?')[0] === '/api/events') return Promise.resolve(eventResponse(request));
      const raw = init?.body;
      const size = raw instanceof Blob ? raw.size : raw instanceof ArrayBuffer ? raw.byteLength : ArrayBuffer.isView(raw) ? raw.byteLength : 0;
      if (size > UPLOAD_LIMIT) return Promise.resolve(new Response(JSON.stringify({ error: 'Upload size limit exceeded.' }), { status: 400, headers: { 'Content-Type': 'application/json' } }));
    } catch (error) { return Promise.reject(error); }
    const execute = async () => {
      ensureOpen(); abort(request.signal);
      let body = null, upload = null;
      if (request.method !== 'GET' && request.method !== 'HEAD') {
        if (request.headers.get('Content-Type')?.includes('application/json')) body = await request.json();
        else {
          upload = new Uint8Array(await request.arrayBuffer());
          if (upload.byteLength > UPLOAD_LIMIT) throw new Error('Upload size limit exceeded.');
        }
      }
      abort(request.signal);
      const result = await invoke('Dispatch', { method: request.method, path,
        headers: Object.fromEntries(request.headers.entries()), body }, upload);
      // A started synchronous .NET operation cannot be rolled back by AbortSignal.
      // Fence its response, but never retry a possibly committed mutation.
      if (result.status >= 200 && result.status < 300) {
        if (request.method === 'POST') possiblyDirty = true;
        if (path === '/api/cad/documents' && request.method === 'GET')
          possiblyDirty = JSON.parse(result.json ?? new TextDecoder().decode(result.bytes)).some(document => document.dirty);
      }
      abort(request.signal);
      return new Response(result.bytes ?? result.json, { status: result.status, headers: { 'Content-Type': result.contentType } });
    };
    const result = tail.then(execute);
    tail = result.catch(() => {});
    return result;
  }

  class BrowserEventSource extends EventTarget {
    static CONNECTING = 0; static OPEN = 1; static CLOSED = 2;
    CONNECTING = 0; OPEN = 1; CLOSED = 2;
    readyState = 0; withCredentials = false; onopen = null; onerror = null; onmessage = null;
    constructor(url, options) {
      super();
      const path = apiPath(String(url), baseURI);
      if (path?.split('?')[0] !== '/api/events') return new NativeEventSource(url, options);
      ensureOpen(); this.url = new URL(url, baseURI).href;
      this.subscription = subscribe(null, notice => {
        if (this.readyState === this.CLOSED) return;
        this.dispatchEvent(new MessageEvent('state', { data: notice.data, lastEventId: notice.eventId, origin: new URL(baseURI).origin }));
      }, () => {
        if (this.readyState === this.CLOSED) return;
        this.readyState = this.CLOSED;
        const event = new Event('error'); this.dispatchEvent(event); this.onerror?.(event);
      }, () => {
        if (this.readyState === this.CLOSED) return;
        this.readyState = this.OPEN;
        const event = new Event('open'); this.dispatchEvent(event); this.onopen?.(event);
      });
    }
    close() { this.readyState = this.CLOSED; this.subscription?.close(); }
  }
  const beforeUnload = event => {
    if (!possiblyDirty) return;
    event.preventDefault(); event.returnValue = '';
  };
  globalThis.fetch = localFetch;
  globalThis.EventSource = BrowserEventSource;
  globalThis.addEventListener?.('beforeunload', beforeUnload);
  return Object.freeze({
    fetch: localFetch,
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const subscription of [...subscriptions]) subscription.close();
      globalThis.removeEventListener?.('beforeunload', beforeUnload);
      if (globalThis.fetch === localFetch) globalThis.fetch = originalFetch;
      if (globalThis.EventSource === BrowserEventSource) globalThis.EventSource = NativeEventSource;
      void tail.finally(() => invoke('Shutdown')).catch(error => console.error(error));
    }
  });
}
