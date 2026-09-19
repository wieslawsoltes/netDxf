// Actual native-host evidence; portable contract tests cannot substitute for this suite.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { javascriptRoot } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint, sha256 } from './evidence.mjs';
const proof = { runtimeFingerprint: runtimeFingerprint(), verificationFingerprint: verificationFingerprint() };
const report = { ...proof, platform: process.platform, arch: process.arch, node: process.version, completed: false, fatal: null, tests: 0, passed: 0, failed: 0, skipped: 0, todo: 0 };
const output = path.join(javascriptRoot, 'artifacts/windows-host'); fs.mkdirSync(output, { recursive: true });
try {
  if (process.platform !== 'win32') throw new Error('Windows integration tests require an actual Windows runner.');
  const bin = path.join(javascriptRoot, 'native/bin', `win32-${process.arch}`);
  report.host = JSON.parse(fs.readFileSync(path.join(bin, 'build.json')));
  if (report.host.platform !== 'win32' || report.host.arch !== process.arch || report.host.napi !== 8 ||
      report.host.sha256 !== sha256(fs.readFileSync(path.join(bin, 'netdxf_windows.node'))) ||
      report.host.sourceSha256 !== sha256(fs.readFileSync(path.join(javascriptRoot, 'native/windows/atomic_replace.cc'))))
    throw new Error('Native host build metadata/source/binary verification failed.');
  const result = spawnSync(process.execPath, ['--test', '--test-reporter=tap', 'tests/windows-host/AtomicReplacementTests.mjs'], {
    cwd: javascriptRoot, encoding: 'utf8', timeout: 60000, windowsHide: true,
  });
  const log = (result.stdout ?? '') + (result.stderr ?? ''); process.stdout.write(log);
  fs.writeFileSync(path.join(output, 'tests.log'), log);
  for (const [key, label] of Object.entries({tests:'tests',passed:'pass',failed:'fail',skipped:'skipped',todo:'todo'}))
    report[key] = Number(new RegExp('^# ' + label + ' (\\d+)$', 'm').exec(log)?.[1] ?? NaN);
  if (result.error || result.status !== 0 || report.tests !== 5 || report.passed !== 5 || report.failed !== 0 || report.skipped !== 0 || report.todo !== 0)
    throw result.error ?? new Error('Windows native-host integration tests failed or did not execute completely.');
  report.completed = true;
} catch (error) { report.fatal = error.stack; }
finally {
  if (runtimeFingerprint() !== proof.runtimeFingerprint || verificationFingerprint() !== proof.verificationFingerprint) {
    report.completed = false; report.fatal = 'Executable files changed during native-host verification.';
  }
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2) + '\n');
}
if (!report.completed) { console.error(report.fatal); process.exitCode = 1; }
