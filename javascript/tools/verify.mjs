// Aggregate positive AND negative evidence. A failed comparison must still leave a complete ledger.
import fs from 'node:fs';
import path from 'node:path';
import { javascriptRoot, sourceRoot, configuration, baseline, computeSourceFingerprint } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint, sha256, compareCaseCoverage } from './evidence.mjs';
import { evidenceProblems } from './verification-report.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()},problems={},checks={};
const read=relative=>JSON.parse(fs.readFileSync(path.join(javascriptRoot,relative),'utf8'));
function guard(name,action){try{return action();}catch(error){(problems[name]??=[]).push(error.message);return null;}}
const assert=(condition,message)=>{if(!condition)throw new Error(message);};
guard('source',()=>assert(computeSourceFingerprint()===baseline.sourceFingerprint,'Pinned source drift.'));
const inventory=guard('inventory',()=>{
  const value=read('artifacts/inventory/source-inventory.json');
  assert(value.sourceFingerprint===baseline.sourceFingerprint,'Stale source inventory.');
  assert(value.fixtures.length===baseline.counts.dxfFixtures,'Missing shared fixtures.');
  for(const f of value.fixtures)assert(sha256(fs.readFileSync(path.join(sourceRoot,f.path)))===f.sha256,'Changed shared fixture: '+f.path);
  return value;
});
guard('generated',()=>{
  const generated=read('generated-manifest.json');
  for(const f of generated.files)assert(sha256(fs.readFileSync(path.join(javascriptRoot,f.path)))===f.sha256,'Generated file drift: '+f.path);
  const native=read('native-port-manifest.json');assert(native.sourceRef===baseline.ref,'Unpinned native lowering.');
  for(const f of native.files)assert(sha256(fs.readFileSync(path.join(sourceRoot,f.source)))===f.sourceSha256&&
    sha256(fs.readFileSync(path.join(javascriptRoot,f.target)))===f.outputSha256,'Native mirror drift: '+f.target);
});
const expected=guard('dotnet-tests',()=>{
  const data=read(`artifacts/dotnet-${configuration.toLowerCase()}/results.json`),meta=read(`artifacts/dotnet-${configuration.toLowerCase()}/metadata.json`);
  assert(meta.fullSuite&&meta.sourceFingerprint===baseline.sourceFingerprint&&data.length===baseline.counts.dotnetConformanceCases,'Complete pinned .NET result set is required.');
  return data;
});
const actual=guard('javascript-tests',()=>{
  const meta=read('artifacts/conformance/metadata.json'),data=read('artifacts/conformance/results.json');
  assert(meta.runtimeFingerprint===proof.runtimeFingerprint&&meta.verificationFingerprint===proof.verificationFingerprint,'Stale JavaScript test evidence.');
  assert(meta.fullSuite,'Filtered tests cannot satisfy verification.');return data;
});
const coverage=expected&&actual?guard('case-identities',()=>{
  const value=compareCaseCoverage(expected,actual);assert(value.unexpected.length===0,'Unexpected original test identities: '+value.unexpected.join(', '));return value;
}):null;
const specs={
  databaseModels:[`database-model-differential/${configuration}`,{equal:{'stats.scenarios':849,'stats.operations':4011}}],
  math:[`math-differential/${configuration}`,{equal:{'stats.comparisons':30904}}],
  mathIndependent:[`math-independent/${configuration}`,{equal:{'stats.comparisons':30904,subject:'HighPrecisionMath development reference'}}],
  referenceMath:[`reference-math/${configuration}`,{equal:{'stats.comparisons':61876}}],
  entities:[`entity-differential/${configuration}`,{equal:{'stats.scenarios':3012,'stats.operations':19632}}],
  browserInline:[`browser-inline/${configuration}`,{equal:{fixtures:399},minimum:{comparisons:103826}}],
  styles:[`style-differential/${configuration}`,{equal:{'stats.scenarios':541,'stats.operations':3585,'stats.textComparisons':112}}],
  hatch:[`hatch-differential/${configuration}`,{equal:{'stats.scenarios':376,'stats.operations':2559},minimum:{'stats.textComparisons':174}}],
  lifecycle:[`lifecycle-differential/${configuration}`,{equal:{'stats.comparisons':523,'stats.operations':15563,'stats.byteComparisons':256}}],
  raw:[`differential/${configuration}`,{equal:{'stats.sourceFixtures':399},minimum:{'stats.emittedByteComparisons':8263}}],
  handles:[`handles-differential/${configuration}`,{equal:{'stats.sourceFixtures':399},minimum:{'stats.indexComparisons':2355}}],
  objects:[`objects-differential/${configuration}`,{equal:{'stats.fixtures':399},minimum:{'stats.operations':4167}}],
  geometry:[`geometry-differential/${configuration}`,{minimum:{'stats.comparisons':4254}}],
  collections:[`collection-differential/${configuration}`,{minimum:{'stats.comparisons':282}}],
  foundations:[`foundations-differential/${configuration}`,{equal:{'stats.scenarios':5185,'stats.operations':49421}}],
  exactGeometry:[`geometry-exact/${configuration}`,{equal:{'stats.comparisons':2000}}],
  filesystem:[`filesystem-differential/${configuration}`,{equal:{'stats.sourceFixtures':399},minimum:{'stats.comparisons':1782}}],
  casing:[`casing-differential/${configuration}`,{minimum:{'stats.comparisons':3045}}],
  unit:['unit',{minimum:{tests:1}}],package:['package',{minimum:{files:1}}],
  browser:['browser',{equal:{fixtures:399},minimum:{comparisons:103826}}],
};
for(const [name,[location,requirements]] of Object.entries(specs)){
  const report=guard(name,()=>read(`artifacts/${location}/results.json`));
  if(!report)continue;
  const errors=evidenceProblems(report,proof,requirements);
  if(report.configuration!==undefined&&report.configuration!==configuration)errors.push('Wrong build configuration.');
  if(errors.length)problems[name]=errors;
  checks[name]={passed:errors.length===0,completed:report.completed,stats:report.stats??{
    tests:report.tests,files:report.files,comparisons:report.comparisons,browser:report.browser},problems:errors};
}
guard('benchmark',()=>{
  const report=read('artifacts/benchmark/results.json');assert(report.completed&&report.runtimeFingerprint===proof.runtimeFingerprint,'Missing/stale performance evidence.');
  checks.benchmark={passed:true,descriptiveOnly:true};
});
const library=inventory?.files.filter(f=>f.source.startsWith('netDxf/'))??[];
const absent=files=>files.filter(f=>!fs.existsSync(path.join(javascriptRoot,f.target))).map(f=>f.source);
const missingLibrary=absent(library),missingTests=absent(inventory?.files.filter(f=>f.source.startsWith('tests/netDxf.Conformance/'))??[]);
const report={schemaVersion:2,sourceRef:baseline.ref,sourceFingerprint:baseline.sourceFingerprint,configuration,...proof,
  implementedScopePassed:Object.keys(problems).length===0,fullParityVerified:false,checks,problems,
  library:{originalFiles:library.length||baseline.counts.librarySourceFiles,presentMirrors:inventory?library.length-missingLibrary.length:null,
    missing:inventory?missingLibrary:null,note:'Source-file presence is not complete API or behavioral qualification.'},
  tests:coverage??{originalCases:baseline.counts.dotnetConformanceCases,mirroredCases:actual?.length??null,missingCount:null,evidenceUnavailable:true},
  missingTestFiles:inventory?missingTests:null,
  remainingGates:['Complete typed DxfDocument, entity/table ownership, typed IO and remaining public APIs',
    'Every original test identity, method and example ported and independently qualified',
    'Exact numerical qualification across all admitted runtime configurations and platforms',
    'All filesystem platforms, metadata/locking guarantees, browser hosts and performance acceptance',
    'Exhaustive public member/signature and behavioral audit; file presence is not this audit'],
};
const out=path.join(javascriptRoot,'artifacts/verification',configuration);fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n');
console.log(`Original JavaScript cases: ${report.tests.mirroredCases}/${report.tests.originalCases}; source mirrors: ${report.library.presentMirrors}/${report.library.originalFiles}.`);
for(const [name,errors] of Object.entries(problems))console.error(name+': '+errors.join(' '));
console.log('Full parity is BLOCKED; the report retains missing work and every failed/unavailable verification category.');
if(!report.implementedScopePassed||process.argv.includes('--require-complete'))process.exitCode=1;
