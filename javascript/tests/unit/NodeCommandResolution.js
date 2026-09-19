import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../..', import.meta.url));
test('package root cannot shadow the node executable in a Windows npm shell', () => {
  const shadow = /^(?:node|npm|npx)\.(?:exe|com|bat|cmd|js|jse|vbs|vbe|wsf|wsh)$/i;
  assert.deepEqual(fs.readdirSync(root).filter(name => shadow.test(name)), []);
  const metadata = JSON.parse(fs.readFileSync(path.join(root, 'package.json')));
  assert.equal(metadata.exports['./node'], './node-entry.js');
  assert.ok(fs.existsSync(path.join(root, metadata.exports['./node'])));
});
test('shell-launched node resolves to the actual runtime from the npm package root', () => {
  // On Windows the extensionless lookup exercises PATHEXT/current-directory precedence.
  const result = spawnSync('node', ['-p', '"JSON.stringify(process.execPath)"'], {
    cwd: root, shell: true, encoding: 'utf8', timeout: 10000, windowsHide: true,
  });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(fs.realpathSync(JSON.parse(result.stdout.trim())), fs.realpathSync(process.execPath));
});
