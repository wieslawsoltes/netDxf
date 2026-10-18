import { safeFilePath } from './contracts.mjs';

export const workspaceLimits = Object.freeze({ files: 512, bytes: 16 * 1024 * 1024, archiveBytes: 20 * 1024 * 1024 });
const encoder = new TextEncoder(), decoder = new TextDecoder('utf-8', { fatal: true });
const skipDirectory = new Set(['.git', '.svn', '.hg', 'node_modules', 'bin', 'obj']);
const skipPath = path => path.split('/').slice(0, -1).some(part => skipDirectory.has(part));
const bytesOf = content => encoder.encode(content).length;
const compare = (a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0;

export function normalizeFiles(input) {
  if (!Array.isArray(input) || input.length > workspaceLimits.files) throw new Error(`A browser workspace supports at most ${workspaceLimits.files} text files.`);
  let bytes = 0; const paths = new Set();
  const files = input.map(file => {
    const path = safeFilePath(file.path);
    if (paths.has(path)) throw new Error(`Duplicate workspace path: ${path}`);
    paths.add(path);
    if (typeof file.content !== 'string' || file.content.includes('\0')) throw new Error(`Only UTF-8 text files can be imported: ${path}`);
    bytes += bytesOf(file.content);
    if (bytes > workspaceLimits.bytes) throw new Error('The browser workspace exceeds its 16 MiB text budget. Import a smaller project or exclude generated files.');
    return { path, content: file.content, ...(Number.isSafeInteger(file.version) && file.version >= 0 ? { version: file.version } : {}) };
  });
  return files.sort(compare);
}

export function workspaceBundle(files) {
  return JSON.stringify({ format: 'netdxf.shadow.workspace', version: 1, createdAt: new Date().toISOString(), files: normalizeFiles(files).map(({ path, content }) => ({ path, content })) }, null, 2);
}
export function parseWorkspaceBundle(text) {
  if (typeof text !== 'string' || bytesOf(text) > workspaceLimits.archiveBytes) throw new Error('Workspace bundles must be smaller than 20 MiB.');
  let data; try { data = JSON.parse(text); } catch { throw new Error('The workspace bundle is not valid JSON.'); }
  if (data?.format !== 'netdxf.shadow.workspace' || data?.version !== 1) throw new Error('Choose a Shadow workspace bundle (version 1). Conversation checkpoints are imported from Conversation.');
  return normalizeFiles(data.files);
}

export function importPlan(current, incoming) {
  const existing = new Map(normalizeFiles(current).map(file => [file.path, file]));
  const rows = normalizeFiles(incoming).map(file => {
    const before = existing.get(file.path);
    return { ...file, before: before?.content ?? '', expectedVersion: before?.version ?? 0,
      kind: !before ? 'added' : before.content === file.content ? 'unchanged' : 'modified' };
  });
  const candidate = new Map(existing);
  for (const row of rows) candidate.set(row.path, row);
  normalizeFiles([...candidate.values()]);
  return { rows, changes: rows.filter(row => row.kind !== 'unchanged').map(({ path, content, expectedVersion }) => ({ path, content, expectedVersion })),
    added: rows.filter(row => row.kind === 'added').length, modified: rows.filter(row => row.kind === 'modified').length,
    unchanged: rows.filter(row => row.kind === 'unchanged').length };
}

/** A bounded context diff remains linear even for large or adversarial model output. */
export function lineDiff(before, after, context = 3, maximumLines = 180) {
  const left = String(before).split('\n'), right = String(after).split('\n'); let prefix = 0, suffix = 0;
  while (prefix < left.length && prefix < right.length && left[prefix] === right[prefix]) prefix++;
  if (prefix === left.length && prefix === right.length) return { lines: [], omitted: 0, removed: 0, added: 0 };
  while (suffix < left.length - prefix && suffix < right.length - prefix && left[left.length - 1 - suffix] === right[right.length - 1 - suffix]) suffix++;
  const lines = [], removed = left.length - prefix - suffix, added = right.length - prefix - suffix;
  const push = (kind, value, line) => { if (lines.length < maximumLines) lines.push({ kind, text: value.slice(0, 2000), line }); };
  for (let i = Math.max(0, prefix - context); i < prefix; i++) push('context', left[i], i + 1);
  // Give both sides a visible budget; a long removal cannot hide every new line.
  const sideBudget = Math.max(1, Math.floor((maximumLines - context * 2) / 2));
  for (let i = prefix; i < Math.min(left.length - suffix, prefix + sideBudget); i++) push('removed', left[i], i + 1);
  for (let i = prefix; i < Math.min(right.length - suffix, prefix + sideBudget); i++) push('added', right[i], i + 1);
  for (let i = right.length - suffix; i < Math.min(right.length, right.length - suffix + context); i++) push('context', right[i], i + 1);
  return { lines, removed, added, omitted: Math.max(0, removed - sideBudget) + Math.max(0, added - sideBudget) };
}

function decodeText(data, path) {
  let text; try { text = decoder.decode(data); } catch { throw new Error(`The file is not UTF-8 text: ${path}`); }
  if (text.includes('\0')) throw new Error(`The file contains binary data: ${path}`);
  return text;
}
export async function readSelectedFiles(selected, { directory = false } = {}) {
  const input = Array.from(selected || []), files = [], skipped = []; let bytes = 0;
  for (const file of input) {
    const original = directory && file.webkitRelativePath ? file.webkitRelativePath.split('/').slice(1).join('/') : file.name;
    const path = safeFilePath(original);
    if (skipPath(path)) { skipped.push({ path, reason: 'Generated or repository metadata directory' }); continue; }
    if (files.length >= workspaceLimits.files) throw new Error('The selected project contains more than 512 text files. Choose a smaller folder.');
    if (file.size > workspaceLimits.bytes || bytes + file.size > workspaceLimits.bytes) throw new Error('The selected files exceed the 16 MiB workspace budget.');
    try { const content = decodeText(await file.arrayBuffer(), path); files.push({ path, content }); bytes += bytesOf(content); }
    catch (error) { if (/not UTF-8|binary data/.test(error.message)) skipped.push({ path, reason: error.message }); else throw error; }
  }
  return { files: normalizeFiles(files), skipped };
}

export class BrowserFolder {
  constructor(handle) { if (!handle || handle.kind !== 'directory') throw new Error('Choose a directory.'); this.handle = handle; this.name = handle.name; this.baseline = new Map(); }
  async permission(mode, request = false) {
    const options = { mode };
    if (await this.handle.queryPermission(options) === 'granted') return;
    if (request && await this.handle.requestPermission(options) === 'granted') return;
    throw new Error(`Folder ${mode === 'readwrite' ? 'write' : 'read'} permission was not granted. Reconnect the folder to continue.`);
  }
  async read() {
    await this.permission('read');
    const files = [], skipped = []; let bytes = 0;
    const visit = async (directory, prefix = '', depth = 0) => {
      if (depth > 64) throw new Error('The folder nesting exceeds the browser workspace limit.');
      for await (const [name, handle] of directory.entries()) {
        const path = safeFilePath(prefix + name);
        if (handle.kind === 'directory') {
          if (skipDirectory.has(name)) { skipped.push({ path, reason: 'Generated or repository metadata directory' }); continue; }
          await visit(handle, `${path}/`, depth + 1); continue;
        }
        if (handle.kind !== 'file') continue;
        if (files.length >= workspaceLimits.files) throw new Error('The folder contains more than 512 text files. Choose a smaller project folder.');
        const file = await handle.getFile();
        if (file.size > workspaceLimits.bytes || bytes + file.size > workspaceLimits.bytes) throw new Error('The folder exceeds the 16 MiB browser text workspace budget.');
        try { const content = decodeText(await file.arrayBuffer(), path); files.push({ path, content }); bytes += bytesOf(content); }
        catch (error) { if (/not UTF-8|binary data/.test(error.message)) skipped.push({ path, reason: error.message }); else throw error; }
      }
    };
    await visit(this.handle);
    const result = normalizeFiles(files); this.baseline = new Map(result.map(file => [file.path, file.content]));
    return { files: result, skipped };
  }
  async fileHandle(path, create = false) {
    const parts = safeFilePath(path).split('/'); let directory = this.handle;
    for (const part of parts.slice(0, -1)) directory = await directory.getDirectoryHandle(part, { create });
    return directory.getFileHandle(parts.at(-1), { create });
  }
  async current(path) {
    try { const handle = await this.fileHandle(path), file = await handle.getFile(); if (file.size > workspaceLimits.bytes) throw new Error(`Folder file exceeds the workspace budget: ${path}`); return decodeText(await file.arrayBuffer(), path); }
    catch (error) { if (error.name === 'NotFoundError') return null; throw error; }
  }
  async planWrite(files) {
    await this.permission('read');
    const rows = [];
    for (const file of normalizeFiles(files)) {
      const current = await this.current(file.path), baseline = this.baseline.get(file.path);
      if (current === file.content) continue;
      rows.push({ ...file, before: current ?? '', expectedContent: current,
        kind: current === null ? 'added' : 'modified', conflict: current !== null && (baseline === undefined || current !== baseline) });
    }
    return rows;
  }
  async write(rows) {
    await this.permission('readwrite', true);
    if (!Array.isArray(rows) || rows.length > workspaceLimits.files) throw new Error('Invalid folder write plan.');
    normalizeFiles(rows);
    // Check all paths before opening any writable. Recheck each file immediately
    // before its atomic close because browser file APIs have no cross-file CAS.
    for (const row of rows) if (await this.current(row.path) !== row.expectedContent) throw new Error(`The folder changed after review: ${row.path}. Review folder changes again.`);
    const written = [];
    try {
      for (const row of rows) {
        if (await this.current(row.path) !== row.expectedContent) throw new Error(`The folder changed after review: ${row.path}.`);
        const handle = await this.fileHandle(row.path, true), stream = await handle.createWritable();
        try { await stream.write(row.content); await stream.close(); }
        catch (error) { await stream.abort().catch(() => {}); throw error; }
        written.push(row.path); this.baseline.set(row.path, row.content);
      }
    } catch (error) {
      const failure = new Error(`${error.message} ${written.length ? `${written.length} file(s) were already saved: ${written.join(', ')}.` : 'No workspace file contents were saved.'}`);
      failure.written = written; throw failure;
    }
    return { written };
  }
}

const crcTable = Uint32Array.from({ length: 256 }, (_, value) => {
  for (let i = 0; i < 8; i++) value = value & 1 ? 0xedb88320 ^ value >>> 1 : value >>> 1;
  return value >>> 0;
});
function crc32(data) { let value = 0xffffffff; for (const byte of data) value = crcTable[(value ^ byte) & 255] ^ value >>> 8; return (value ^ 0xffffffff) >>> 0; }
function zipHeader(length, signature) { const data = new Uint8Array(length); new DataView(data.buffer).setUint32(0, signature, true); return data; }

/** Portable ZIP output uses STORE entries. No CDN, package download, or compression worker is required. */
export function workspaceZip(files) {
  const entries = normalizeFiles(files), chunks = [], central = []; let offset = 0, centralBytes = 0;
  for (const file of entries) {
    const name = encoder.encode(file.path), content = encoder.encode(file.content), crc = crc32(content);
    const local = zipHeader(30 + name.length, 0x04034b50), lv = new DataView(local.buffer);
    lv.setUint16(4, 20, true); lv.setUint16(6, 0x800, true); lv.setUint32(14, crc, true); lv.setUint32(18, content.length, true); lv.setUint32(22, content.length, true); lv.setUint16(26, name.length, true); local.set(name, 30);
    const index = zipHeader(46 + name.length, 0x02014b50), iv = new DataView(index.buffer);
    iv.setUint16(4, 20, true); iv.setUint16(6, 20, true); iv.setUint16(8, 0x800, true); iv.setUint32(16, crc, true); iv.setUint32(20, content.length, true); iv.setUint32(24, content.length, true); iv.setUint16(28, name.length, true); iv.setUint32(42, offset, true); index.set(name, 46);
    chunks.push(local, content); central.push(index); centralBytes += index.length; offset += local.length + content.length;
  }
  const end = zipHeader(22, 0x06054b50), ev = new DataView(end.buffer);
  ev.setUint16(8, entries.length, true); ev.setUint16(10, entries.length, true); ev.setUint32(12, centralBytes, true); ev.setUint32(16, offset, true);
  const output = new Uint8Array(offset + centralBytes + end.length); let position = 0;
  for (const chunk of [...chunks, ...central, end]) { output.set(chunk, position); position += chunk.length; }
  return output;
}

async function inflate(data, expected) {
  if (!globalThis.DecompressionStream) throw new Error('This browser cannot decompress this ZIP. Extract it and use Import folder, or import a Shadow workspace bundle.');
  let stream;
  try { stream = new Blob([data]).stream().pipeThrough(new DecompressionStream('deflate-raw')); }
  catch { throw new Error('This browser cannot decompress this ZIP. Extract it and use Import folder.'); }
  const reader = stream.getReader(), chunks = []; let length = 0;
  try {
    while (true) { const { done, value } = await reader.read(); if (done) break; length += value.length; if (length > expected) throw new Error('The ZIP expands beyond its declared size.'); chunks.push(value); }
  } catch (error) { await reader.cancel().catch(() => {}); throw error; }
  if (length !== expected) throw new Error('The ZIP entry size is inconsistent.');
  const result = new Uint8Array(length); let position = 0; for (const chunk of chunks) { result.set(chunk, position); position += chunk.length; } return result;
}
export async function readWorkspaceZip(input, { inflateEntry = inflate } = {}) {
  const data = input instanceof Uint8Array ? input : new Uint8Array(input);
  if (data.length < 22 || data.length > workspaceLimits.archiveBytes) throw new Error('Choose a ZIP smaller than 20 MiB.');
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength); let end = -1;
  for (let position = data.length - 22; position >= Math.max(0, data.length - 65557); position--) {
    if (view.getUint32(position, true) === 0x06054b50 && position + 22 + view.getUint16(position + 20, true) === data.length) { end = position; break; }
  }
  if (end < 0 || view.getUint16(end + 4, true) || view.getUint16(end + 6, true)) throw new Error('The ZIP is invalid or split across disks.');
  const count = view.getUint16(end + 10, true), centralSize = view.getUint32(end + 12, true), centralStart = view.getUint32(end + 16, true);
  if (count > workspaceLimits.files || view.getUint16(end + 8, true) !== count || centralStart + centralSize > end) throw new Error('ZIP64, oversized, and inconsistent ZIP archives are not supported.');
  const files = [], skipped = []; let position = centralStart, total = 0;
  for (let index = 0; index < count; index++) {
    if (position + 46 > centralStart + centralSize || view.getUint32(position, true) !== 0x02014b50) throw new Error('The ZIP directory is corrupt.');
    const flags = view.getUint16(position + 8, true), method = view.getUint16(position + 10, true), expectedCrc = view.getUint32(position + 16, true);
    const packed = view.getUint32(position + 20, true), size = view.getUint32(position + 24, true), nameSize = view.getUint16(position + 28, true);
    const extra = view.getUint16(position + 30, true), comment = view.getUint16(position + 32, true), external = view.getUint32(position + 38, true), local = view.getUint32(position + 42, true);
    if (flags & 1 || ![0, 8].includes(method) || packed === 0xffffffff || size === 0xffffffff) throw new Error('Encrypted, ZIP64, or unsupported compressed entries cannot be imported.');
    const next = position + 46 + nameSize + extra + comment;
    if (next > centralStart + centralSize) throw new Error('The ZIP directory entry is truncated.');
    const rawName = decodeText(data.subarray(position + 46, position + 46 + nameSize), 'ZIP filename'); position = next;
    const path = safeFilePath(rawName.replace(/\/$/, ''));
    if ((external >>> 16 & 0xf000) === 0xa000) throw new Error(`Symbolic links are not imported: ${path}`);
    if (rawName.endsWith('/') || skipPath(path)) { skipped.push({ path, reason: 'Directory or generated metadata' }); continue; }
    total += size; if (total > workspaceLimits.bytes) throw new Error('The ZIP expands beyond the 16 MiB workspace budget.');
    if (local + 30 > centralStart || view.getUint32(local, true) !== 0x04034b50) throw new Error('The ZIP file entry is corrupt.');
    const start = local + 30 + view.getUint16(local + 26, true) + view.getUint16(local + 28, true);
    if (start + packed > centralStart) throw new Error('The ZIP file entry is truncated.');
    if (view.getUint16(local + 8, true) !== method || (view.getUint16(local + 6, true) & 1)) throw new Error('The ZIP file headers disagree.');
    const content = method === 0 ? data.slice(start, start + packed) : await inflateEntry(data.slice(start, start + packed), size);
    if (content.length !== size || crc32(content) !== expectedCrc) throw new Error(`The ZIP checksum failed: ${path}`);
    try { files.push({ path, content: decodeText(content, path) }); }
    catch (error) { skipped.push({ path, reason: error.message }); }
  }
  if (position !== centralStart + centralSize) throw new Error('The ZIP directory size is inconsistent.');
  return { files: normalizeFiles(files), skipped };
}
