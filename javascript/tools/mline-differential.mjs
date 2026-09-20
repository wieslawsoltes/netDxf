// Independent observation of the unchanged C# assembly and production JavaScript.
import '../node-entry.js';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot, javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { mlineCorpus } from './mline-corpus.mjs';
import { jsGeometry } from './foundations-wire.mjs';

const proof = {runtimeFingerprint:runtimeFingerprint(), verificationFingerprint:verificationFingerprint()};
const report = {...proof, configuration, sourceRef:baseline.ref, completed:false, fatal:null,
  stats:{scenarios:0, operations:0, failures:0}, categories:{}, failures:[]};
const oracle = new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  const corpus = mlineCorpus();
  if (corpus.length !== 789 || new Set(corpus.map(p=>p.name)).size !== corpus.length ||
      corpus.reduce((n,p)=>n+p.request.steps.length,0) !== 5123)
    throw new Error('Incomplete or duplicate multiline corpus.');
  const nativeManifest = JSON.parse(fs.readFileSync(path.join(javascriptRoot,'native-port-manifest.json')));
  report.environment = await oracle.request({op:'environment'});
  for (const probe of corpus) {
    const expected = await oracle.request(probe.request);
    const actual = jsGeometry({...probe.request, nativeManifest});
    if (!Array.isArray(expected) || expected.length !== probe.request.steps.length ||
        !Array.isArray(actual) || actual.length !== expected.length)
      throw new Error('Incomplete multiline result: '+probe.name);
    report.stats.scenarios++; report.stats.operations += expected.length;
    const category = report.categories[probe.category] ??= {scenarios:0, operations:0, failures:0};
    category.scenarios++; category.operations += expected.length;
    const differences = expected.flatMap((value,i)=>isDeepStrictEqual(value,actual[i]) ? [] :
      [{step:i, operation:probe.request.steps[i], expected:value, actual:actual[i]}]);
    if (differences.length) {
      report.stats.failures += differences.length; category.failures += differences.length;
      report.failures.push({name:probe.name, category:probe.category, differences});
      if (report.failures.length <= 3) console.error(JSON.stringify(report.failures.at(-1)));
    }
  }
  report.completed = true;
} catch (error) { report.fatal = error.stack ?? String(error); }
finally {
  try { await oracle.close(); }
  catch (error) { report.completed=false; report.fatal ??= error.stack ?? String(error); }
  if (runtimeFingerprint() !== proof.runtimeFingerprint || verificationFingerprint() !== proof.verificationFingerprint) {
    report.completed=false; report.fatal='Executable source changed during multiline qualification.';
  }
  const directory=path.join(javascriptRoot,'artifacts/mline-differential',configuration);
  fs.mkdirSync(directory,{recursive:true});
  fs.writeFileSync(path.join(directory,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats, categories:report.categories, fatal:report.fatal}));
if (!report.completed || report.fatal || report.stats.failures) process.exitCode=1;
