import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { javascriptRoot, sourceRoot, walk, computeSourceFingerprint } from './dotnet.mjs';
export const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const relative = (root, file) => path.relative(root, file).split(path.sep).join('/');
/** Whole pinned input tree: includes support assets as well as C# and DXF. */
export const sourceFingerprint = computeSourceFingerprint;
/** Bind executable evidence to runtime bytes; documentation edits do not invalidate results. */
export function runtimeFingerprint(root = javascriptRoot) {
  const files = ['netDxf', 'runtime'].flatMap(dir => walk(path.join(root, dir)));
  files.push(...['index.js','Enums.generated.js','geometry.js'].map(file=>path.join(root,file)));
  return sha256(files.sort().map(file => relative(root, file) + '\0' + sha256(fs.readFileSync(file)) + '\n').join(''));
}
/** Tests and oracle inputs must be re-executed after an implementation of the verifier changes. */
export function verificationFingerprint(root = javascriptRoot) {
  const files = ['tests', 'tools'].flatMap(dir => walk(path.join(root, dir)))
    .filter(file => /\.(?:cs|js|mjs|py|html|txt|json)$/.test(file));
  files.push(...['package.json','baseline.json','generated-manifest.json','native-port-manifest.json'].map(file => path.join(root,file)));
  return sha256(files.sort().map(file => relative(root, file) + '\0' + sha256(fs.readFileSync(file)) + '\n').join(''));
}
export function compareCaseCoverage(expected, actual) {
  if (!Array.isArray(expected) || !Array.isArray(actual) || expected.length === 0 || actual.length === 0)
    throw new Error('Both nonempty .NET and JavaScript result arrays are required.');
  const index = (items, name) => {
    const map = new Map();
    for (const item of items) {
      if (typeof item.name !== 'string' || item.passed !== true || item.skipped || item.todo)
        throw new Error(`${name} has an invalid, failing, skipped or TODO result: ${item.name}`);
      if (map.has(item.name)) throw new Error(`${name} duplicate test identity: ${item.name}`);
      map.set(item.name, item);
    }
    return map;
  };
  const net = index(expected, '.NET'), js = index(actual, 'JavaScript');
  const unexpected = [...js.keys()].filter(name => !net.has(name)).sort();
  const missing = [...net.keys()].filter(name => !js.has(name)).sort();
  return { originalCases: net.size, mirroredCases: js.size, missingCount: missing.length, unexpected, missing,
    complete: missing.length === 0 && unexpected.length === 0 };
}
