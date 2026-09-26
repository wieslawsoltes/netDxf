// Cross-process exclusion between mapped oracle assemblies and compiler writes.
// Readers may coexist. A writer fails closed rather than replacing a live DLL.
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
const alive = pid => {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try { process.kill(pid, 0); return true; } catch (error) { if (error.code === 'ESRCH') return false; return true; }
};
function record(file) {
  const value = JSON.parse(fs.readFileSync(file, 'utf8'));
  if (value.schemaVersion !== 1 || !Number.isInteger(value.pid) || value.pid <= 0 || typeof value.token !== 'string')
    throw new Error('Invalid oracle lease: ' + file);
  return value;
}
function layout(root) {
  const readers = path.join(root, '.oracle-readers'), writer = path.join(root, '.oracle-writer');
  fs.mkdirSync(readers, { recursive: true });
  return { readers, writer };
}
function writerExists(writer) {
  if (!fs.existsSync(writer)) return false;
  const owner = path.join(writer, 'owner.json');
  // A missing owner can be a live mkdir/write race. Never remove that lock.
  if (!fs.existsSync(owner)) return true;
  let metadata;
  try { metadata = record(owner); } catch (error) { if (error.code === 'ENOENT') return fs.existsSync(writer); throw error; }
  if (!alive(metadata.pid)) throw new Error('Stale oracle writer lease; verify the compiler has exited before removing ' + writer);
  return true;
}
export function acquireOracleLease(root, { childProcess = false } = {}) {
  const { readers, writer } = layout(root);
  if (writerExists(writer)) throw new Error('Oracle build is active; cannot start an assembly reader.');
  const metadata = { schemaVersion: 1, pid: process.pid, childPid: null, pendingChild: childProcess, token: randomUUID() };
  const file = path.join(readers, metadata.token + '.json');
  fs.writeFileSync(file, JSON.stringify(metadata), { flag: 'wx' });
  const release = () => { try { fs.unlinkSync(file); } catch (error) { if (error.code !== 'ENOENT') throw error; } };
  try { if (writerExists(writer)) throw new Error('Oracle build started while acquiring reader lease.'); }
  catch (error) { release(); throw error; }
  return {
    bind(childPid) { if (Number.isInteger(childPid) && childPid > 0) { metadata.childPid = childPid; metadata.pendingChild = false; fs.writeFileSync(file, JSON.stringify(metadata)); } },
    release,
  };
}
export function acquireBuildLease(root) {
  const { readers, writer } = layout(root);
  if (writerExists(writer)) throw new Error('Another oracle build is active.');
  try { fs.mkdirSync(writer); } catch (error) { if (error.code === 'EEXIST') throw new Error('Another oracle build is active.'); throw error; }
  const owner = path.join(writer, 'owner.json');
  let released = false;
  const release = () => {
    if (released) return;
    fs.unlinkSync(owner); fs.rmdirSync(writer); released = true;
  };
  try {
    fs.writeFileSync(owner, JSON.stringify({ schemaVersion: 1, pid: process.pid, token: randomUUID() }), { flag: 'wx' });
    for (const name of fs.readdirSync(readers)) {
      const file = path.join(readers, name);
      let value;
      try { value = record(file); } catch (error) { if (error.code === 'ENOENT') continue; throw error; }
      if (alive(value.pid) || alive(value.childPid)) throw new Error('Oracle reader is active; close it before rebuilding assemblies.');
      if (value.pendingChild) throw new Error('Abandoned oracle reader may have an unregistered child; verify it has exited before removing ' + file);
      try { fs.unlinkSync(file); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    }
    return release;
  } catch (error) {
    if (fs.existsSync(owner)) release(); else fs.rmdirSync(writer);
    throw error;
  }
}
