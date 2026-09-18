import { databaseModelCorpus } from './database-model-corpus.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot } from './dotnet.mjs';
import { mathCorpus } from './math-corpus.mjs';
import { referenceMathCorpus } from './reference-math-corpus.mjs';
import { entityCorpus } from './entity-corpus.mjs';
import { styleCorpus } from './style-corpus.mjs';
import { hatchCorpus } from './hatch-corpus.mjs';
import { baseline, sourceRoot, javascriptRoot, configuration } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint, sha256 } from './evidence.mjs';
import { geometryCorpus } from './geometry-differential.mjs';
import { collectionCorpus } from './collection-differential.mjs';
import { lifecycleCorpus } from './lifecycle-differential.mjs';
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
  const mathRequests = mathCorpus();
  for (let offset = 0; offset < mathRequests.length; offset += 256) {
    const requests = mathRequests.slice(offset, offset + 256), expected = await oracle.request({op:'math', requests});
    if (!expected.ok || !Array.isArray(expected.value) || expected.value.length !== requests.length) throw new Error('Incomplete expanded math oracle response.');
    cases.push({name:`math/batch/${offset}`, names:requests.map(request=>request.id), input:{requests},
      expected:{math:expected.value.map(value=>sha256(canonical(value)))}});
  }
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
  for (const scenario of lifecycleCorpus()) {
    const input = { scenarios: [scenario] }, expected = await oracle.request({ op: 'lifecycle', ...input });
    if (!expected.ok) throw new Error('Typed lifecycle oracle failed.');
    cases.push({ name: 'lifecycle/' + scenario.name, input, expected: { lifecycle: sha256(canonical(expected)) } });
  }
  const native=JSON.parse(fs.readFileSync(path.join(javascriptRoot,'native-port-manifest.json')));
  const requests=geometryCorpus(native);
  for(let at=0;at<requests.length;at+=256){
    const batch=requests.slice(at,at+256), expected=await oracle.request({op:'geometry',requests:batch});
    if(!expected.ok)throw new Error('Geometry oracle failed.');
    cases.push({name:`geometry/batch/${at}`, names:batch.map(request=>request.id), input:{requests:batch},
      expected:{geometry:expected.value.map(value=>sha256(canonical(value)))}});
  }
  for(const scenario of collectionCorpus()){
    const input={scenarios:[scenario]},expected=await oracle.request({op:'collection',...input});
    if(!expected.ok)throw new Error('Collection oracle failed.');
    cases.push({name:'collection/'+cases.length,input,expected:{collection:sha256(canonical(expected))}});
  }
} finally { await oracle.close(); }
// Detached model/text corpora use the same reflection oracle as their exact Node comparison.
const modelOracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  const read=name=>fs.readFileSync(path.join(sourceRoot,'TestDxfDocument/Support',name));
  const hatches=hatchCorpus(['acad.pat','acadiso.pat'].map(name=>({name,text:read(name).toString('utf8')})));
  const styles=styleCorpus(['acad.lin','acadiso.lin'].map(name=>({name,text:read(name).toString('utf8')})),read('ltypeshp.shx'));
  for(const [category,probes] of [['hatch',hatches],['styles',styles],['entities',entityCorpus()],['database-models',databaseModelCorpus()]])for(const probe of probes){
    const expected=await modelOracle.request(probe.request);
    if(!Array.isArray(expected)||expected.length!==probe.request.steps.length)throw new Error('Incomplete model oracle response.');
    cases.push({name:`${category}/${probe.name}`,input:probe.request,expected:{models:sha256(canonical(expected))}});
  }
  const math=referenceMathCorpus();
  for(let offset=0;offset<math.length;offset+=256){
    const calls=math.slice(offset,offset+256),expected=await modelOracle.request({op:'reference-math',calls});
    if(!Array.isArray(expected)||expected.length!==calls.length)throw new Error('Incomplete direct math oracle response.');
    cases.push({name:`reference-math/batch/${offset}`,names:calls.map(call=>call.id),input:{calls},
      expected:{referenceMath:expected.map(value=>sha256(canonical(value)))}});
  }
}finally {await modelOracle.close();}
if (proof.runtimeFingerprint !== runtimeFingerprint() || proof.verificationFingerprint !== verificationFingerprint()) throw new Error('Code changed while preparing browser oracle.');
const dir = path.join(javascriptRoot, 'artifacts/browser'); fs.mkdirSync(dir, { recursive: true });
fs.writeFileSync(path.join(dir, 'corpus.json'), JSON.stringify({ ...proof, configuration, sourceRef: baseline.ref,
  sourceFingerprint: baseline.sourceFingerprint, fixtures: inventory.fixtures.length, cases }));
console.log(`Prepared ${cases.length} browser inputs with ${cases.reduce((n,c) => n + Object.values(c.expected).reduce((count,expected)=>count+(Array.isArray(expected)?expected.length:1),0),0)} exact .NET result digests.`);
