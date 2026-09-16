import fs from 'node:fs';
import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { baseline, sourceRoot, javascriptRoot, configuration } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint, sha256 } from './evidence.mjs';
import { ObjectFixture } from '../tests/support/ObjectFixture.js';
function canonical(value) {
  if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']';
  if (value !== null && typeof value === 'object') return '{' + Object.keys(value).sort().map(k => JSON.stringify(k) + ':' + canonical(value[k])).join(',') + '}';
  return JSON.stringify(value);
}
const proof = { runtimeFingerprint: runtimeFingerprint(), verificationFingerprint: verificationFingerprint() };
const oracle = new OracleClient(), cases = [];
const inventory = JSON.parse(fs.readFileSync(path.join(javascriptRoot, 'artifacts/inventory/source-inventory.json')));
if (inventory.sourceFingerprint !== baseline.sourceFingerprint || inventory.fixtures.length !== baseline.counts.dxfFixtures) throw new Error('Unpinned browser fixture inventory.');
const expectedFor = async input => {
  const expected = {};
  for (const op of ['raw', 'handles', 'objects']) expected[op] = sha256(canonical(await oracle.request({ op, ...input })));
  return expected;
};
try {
  for (const fixture of inventory.fixtures) {
    const bytes = fs.readFileSync(path.join(sourceRoot, fixture.path));
    if (sha256(bytes) !== fixture.sha256) throw new Error('Modified browser fixture: ' + fixture.path);
    const input = { bytes: bytes.toString('base64') };
    cases.push({ name: fixture.path, input, expected: await expectedFor(input) });
  }
  const ref = name => ({ ref: name });
  const step = (method, args = [], as) => ({ method, args, ...(as ? { as } : {}) });
  for (let version = 13; version <= 18; version++) for (const binary of [false, true]) {
    const input = { bytes: Buffer.from(ObjectFixture(version, binary).ToBytes()).toString('base64'), steps: [
      step('BeginEdit'), step('CreateDictionary', ['10', 'Unicode Żółć'], 'folder'),
      step('CreatePlaceholder', [ref('folder'), 'Leaf'], 'leaf'),
      step('CreateXRecord', [ref('folder'), 'Data', [[160,4,'-9223372036854775808'],[10,1,'8000000000000000'],[330,7,ref('leaf')]]]),
      step('CreateVariable', [ref('folder'), 'Value', '東京 Ω 🧪 \\U+0041']),
      step('CloneDictionaryTree', [ref('folder'), '10', 'Copy']), step('Commit'), step('BeginEdit'),
      step('EnsureExtensionDictionary', ['A'], 'ext'), step('CreateIdBuffer', [ref('ext'), 'Refs', ['A','B','0']]),
      step('Commit'), step('BeginEdit'), step('RemoveExtensionDictionary', ['A', true]), step('Commit')
    ] };
    const expected = await oracle.request({ op: 'objects', ...input });
    if (!expected.ok || expected.value.results.some(r => !r.ok)) throw new Error('Browser transaction oracle failed.');
    cases.push({ name: `objects-transaction/${version}/${binary}`, input, expected: { objects: sha256(canonical(expected)) } });
  }
} finally { await oracle.close(); }
if (proof.runtimeFingerprint !== runtimeFingerprint() || proof.verificationFingerprint !== verificationFingerprint()) throw new Error('Code changed while preparing browser oracle.');
const dir = path.join(javascriptRoot, 'artifacts/browser'); fs.mkdirSync(dir, { recursive: true });
fs.writeFileSync(path.join(dir, 'corpus.json'), JSON.stringify({ ...proof, configuration, sourceRef: baseline.ref,
  sourceFingerprint: baseline.sourceFingerprint, fixtures: inventory.fixtures.length, cases }));
console.log(`Prepared ${cases.length} browser inputs with ${cases.reduce((n,c) => n + Object.keys(c.expected).length,0)} exact .NET result digests.`);
