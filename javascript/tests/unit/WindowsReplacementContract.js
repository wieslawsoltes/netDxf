import test from 'node:test';
import assert from 'node:assert/strict';
import { CreateWindowsFileReplacer } from '../../runtime/WindowsReplacementContract.js';
import * as E from '../../runtime/Errors.js';

test('Windows bridge: failed loads preserve the cause and remain retryable', () => {
  const cause = new Error('binary unavailable'); let attempts = 0, calls = 0;
  const replace = CreateWindowsFileReplacer(() => {
    if (++attempts <= 2) throw cause;
    return { replaceFile() { calls++; return 0; } };
  });
  for (let i = 0; i < 2; i++) assert.throws(() => replace('a', 'b'), e => e instanceof E.NotSupportedException && e.cause === cause);
  replace('a', 'b'); replace('a', 'b');
  assert.equal(attempts, 3); assert.equal(calls, 2);
});
test('Windows bridge: invalid exports cannot poison the callable cache', () => {
  for (const bad of [null, {}, { replaceFile: 0 }]) {
    let attempts = 0;
    const replace = CreateWindowsFileReplacer(() => ++attempts < 3 ? bad : { replaceFile: () => 0 });
    for (let i = 0; i < 2; i++) assert.throws(() => replace('a', 'b'), E.NotSupportedException);
    replace('a', 'b'); assert.equal(attempts, 3);
  }
});
test('Windows bridge: validated callable is independent of later export mutation', () => {
  const calls = [], host = { replaceFile(...args) { calls.push(args); return 0; } };
  const replace = CreateWindowsFileReplacer(() => host);
  replace('staged-1', 'target-1'); host.replaceFile = null;
  replace('staged-2', 'target-2');
  assert.deepEqual(calls, [['staged-1', 'target-1'], ['staged-2', 'target-2']]);
});
test('Windows bridge: exact Win32 errors remain failures and keep their code', () => {
  for (const [code, Type] of [[2,E.FileNotFoundException],[3,E.DirectoryNotFoundException],
    [5,E.UnauthorizedAccessException],[32,E.IOException],[1175,E.IOException],[0xffffffff,E.IOException]]) {
    let calls = 0;
    const replace = CreateWindowsFileReplacer(() => ({ replaceFile() { calls++; return code; } }));
    assert.throws(() => replace('staged', 'target'), e => e instanceof Type && e.Win32ErrorCode === code);
    assert.equal(calls, 1, 'No retry or copy/delete fallback is permitted.');
  }
});
test('Windows bridge: malformed native statuses never become successful publication', () => {
  for (const code of [undefined, null, false, '0', -1, 0.5, 0x100000000, NaN, Infinity]) {
    const replace = CreateWindowsFileReplacer(() => ({ replaceFile: () => code }));
    assert.throws(() => replace('staged', 'target'), E.IOException);
  }
});
test('Windows bridge: exceptions during native invocation propagate unchanged', () => {
  const cause = new Error('native invocation failed'); let calls = 0;
  const replace = CreateWindowsFileReplacer(() => ({ replaceFile() { calls++; throw cause; } }));
  assert.throws(() => replace('a', 'b'), error => error === cause); assert.equal(calls, 1);
});
