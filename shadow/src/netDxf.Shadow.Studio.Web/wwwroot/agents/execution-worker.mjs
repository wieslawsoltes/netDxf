/** Self-contained bootstrap serialized into an opaque-origin worker; no app modules or credentials enter it. */
export function workerBootstrap(receiptToken) {
  const post = self.postMessage.bind(self);
  const send = message => post({ ...message, receiptToken });
  const now = performance.now.bind(performance);
  const stringify = JSON.stringify.bind(JSON), parse = JSON.parse.bind(JSON);
  const schedule = setTimeout.bind(self), unschedule = clearTimeout.bind(self);
  self.onmessage = async event => {
    self.onmessage = null;
    const request = event.data;
    const started = now();
    const limit = Math.min(128000, Math.max(1024, request.maximumOutputCharacters || 128000));
    let standardOutput = '', standardError = '', truncated = false;
    const pending = ['', ''], sent = [false, false];
    let outputTimer;
    const flushStream = index => {
      const text = pending[index]; pending[index] = '';
      for (let offset = 0; offset < text.length; offset += 4096)
        send({ type: 'output', stream: index ? 'stderr' : 'stdout', text: text.slice(offset, offset + 4096) });
    };
    const flushOutput = () => {
      if (outputTimer !== undefined) unschedule(outputTimer);
      outputTimer = undefined;
      flushStream(0); flushStream(1);
    };
    const encoder = new TextEncoder();
    const normalize = path => {
      if (typeof path !== 'string' || !path.trim() || path.length > 512 || path.startsWith('/') || /[\\:\x00-\x1f\x7f]/u.test(path)
        || path.split('/').some(part => !part || part === '.' || part === '..')) throw new Error('Use a relative workspace path with forward slashes.');
      return path;
    };
    const original = new Map(), files = new Map();
    let bytes = 0;
    const printable = value => {
      if (typeof value === 'string') return value;
      try { const seen = new WeakSet(); return stringify(value, (_, item) => {
        if (typeof item === 'bigint') return String(item);
        if (typeof item === 'object' && item !== null) { if (seen.has(item)) return '[circular]'; seen.add(item); }
        return item;
      }) ?? String(value); } catch { return '[unprintable value]'; }
    };
    const output = (error, values) => {
      const available = limit - (error ? standardError.length : standardOutput.length);
      if (available <= 0) { truncated = true; return; }
      let line = values.map(printable).join(' ') + '\n';
      if (line.length > available) { truncated = true; line = line.slice(0, available); }
      if (error) standardError += line; else standardOutput += line;
      const index = error ? 1 : 0;
      pending[index] += line;
      // Preserve initial progress even before a synchronous loop. Batch subsequent logs
      // so short, fast log calls cannot flood the frame and UI message queues.
      if (!sent[index] || pending[index].length >= 4096) { sent[index] = true; flushStream(index); }
      if ((pending[0] || pending[1]) && outputTimer === undefined) outputTimer = schedule(flushOutput, 32);
    };
    try {
      if (!request || typeof request.code !== 'string' || request.code.length > 1000000 || !Array.isArray(request.files) || request.files.length > 512)
        throw new Error('Invalid browser code request.');
      for (const file of request.files) {
        const path = normalize(file.path);
        if (typeof file.content !== 'string' || file.content.includes('\0') || files.has(path)) throw new Error('Invalid workspace snapshot.');
        bytes += encoder.encode(file.content).length;
        if (bytes > 16 * 1024 * 1024) throw new Error('Workspace exceeds its byte budget.');
        files.set(path, file.content); original.set(path, file.content);
      }
      const workspace = Object.freeze({
        list(prefix = '') { return [...files.keys()].filter(path => path.startsWith(String(prefix))).sort(); },
        read(path) { path = normalize(path); if (!files.has(path)) throw new Error(`Workspace file does not exist: ${path}`); return files.get(path); },
        write(path, content) {
          path = normalize(path); if (typeof content !== 'string' || content.includes('\0')) throw new Error('Workspace content must be text without NUL bytes.');
          const size = bytes - encoder.encode(files.get(path) || '').length + encoder.encode(content).length;
          if (size > 16 * 1024 * 1024 || !files.has(path) && files.size >= 512) throw new Error('Workspace capacity exceeded.');
          files.set(path, content); bytes = size;
        },
        delete(path) { path = normalize(path); if (!files.has(path)) throw new Error(`Workspace file does not exist: ${path}`); bytes -= encoder.encode(files.get(path)).length; files.delete(path); }
      });
      const logger = Object.freeze({ log: (...values) => output(false, values), info: (...values) => output(false, values),
        warn: (...values) => output(true, values), error: (...values) => output(true, values), debug: (...values) => output(false, values) });
      const assert = (condition, message = 'Assertion failed') => { if (!condition) throw new Error(String(message)); };
      const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
      const execute = new AsyncFunction('workspace', 'args', 'console', 'assert', '"use strict";\n' + request.code + '\n//# sourceURL=shadow-workspace.js');
      const value = await execute(workspace, Object.freeze([...(request.arguments || [])]), logger, assert);
      const changes = [];
      for (const [path, content] of files) if (original.get(path) !== content) changes.push({ path, content });
      for (const path of original.keys()) if (!files.has(path)) changes.push({ path, content: null });
      let result = value === undefined ? null : printable(value);
      if (typeof result === 'string' && result.length > 32000) { result = result.slice(0, 32000); truncated = true; }
      else if (typeof value !== 'string' && result !== null) { try { result = parse(result); } catch { /* a readable description is sufficient */ } }
      flushOutput();
      send({ type: 'completed', result: { exitCode: 0, standardOutput, standardError, truncated, timedOut: false,
        elapsedMilliseconds: now() - started, result, changes } });
    } catch (error) {
      output(true, [error?.stack || error?.message || String(error)]);
      flushOutput();
      send({ type: 'completed', result: { exitCode: 1, standardOutput, standardError, truncated, timedOut: false,
        elapsedMilliseconds: now() - started, result: null, changes: [] } });
    }
  };
}

// The trusted frame invokes this function with a private receipt token. Code executed by
// AsyncFunction has global scope and cannot read the bootstrap's lexical argument.
export function createWorkerSource() { return `(${workerBootstrap.toString()})`; }
