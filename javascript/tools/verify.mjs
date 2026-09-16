import fs from 'node:fs';
import path from 'node:path';
import { javascriptRoot, sourceRoot, configuration, baseline, computeSourceFingerprint } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint, sha256, compareCaseCoverage } from './evidence.mjs';
const read=relative=>JSON.parse(fs.readFileSync(path.join(javascriptRoot,relative),'utf8'));
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
if(computeSourceFingerprint()!==baseline.sourceFingerprint)throw new Error('Pinned source drift.');
const inventory=read('artifacts/inventory/source-inventory.json');
if(inventory.sourceFingerprint!==baseline.sourceFingerprint)throw new Error('Stale source inventory.');
if(inventory.fixtures.length!==baseline.counts.dxfFixtures)throw new Error('Missing shared fixtures.');
for(const f of inventory.fixtures)if(sha256(fs.readFileSync(path.join(sourceRoot,f.path)))!==f.sha256)throw new Error('Changed fixture '+f.path);
const generated=read('generated-manifest.json');
for(const file of generated.files)if(sha256(fs.readFileSync(path.join(javascriptRoot,file.path)))!==file.sha256)throw new Error('Generated file drift: '+file.path);
const expected=read(`artifacts/dotnet-${configuration.toLowerCase()}/results.json`),metadata=read(`artifacts/dotnet-${configuration.toLowerCase()}/metadata.json`);
if(!metadata.fullSuite || metadata.sourceFingerprint!==baseline.sourceFingerprint || expected.length!==baseline.counts.dotnetConformanceCases)
  throw new Error('A fresh complete original .NET suite is required.');
const actual=read('artifacts/conformance/results.json'),jsMetadata=read('artifacts/conformance/metadata.json');
const checkProof=report=>{
  if(report.runtimeFingerprint!==proof.runtimeFingerprint || report.verificationFingerprint!==proof.verificationFingerprint)
    throw new Error('Evidence is stale for the current runtime or verifier.');
};
checkProof(jsMetadata);if(!jsMetadata.fullSuite)throw new Error('Filtered JS tests cannot satisfy verification.');
const coverage=compareCaseCoverage(expected,actual);if(coverage.unexpected.length)throw new Error('Unmapped test identities.');
const checks={};
for(const [name,file] of Object.entries({raw:`differential/${configuration}`,handles:`handles-differential/${configuration}`,
  objects:`objects-differential/${configuration}`,casing:`casing-differential/${configuration}`,unit:'unit',package:'package',browser:'browser'})){
  const report=read(`artifacts/${file}/results.json`);checkProof(report);
  if(!report.completed || report.fatal || report.stats?.failures || report.failures?.length || report.failed || report.skipped || report.todo)throw new Error('Failed or incomplete evidence: '+name);
  if(['raw','handles'].includes(name) && report.stats.sourceFixtures!==baseline.counts.dxfFixtures)throw new Error('Incomplete fixture corpus: '+name);
  if(name==='objects' && report.stats.fixtures!==baseline.counts.dxfFixtures)throw new Error('Incomplete OBJECTS corpus.');
  if(name==='browser' && (report.fixtures!==baseline.counts.dxfFixtures || report.comparisons<1209))throw new Error('Incomplete browser corpus.');
  if(name==='unit' && !(report.tests>0))throw new Error('Empty unit suite.');
  checks[name]=report.stats||{tests:report.tests,files:report.files,comparisons:report.comparisons,browser:report.browser};
}
const benchmark=read('artifacts/benchmark/results.json');
if(!benchmark.completed||benchmark.runtimeFingerprint!==proof.runtimeFingerprint)throw new Error('Missing/stale performance evidence.');
const library=inventory.files.filter(f=>f.source.startsWith('netDxf/'));
const missingLibrary=library.filter(f=>!fs.existsSync(path.join(javascriptRoot,f.target))).map(f=>f.source);
const missingTests=inventory.files.filter(f=>f.source.startsWith('tests/netDxf.Conformance/')&&!fs.existsSync(path.join(javascriptRoot,f.target))).map(f=>f.source);
const report={schemaVersion:1,sourceRef:baseline.ref,sourceFingerprint:baseline.sourceFingerprint,configuration,...proof,
  implementedScopePassed:true,fullParityVerified:false,checks,
  library:{originalFiles:library.length,presentMirrors:library.length-missingLibrary.length,missing:missingLibrary,
    note:'File presence is not complete API or semantic qualification.'},
  tests:coverage,missingTestFiles:missingTests,
  remainingGates:['Complete typed DxfDocument/entity/table/math API and native typed serialization',
    'All original test methods and sample scenarios ported without omissions',
    'Exhaustive public member/signature migration audit','Filesystem/atomic-save and stream adapter qualification',
    'Full browser and performance qualification of every completed API'],
};
const out=path.join(javascriptRoot,'artifacts/verification',configuration);fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n');
console.log(`Verified implemented scope: ${coverage.mirroredCases}/${coverage.originalCases} original test identities; ${report.library.presentMirrors}/${library.length} library file mirrors.`);
console.log(`Full parity remains BLOCKED: ${coverage.missingCount} original test identities and ${missingLibrary.length} source-file mirrors are missing.`);
if(process.argv.includes('--require-complete'))throw new Error('Full-port release gate is not satisfied. See the explicit missing-work report.');
