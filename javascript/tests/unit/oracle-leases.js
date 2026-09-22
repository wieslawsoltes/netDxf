import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { acquireOracleLease, acquireBuildLease } from '../../tools/oracle-leases.mjs';
const temporary = action => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'netdxf-oracle-lease-'));
  try { action(root); } finally { fs.rmSync(root, { recursive: true, force: true }); }
};
test('oracle readers coexist and prevent compiler replacement until every reader closes', () => temporary(root => {
  const first = acquireOracleLease(root), second = acquireOracleLease(root);
  try {
    assert.throws(() => acquireBuildLease(root), /reader is active/);
    first.release(); assert.throws(() => acquireBuildLease(root), /reader is active/);
    second.release(); const release = acquireBuildLease(root); release(); release();
  } finally { first.release(); second.release(); }
}));
test('oracle compiler excludes new readers and competing compilers', () => temporary(root => {
  const release = acquireBuildLease(root);
  try {
    assert.throws(() => acquireOracleLease(root), /build is active/);
    assert.throws(() => acquireBuildLease(root), /build is active/);
  } finally { release(); }
  acquireOracleLease(root).release();
}));
test('oracle build exclusion also holds across Node processes', () => temporary(root => {
  const release = acquireBuildLease(root);
  try {
    const module = new URL('../../tools/oracle-leases.mjs', import.meta.url).href;
    const code = `import { acquireOracleLease } from ${JSON.stringify(module)}; try { acquireOracleLease(${JSON.stringify(root)}); process.exitCode=2; } catch (e) { if (!/build is active/.test(e.message)) throw e; }`;
    const result = spawnSync(process.execPath, ['--input-type=module', '-e', code], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
  } finally { release(); }
}));
test('malformed oracle lease fails closed and releases the attempted writer lock', () => temporary(root => {
  const lease = acquireOracleLease(root); lease.release();
  const file = path.join(root, '.oracle-readers', 'broken.json'); fs.writeFileSync(file, '{}');
  assert.throws(() => acquireBuildLease(root), /Invalid oracle lease/);
  assert.equal(fs.existsSync(path.join(root, '.oracle-writer')), false);
  assert.equal(fs.readFileSync(file, 'utf8'), '{}');
}));
test('reader leases from terminated processes are reclaimed without deleting assembly files', () => temporary(root => {
  const module = new URL('../../tools/oracle-leases.mjs', import.meta.url).href;
  const child = spawnSync(process.execPath, ['--input-type=module', '-e', `import { acquireOracleLease } from ${JSON.stringify(module)}; acquireOracleLease(${JSON.stringify(root)});`], { encoding: 'utf8' });
  assert.equal(child.status, 0, child.stderr);
  const assembly = path.join(root, 'Oracle.dll'); fs.writeFileSync(assembly, 'untouched');
  const release = acquireBuildLease(root); release();
  assert.equal(fs.readFileSync(assembly, 'utf8'), 'untouched');
  assert.equal(fs.readdirSync(path.join(root, '.oracle-readers')).length, 0);
}));
test('incomplete writer ownership metadata remains blocking rather than being guessed stale', () => temporary(root => {
  fs.mkdirSync(path.join(root, '.oracle-writer'));
  assert.throws(() => acquireOracleLease(root), /build is active/);
  assert.throws(() => acquireBuildLease(root), /build is active/);
}));

test('a terminated reader in the child-registration window remains blocking', () => temporary(root => {
  const module = new URL('../../tools/oracle-leases.mjs', import.meta.url).href;
  const child = spawnSync(process.execPath, ['--input-type=module', '-e', `import { acquireOracleLease } from ${JSON.stringify(module)}; acquireOracleLease(${JSON.stringify(root)}, { childProcess: true });`], { encoding: 'utf8' });
  assert.equal(child.status, 0, child.stderr);
  assert.throws(() => acquireBuildLease(root), /unregistered child/);
  assert.equal(fs.readdirSync(path.join(root, '.oracle-readers')).length, 1);
}));
test('an abandoned compiler lock requires explicit inspection instead of racy automatic reclamation', () => temporary(root => {
  const module = new URL('../../tools/oracle-leases.mjs', import.meta.url).href;
  const child = spawnSync(process.execPath, ['--input-type=module', '-e', `import { acquireBuildLease } from ${JSON.stringify(module)}; acquireBuildLease(${JSON.stringify(root)});`], { encoding: 'utf8' });
  assert.equal(child.status, 0, child.stderr);
  assert.throws(() => acquireBuildLease(root), /Stale oracle writer/);
  assert.throws(() => acquireOracleLease(root), /Stale oracle writer/);
  assert.ok(fs.existsSync(path.join(root, '.oracle-writer', 'owner.json')));
}));
