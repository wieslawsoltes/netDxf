// Explicit development/packaging command. Nothing is downloaded or compiled on import/install.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
const root = path.dirname(fileURLToPath(import.meta.url));
if (process.platform !== 'win32') {
  console.log('Windows host bridge is not required on ' + process.platform + '.');
} else {
  const npmRoot = process.env.npm_execpath ? path.dirname(path.dirname(process.env.npm_execpath)) : path.join(path.dirname(process.execPath), 'node_modules/npm');
  const gyp = path.join(npmRoot, 'node_modules/node-gyp/bin/node-gyp.js');
  if (!fs.existsSync(gyp)) throw new Error('Run npm run build:windows-host with the pinned Node/npm toolchain and Visual Studio C++ build tools.');
  console.log(JSON.stringify({node:process.execPath,version:process.version,nodeGyp:gyp,arch:process.arch}));
  const result = spawnSync(process.execPath, [gyp, 'rebuild', '--verbose', '--directory', path.join(root, 'windows')], {stdio: 'inherit', timeout: 180000, windowsHide: true});
  if (result.error || result.status !== 0) throw result.error || new Error('Windows host build failed: ' + result.status);
  const binary = fs.readFileSync(path.join(root, 'windows/build/Release/netdxf_windows.node'));
  const target = path.join(root, 'bin', `win32-${process.arch}`);
  fs.mkdirSync(target, { recursive: true });
  fs.writeFileSync(path.join(target, 'netdxf_windows.node'), binary);
  fs.writeFileSync(path.join(target, 'build.json'), JSON.stringify({
    node: process.version, napi: 8, platform: process.platform, arch: process.arch,
    sha256: createHash('sha256').update(binary).digest('hex'),
    sourceSha256: createHash('sha256').update(fs.readFileSync(path.join(root, 'windows/atomic_replace.cc'))).digest('hex'),
  }, null, 2) + '\n');
}
