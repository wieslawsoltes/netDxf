import { entityBodyIOCall } from './entity-body-io-wire.mjs';
import { ValidateEntityBodyObservation } from './entity-body-io-observation.mjs';
import { transportSectionsCall } from './transport-sections-wire.mjs';
import { ValidateTransportObservation } from './transport-sections-observation.mjs';
import { gteCall } from './gte-wire.mjs';
import { validateGteObservation } from './gte-observation.mjs';
import { jsNurbs } from './nurbs-wire.mjs';
// Browser-native execution. Golden result digests are supplied only by the pinned C# oracle.
import { mathCall } from './math-wire.mjs';
import { referenceMathCall } from './reference-math-wire.mjs';
import { jsRaw } from './wire.mjs';
import { jsHandles } from './handle-wire.mjs';
import { jsObjects } from './object-wire.mjs';
import { createGeometryCaller } from './geometry-wire.mjs';
import { collectionCall } from './collection-wire.mjs';
import { jsGeometry } from './foundations-wire.mjs';
import { lifecycleCall } from './lifecycle-wire.mjs';
export function canonical(value) {
  if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']';
  if (value !== null && typeof value === 'object') return '{' + Object.keys(value).sort().map(k => JSON.stringify(k) + ':' + canonical(value[k])).join(',') + '}';
  return JSON.stringify(value);
}
async function webHash(text) {
  return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))), b => b.toString(16).padStart(2,'0')).join('');
}
/** hashCanonical is solely a SHA-256 transport adapter for non-secure offline test pages. */
export async function runBrowserCorpus(corpus, native, { hashCanonical = webHash, onProgress = () => {} } = {}) {
  const report = {completed:false,failures:[],sourceOracleFailures:[],comparisons:0,categories:{}};
  try {
    if (!Array.isArray(corpus.cases) || !corpus.cases.length) throw new Error('Missing browser corpus.');
    for (const key of ['runtimeFingerprint','verificationFingerprint','sourceRef','sourceFingerprint','configuration','fixtures']) report[key] = corpus[key];
    const geometry = createGeometryCaller(native);
    const methods = {entityBodyIO:input=>ValidateEntityBodyObservation(entityBodyIOCall(input),input.steps.length),transportSections:input=>ValidateTransportObservation(transportSectionsCall(input),input.steps.length),gte:input=>validateGteObservation(input,gteCall(input,corpus.gteManifest)),nurbs:jsNurbs,math:input=>({ok:true,value:mathCall(input.requests)}),referenceMath:input=>({ok:true,value:referenceMathCall(input.calls)}),raw:jsRaw,handles:jsHandles,objects:jsObjects,models:input=>jsGeometry({...input,nativeManifest:native}),
      geometry:input=>({ok:true,value:geometry(input)}),lifecycle:input=>({ok:true,value:lifecycleCall(input)}),collection:input=>({ok:true,value:collectionCall(input)})};
    const compare = async (name,op,expected,result) => {
      if (typeof expected !== 'string' || !/^[a-f0-9]{64}$/.test(expected)) throw new Error('Invalid expected digest: '+name);
      const actual = await hashCanonical(canonical(result));
      if (typeof actual !== 'string' || !/^[a-f0-9]{64}$/.test(actual)) throw new Error('Invalid computed digest: '+name);
      report.comparisons++; report.categories[op] = (report.categories[op] ?? 0) + 1;
      if (actual !== expected) report.failures.push({name,op,expected,actual,result});
    };
    for (const test of corpus.cases) {
      // Even a coincidentally equal digest cannot qualify a missing native observation.
      if (Object.hasOwn(test,'sourceOracleFailure')) report.sourceOracleFailures.push({name:test.name,detail:test.sourceOracleFailure});
      const entries=Object.entries(test.expected);
      if (!entries.length) throw new Error('Browser case has no comparisons: '+test.name);
      for (const [op,expected] of entries) {
        if (!Object.hasOwn(methods,op)) throw new Error('Unknown browser operation: '+op);
        const result=methods[op](test.input);
        if (Array.isArray(expected)) {
          if (!result.ok || !Array.isArray(result.value) || result.value.length !== expected.length || !expected.length)
            throw new Error('Incomplete browser batch: '+test.name);
          for(let i=0;i<expected.length;i++) await compare(test.names[i],op,expected[i],result.value[i]);
        } else await compare(test.name,op,expected,result);
      }
      onProgress(report);
    }
    report.completed=report.failures.length===0&&report.sourceOracleFailures.length===0;
  } catch(error) { report.fatal=String(error.stack||error); }
  return report;
}
