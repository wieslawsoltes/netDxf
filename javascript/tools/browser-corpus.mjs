import { entityBodyIOCorpus } from './entity-body-io-corpus.mjs';
import { ValidateEntityBodyObservation } from './entity-body-io-observation.mjs';
import { transportSectionsCorpus } from './transport-sections-corpus.mjs';
import { ValidateTransportObservation } from './transport-sections-observation.mjs';
import { gteCorpus } from './gte-corpus.mjs';
import { GteOracleSession } from './GteOracleSession.mjs';
import { codecReadersCorpus } from './codec-readers-corpus.mjs';
import { codecWritersCorpus } from './codec-writers-corpus.mjs';
import { ValidateCodecObservation } from './codec-observation.mjs';
import { storedDependenciesCorpus } from './stored-dependencies-corpus.mjs';
import { tableContentCorpus } from './table-content-corpus.mjs';
import { storedTableCorpus } from './stored-table-corpus.mjs';
import { tableGeometryCorpus } from './table-geometry-corpus.mjs';
import { tableStyleCorpus } from './table-style-corpus.mjs';
import { sectionManagerCorpus } from './section-manager-corpus.mjs';
import { retainedPolylineCorpus } from './retained-polyline-corpus.mjs';
import { registeredAnnotationsCorpus } from './registered-annotations-corpus.mjs';
import { documentOwnershipCorpus } from './document-ownership-corpus.mjs';
import { DocumentOracleSession } from './DocumentOracleSession.mjs';
import { drawingTimeCorpus } from './drawing-time-corpus.mjs';
import { stringEnumCorpus } from './string-enum-corpus.mjs';
import { objectReferenceCorpus } from './object-reference-corpus.mjs';
import { concreteDimensionCorpus } from './concrete-dimension-corpus.mjs';
import { ObservableDictionaryOracleSession } from './ObservableDictionaryOracleSession.mjs';
import { observableDictionaryCorpus } from './observable-dictionary-corpus.mjs';
import {leaderCorpus} from './leader-corpus.mjs';
import {toleranceCorpus} from './tolerance-corpus.mjs';
import { unitFormatCorpus } from './unit-format-corpus.mjs';
import { mleaderCorpus } from './mleader-corpus.mjs';
import { dimensionCorpus } from './dimension-corpus.mjs';
import { headerCorpus } from './header-corpus.mjs';
import { geoDataVbaCorpus } from './geodata-vba-corpus.mjs';
import { layoutViewportCorpus } from './layout-viewport-corpus.mjs';
import { ModelOracleSession } from './ModelOracleSession.mjs';
import { blockCorpus } from './block-corpus.mjs';
import { insertCorpus } from './insert-corpus.mjs';
import { mlineCorpus } from './mline-corpus.mjs';
import { outputSettingsCorpus } from './output-settings-corpus.mjs';
import { groupCorpus } from './group-corpus.mjs';
import { surfaceCorpus } from './surface-corpus.mjs';
import { expLogCorpus } from './exp-log-corpus.mjs';
import { nurbsCorpus } from './nurbs-corpus.mjs';
import { coordinateCorpus } from './coordinate-corpus.mjs';
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
      expected:{geometry:expected.value.map(value=>sha256(canonical(value)))} });
  }
  for(const scenario of collectionCorpus()){
    const input={scenarios:[scenario]},expected=await oracle.request({op:'collection',...input});
    if(!expected.ok)throw new Error('Collection oracle failed.');
    cases.push({name:'collection/'+cases.length,input,expected:{collection:sha256(canonical(expected))}});
  }
} finally { await oracle.close(); }
// A native Debug.Assert may terminate a model process. Preserve that outcome as
// non-comparable evidence, and continue with a fresh process for the next scenario.
// The original model oracle and all its prior input ordering remain unchanged.
const layoutOracle=new ModelOracleSession();
try {
  for(const probe of layoutViewportCorpus()) {
    const observed=await layoutOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:`layouts-viewports/${probe.name}`,input:probe.request,
      expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
}finally {await layoutOracle.close();}
// Detached model/text corpora use the same reflection oracle as their exact Node comparison.
const modelOracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  const read=name=>fs.readFileSync(path.join(sourceRoot,'TestDxfDocument/Support',name));
  const hatches=hatchCorpus(['acad.pat','acadiso.pat'].map(name=>({name,text:read(name).toString('utf8')})));
  const styles=styleCorpus(['acad.lin','acadiso.lin'].map(name=>({name,text:read(name).toString('utf8')})),read('ltypeshp.shx'));
  for(const [category,probes] of [['blocks',blockCorpus().concat(insertCorpus())],['mlines',mlineCorpus()],['output-settings',outputSettingsCorpus()],['groups',groupCorpus()],['surfaces',surfaceCorpus()],['hatch',hatches],['styles',styles],['entities',entityCorpus()],['coordinates',coordinateCorpus()],['database-models',databaseModelCorpus()],['geodata-vba',geoDataVbaCorpus()],['headers',headerCorpus()],['dimensions',dimensionCorpus()],['mleaders',mleaderCorpus()],['unit-formats',unitFormatCorpus()]])for(const probe of probes){
    const expected=await modelOracle.request(probe.request);
    if(!Array.isArray(expected)||expected.length!==probe.request.steps.length)throw new Error('Incomplete model oracle response.');
    cases.push({name:`${category}/${probe.name}`,input:probe.request,expected:{models:sha256(canonical(expected))}});
  }
  for(const probe of nurbsCorpus()){
    const expected=await modelOracle.request(probe.request);
    if(!Array.isArray(expected)||expected.length!==probe.request.steps.length)throw new Error('Incomplete NURBS oracle response.');
    cases.push({name:probe.name,input:probe.request,expected:{nurbs:sha256(canonical(expected))}});
  }
  const math=referenceMathCorpus().concat(expLogCorpus());
  for(let offset=0;offset<math.length;offset+=256){
    const calls=math.slice(offset,offset+256),expected=await modelOracle.request({op:'reference-math',calls});
    if(!Array.isArray(expected)||expected.length!==calls.length)throw new Error('Incomplete direct math oracle response.');
    cases.push({name:`reference-math/batch/${offset}`,names:calls.map(call=>call.id),input:{calls},
      expected:{referenceMath:expected.map(value=>sha256(canonical(value)))}});
  }
}finally {await modelOracle.close();}
// Append the recovered TOLERANCE inputs after every previous comparison; source
// assertion failures remain explicit, non-comparable evidence in both browser modes.
const toleranceOracle=new ModelOracleSession();
try {
  for(const probe of toleranceCorpus()) {
    const observed=await toleranceOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await toleranceOracle.close();}
// Keep classic LEADER inputs after every preceding corpus without changing their order.
const leaderOracle=new ModelOracleSession();
try {
  for(const probe of leaderCorpus()) {
    const observed=await leaderOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await leaderOracle.close();}
// Append observable dictionary observations; constructor rejection is a real result,
// whereas process failure is retained as unavailable native evidence, never waived.
const dictionaryOracle=new ObservableDictionaryOracleSession();
try {
  for(const probe of observableDictionaryCorpus()) {
    const observed=await dictionaryOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await dictionaryOracle.close();}
// Append all reconstructed dimension inputs; no preceding input is removed or reordered.
const concreteOracle=new ModelOracleSession();
try {
  for(const probe of concreteDimensionCorpus()) {
    const observed=await concreteOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await concreteOracle.close();}
// Reference accounting follows all prior comparison inputs without replacing any.
const referenceOracle=new ModelOracleSession();
try {
  for(const probe of objectReferenceCorpus()) {
    const observed=await referenceOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await referenceOracle.close();}
// Append drawing utilities after all preceding inputs. Native failures are not
// replaced by synthesized snapshots, nor excluded from the comparison count.
const utilityOracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  for(const probe of drawingTimeCorpus().concat(stringEnumCorpus())) {
    const expected=await utilityOracle.request(probe.request);
    if(!Array.isArray(expected)||expected.length!==probe.request.steps.length)
      throw new Error('Incomplete drawing utility oracle response: '+probe.name);
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))}});
  }
} finally {await utilityOracle.close();}
// Append every typed ownership input without changing any prior comparison.
const documentOracle=new DocumentOracleSession();
try {
  for(const probe of documentOwnershipCorpus(process.platform==='win32'?'\r\n':'\n')) {
    const observed=await documentOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await documentOracle.close();}
// Append annotation registration/lifecycle inputs after all preceding corpora.
const annotationOracle=new DocumentOracleSession();
try {
  for(const probe of registeredAnnotationsCorpus()) {
    const observed=await annotationOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await annotationOracle.close();}
// Retained metadata is constructed through explicit test-only source adapters.
// These observations do not qualify typed file loading or writing.
const retainedOracle=new DocumentOracleSession();
try {
  for(const probe of retainedPolylineCorpus()) {
    const observed=await retainedOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await retainedOracle.close();}
// SECTION_MANAGER lifecycle observations follow every previous corpus unchanged.
const managerOracle=new DocumentOracleSession();
try {
  for(const probe of sectionManagerCorpus()) {
    const observed=await managerOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await managerOracle.close();}
// Append table-style/map observations after every preceding corpus without reordering.
const tableOracle=new DocumentOracleSession();
try {
  for(const probe of tableStyleCorpus()) {
    const observed=await tableOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await tableOracle.close();}
// Append all TABLEGEOMETRY observations after the pre-existing corpora.
const tableGeometryOracle=new DocumentOracleSession();
try {
  for(const probe of tableGeometryCorpus()) {
    const observed=await tableGeometryOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await tableGeometryOracle.close();}
// Append TABLECONTENT and ACAD_TABLE after all previous comparisons. The retained
// constructor fixtures do not replace typed reader/writer admission tests.
const contentOracle=new DocumentOracleSession();
try {
  for(const probe of tableContentCorpus().concat(storedTableCorpus())) {
    const observed=await contentOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await contentOracle.close();}
// Retained dependencies and binary diagnostics follow every earlier input unchanged.
const dependencyOracle=new DocumentOracleSession();
try {
  for(const probe of storedDependenciesCorpus()) {
    const observed=await dependencyOracle.observe(probe.request);
    const expected=observed.ok?observed.value:{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await dependencyOracle.close();}
// Append codec inputs after every preceding comparison. Constructor rejections
// are observed results; missing or malformed command results fail preparation.
const codecOracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  for(const probe of codecReadersCorpus().concat(codecWritersCorpus())) {
    const expected=await codecOracle.request(probe.request);
    ValidateCodecObservation(expected,probe.request.steps.length);
    cases.push({name:probe.name,input:probe.request,expected:{models:sha256(canonical(expected))}});
  }
} finally {await codecOracle.close();}
// Keep every earlier input. Native aborts are explicit unavailable observations.
const gteOracle = new GteOracleSession();
try {
  for (const probe of gteCorpus()) {
    const observed = await gteOracle.observe(probe.request);
    const expected = observed.ok ? observed.value : {oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{gte:sha256(canonical(expected))},
      ...(!observed.ok ? {sourceOracleFailure:observed.failure} : {})});
  }
} finally { await gteOracle.close(); }
const gteManifest = JSON.parse(fs.readFileSync(path.join(javascriptRoot,'gte-port-manifest.json'),'utf8'));
// Typed section IO inputs follow every preceding comparison without removing any.
const transportOracle=new ModelOracleSession();
try {
  for(const probe of transportSectionsCorpus()) {
    const observed=await transportOracle.observe(probe.request);
    const expected=observed.ok?ValidateTransportObservation(observed.value,probe.request.steps.length):{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{transportSections:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await transportOracle.close();}
// Append all body-codec cases without replacing any previous category.
const bodyOracle=new ModelOracleSession();
try {
  for(const probe of entityBodyIOCorpus()) {
    const observed=await bodyOracle.observe(probe.request);
    const expected=observed.ok?ValidateEntityBodyObservation(observed.value,probe.request.steps.length):{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{entityBodyIO:sha256(canonical(expected))},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
} finally {await bodyOracle.close();}
if (proof.runtimeFingerprint !== runtimeFingerprint() || proof.verificationFingerprint !== verificationFingerprint()) throw new Error('Code changed while preparing browser oracle.');
const dir = path.join(javascriptRoot, 'artifacts/browser'); fs.mkdirSync(dir, { recursive: true });
fs.writeFileSync(path.join(dir, 'corpus.json'), JSON.stringify({ ...proof, configuration, sourceRef: baseline.ref,
  sourceFingerprint: baseline.sourceFingerprint, fixtures: inventory.fixtures.length, gteManifest, cases }));
console.log(`Prepared ${cases.length} browser inputs with ${cases.reduce((n,c) => n + Object.values(c.expected).reduce((count,expected)=>count+(Array.isArray(expected)?expected.length:1),0),0)} exact .NET result digests.`);
