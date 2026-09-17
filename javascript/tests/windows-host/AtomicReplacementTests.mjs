// Actual Windows/Node-API integration tests, separate from portable contract tests.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createRequire } from 'node:module';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { ReplaceWindowsFile } from '../../runtime/WindowsFileReplacement.js';
import { FileNotFoundException, DirectoryNotFoundException, ArgumentException,
  ArgumentNullException } from '../../runtime/Errors.js';
if (process.platform !== 'win32') throw new Error('Run this integration suite on Windows; a mock or skipped test cannot qualify the host.');
const root = fileURLToPath(new URL('../../', import.meta.url));
const require = createRequire(import.meta.url);
const native = require(`../../native/bin/win32-${process.arch}/netdxf_windows.node`);
function directory(action) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'netdxf-win-host-'));
  try { return action(dir); } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

test('native argument guards reject missing, non-string and NUL paths', () => {
  for (const args of [[], ['only-one'], [null, 'b'], ['a', null], [42, 'b'], ['a', {}], ['', 'b'], ['a\0', 'b'], ['a', 'b\0'], ['x'.repeat(32767), 'b']])
    assert.throws(() => native.replaceFile(...args), error => /^ERR_/.test(error.code));
});
test('JS path guards run before native loading/publication', () => {
  for (const [value, Type] of [[null,ArgumentNullException],[undefined,ArgumentNullException],['',ArgumentException],[3,ArgumentException],['a\0',ArgumentException]]) {
    assert.throws(() => ReplaceWindowsFile(value, 'b'), Type);
    assert.throws(() => ReplaceWindowsFile('a', value), Type);
  }
});
test('Unicode replacement keeps an existing reader on the old file identity', () => directory(dir => {
  const destination = path.join(dir, 'Zażółć-東京-😀.dxf'), staged = path.join(dir, 'staged-🧪.tmp');
  const old = Buffer.from([0,1,2,3,254,255]), next = Buffer.from('new-drawing-東京');
  fs.writeFileSync(destination, old); fs.writeFileSync(staged, next);
  const reader = fs.openSync(destination, 'r');
  try {
    ReplaceWindowsFile(staged, destination);
    assert.deepEqual(fs.readFileSync(destination), next);
    const previous = Buffer.alloc(old.length);
    assert.equal(fs.readSync(reader, previous, 0, previous.length, 0), old.length);
    assert.deepEqual(previous, old);
    assert.equal(fs.existsSync(staged), false);
    assert.deepEqual(fs.readdirSync(dir), [path.basename(destination)]);
  } finally { fs.closeSync(reader); }
}));
test('missing replacement source leaves the existing destination unchanged', () => directory(dir => {
  const destination = path.join(dir, 'unchanged.dxf'), old = Buffer.from('original');
  fs.writeFileSync(destination, old);
  assert.throws(() => ReplaceWindowsFile(path.join(dir, 'missing.tmp'), destination),
    error => (error instanceof FileNotFoundException || error instanceof DirectoryNotFoundException) && [2,3].includes(error.Win32ErrorCode));
  assert.deepEqual(fs.readFileSync(destination), old);
  assert.deepEqual(fs.readdirSync(dir), ['unchanged.dxf']);
}));
test('an unbuilt installation rejects twice without mutating either file', () => directory(dir => {
  const runtime = path.join(dir, 'runtime'); fs.mkdirSync(runtime);
  for (const file of ['WindowsFileReplacement.js','WindowsReplacementContract.js','Errors.js'])
    fs.copyFileSync(path.join(root, 'runtime', file), path.join(runtime, file));
  fs.writeFileSync(path.join(dir, 'package.json'), '{"type":"module"}');
  fs.writeFileSync(path.join(dir, 'staged.tmp'), 'staged'); fs.writeFileSync(path.join(dir, 'existing.dxf'), 'original');
  const script = `import assert from 'node:assert/strict';
    import {ReplaceWindowsFile} from './runtime/WindowsFileReplacement.js';
    import {NotSupportedException} from './runtime/Errors.js';
    for(let i=0;i<2;i++)assert.throws(()=>ReplaceWindowsFile('staged.tmp','existing.dxf'),e=>e instanceof NotSupportedException&&e.cause?.code==='MODULE_NOT_FOUND');`;
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', script], { cwd: dir, encoding: 'utf8', timeout: 15000, windowsHide: true });
  assert.ifError(result.error); assert.equal(result.status, 0, result.stderr);
  assert.equal(fs.readFileSync(path.join(dir, 'staged.tmp'), 'utf8'), 'staged');
  assert.equal(fs.readFileSync(path.join(dir, 'existing.dxf'), 'utf8'), 'original');
}));
