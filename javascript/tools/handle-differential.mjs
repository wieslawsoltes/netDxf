import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { javascriptRoot, sourceRoot, baseline, configuration } from './dotnet.mjs';
import { jsHandles } from './handle-wire.mjs';
import { wireTag, doubleBits } from './wire.mjs';
import { DxfRawDocument, DxfTag } from '../index.js';
import { HandleProfiles, HandleFixture } from '../tests/netDxf.Conformance/RawHandleIndexTests.js';
import { HandleOperationFixture } from '../tests/netDxf.Conformance/RawHandleOperationsTests.js';
import { EmbeddedHandleTags } from '../tests/netDxf.Conformance/RawEmbeddedHandleTests.js';
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
export async function handleDifferential() {
  const output = path.join(javascriptRoot, 'artifacts', 'handles-differential', configuration);
  fs.mkdirSync(output, { recursive: true });
  const inventory = JSON.parse(fs.readFileSync(path.join(javascriptRoot, 'artifacts/inventory/source-inventory.json')));
  if (inventory.sourceFingerprint !== baseline.sourceFingerprint || inventory.fixtures.length !== baseline.counts.dxfFixtures)
    throw new Error('Unpinned source/fixture inventory.');
  const oracle = new OracleClient(), results = [];
  const stats = { sourceFixtures: 0, acceptedSourceFixtures: 0, rejectedSourceFixtures: 0,
    indexComparisons: 0, occurrenceComparisons: 0, diagnosticComparisons: 0, closureComparisons: 0,
    remapComparisons: 0, rejectedRemaps: 0, emittedByteComparisons: 0, crossRuntimeRemapLoads: 0, typedSemanticChecks: 0, failures: 0 };
  function fail(name, request, expected, actual) {
    const number = ++stats.failures;
    fs.writeFileSync(path.join(output, `failure-${number}.json`), JSON.stringify({ name, request, expected, actual }, null, 2) + '\n');
    console.error(`HANDLE DIFFERENTIAL FAIL ${name}; failure-${number}.json`);
    if (number >= 15) throw new Error('Aborting after 15 mismatches.');
  }
  async function check(name, request, cross = false) {
    const expected = await oracle.request({ op: 'handles', ...request }), actual = jsHandles(request);
    stats.indexComparisons++; const same = isDeepStrictEqual(expected, actual);
    results.push({ name, passed: same, accepted: expected.ok, error: expected.ok ? null : expected.error,
      inputSha256: hash(request.bytes ? Buffer.from(request.bytes, 'base64') : JSON.stringify(request.tags)) });
    if (!same) { fail(name, request, expected, actual); return expected; }
    if (expected.ok) {
      stats.occurrenceComparisons += expected.value.occurrences.length;
      stats.diagnosticComparisons += expected.value.diagnostics.length;
      if (request.roots) stats.closureComparisons++;
      if (request.mapping) {
        stats.remapComparisons++;
        const remap = expected.value.remapped;
        if (!remap.ok) stats.rejectedRemaps++;
        else for (const transport of ['text', 'binaryOutput']) {
          const net = remap.value.document[transport], js = actual.value.remapped.value.document[transport];
          if (!net.ok) continue;
          stats.emittedByteComparisons++;
          if (cross) {
            const fromNet = jsHandles({ bytes: net.value });
            const fromJs = await oracle.request({ op: 'handles', bytes: js.value });
            stats.crossRuntimeRemapLoads += 2;
            if (!isDeepStrictEqual(fromNet, fromJs)) fail(name + '/' + transport + '/cross-runtime', { bytes: js.value }, fromJs, fromNet);
          }
        }
      }
    }
    return expected;
  }
  try {
    for (const fixture of inventory.fixtures) {
      const bytes = fs.readFileSync(path.join(sourceRoot, fixture.path));
      if (hash(bytes) !== fixture.sha256 || bytes.length !== fixture.length) throw new Error('Changed shared fixture: ' + fixture.path);
      const request = { bytes: bytes.toString('base64') }, prefix = 'fixture/' + fixture.path;
      const indexed = await check(prefix, request); stats.sourceFixtures++;
      if (indexed.ok) {
        stats.acceptedSourceFixtures++;
        const ids = indexed.value.occurrences.filter(x => x.role === 0 && x.numeric !== '0');
        if (ids.length) {
          const roots = [...new Set(ids.slice(0, 3).map(x => x.record))];
          await check(prefix + '/closure', { ...request, roots, traversal: 255 });
          const seen = new Set(), unique = ids.filter(x => {
            if (seen.has(x.numeric)) return false; seen.add(x.numeric); return true;
          });
          let highest = ids.reduce((maximum, x) => BigInt(x.numeric) > maximum ? BigInt(x.numeric) : maximum, 0n);
          if (highest + BigInt(unique.length) <= 0xffffffffffffffffn) {
            // Complete simultaneous map; opaque matches or malformed identities must fail equally.
            const mapping = unique.map(x => [x.canonical, (++highest).toString(16).toUpperCase()]);
            await check(prefix + '/remap', { ...request, mapping }, true);
          }
        }
      } else stats.rejectedSourceFixtures++;
      if (stats.sourceFixtures % 50 === 0) console.log(`Compared handle graphs ${stats.sourceFixtures}/${inventory.fixtures.length}.`);
    }
    for (const v of HandleProfiles) for (const binary of [false, true]) {
      for (const make of [HandleFixture, HandleOperationFixture]) {
        const tags = make(v), request = { tags: tags.map(wireTag), binary }, prefix = make.name + '/' + v + '/' + binary;
        const info = await check(prefix, request), ids = info.value.occurrences.filter(x => x.role === 0);
        const roots = ids.map(x => x.record);
        for (const traversal of [0, 1, 2, 4, 5, 8, 16, 32, 64, 128, 255, 256])
          await check(prefix + '/closure/' + traversal, { ...request, roots, traversal });
        for (const [name, mapping] of [
          ['empty', []], ['noop', [['10','0010']]], ['fresh', [['10','1000']]],
          ['swap', [['10','20'],['20','10']]], ['alias', [['10','100'],['0010','200']]],
          ['zero', [['10','0']]], ['missing', [['99','100']]], ['overflow', [['10','FFFFFFFFFFFFFFFF']]],
          ['permutation', [['0010','20'],['20','10'],['30','A'],['a','30']]],
        ]) await check(prefix + '/remap/' + name, { ...request, mapping }, true);
        for (const maximumOccurrences of [1, 4, info.value.occurrences.length - 1, info.value.occurrences.length])
          await check(prefix + '/budget/' + maximumOccurrences, { ...request, indexOptions: { maximumOccurrences } });
      }
      for (const controls of [false, true]) {
        const tags = EmbeddedHandleTags(v, 'MTEXT', controls), request = { tags: tags.map(wireTag), binary };
        for (const mapping of [[['B','B1']], [['20','200']], [['20','0020']]])
          await check(`embedded/${v}/${binary}/${controls}/${mapping[0][0]}/${mapping[0][1]}`, { ...request, mapping }, true);
      }
      for (let variant = 0; variant < 7; variant++) {
        const tags = HandleFixture(v), at = tags.findIndex(t => t.Code === 0 && t.Value === 'LINE');
        const T = (c, x) => new DxfTag(c, x);
        if (variant === 0 || variant === 6) tags.splice(at, 0, T(0,'POINT'),T(5,variant === 0 ? 'AB' : 'E'));
        if (variant === 1) tags.splice(at + 2, 0, T(5,'222'));
        if (variant === 2) tags[at + 1] = T(5,'000');
        if (variant === 3) tags.splice(at + 2, 0, T(330,'E'));
        if (variant === 4) tags.splice(at + 2, 0, T(102,'}'));
        if (variant === 5) tags[tags.findIndex((t, i) => i >= at && t.Code === 330 && t.Value === '10')] = T(330,'C');
        await check(`diagnostics/${v}/${binary}/${variant}`, { tags: tags.map(wireTag), binary, mapping: [['10','1000']] });
      }
    }
    // .NET typed document -> native JS raw index/remap -> .NET typed reader.
    // This is not a port of the tests that construct DxfDocument in JavaScript.
    for (let version = 13; version <= 18; version++) for (const binary of [false, true]) {
      const input = await oracle.request({ op: 'typed-fixture', version, binary });
      if (!input.ok) throw new Error('Typed oracle fixture creation failed: ' + input.error);
      const info = jsHandles({ bytes: input.value });
      const mapping = info.value.occurrences.filter(x => x.role === 0).map(x => [x.canonical, (BigInt(x.numeric) + 4096n).toString(16)]);
      const changed = await check(`typed-remap/${version}/${binary}`, { bytes: input.value, mapping }, true);
      if (!changed.ok || !changed.value.remapped.ok) throw new Error('Typed remap control failed.');
      for (const key of ['text','binaryOutput']) {
        const bytes = changed.value.remapped.value.document[key].value;
        const actual = await oracle.request({ op: 'typed-read', bytes });
        const expected = { ok: true, value: { version, lines: [[1e-20,2,3,4,5,6].map(doubleBits)], circles: [[7,8,9,1.25].map(doubleBits)] } };
        stats.typedSemanticChecks++;
        if (!isDeepStrictEqual(actual, expected)) fail(`typed-remap/${version}/${binary}/${key}`, { bytes }, expected, actual);
      }
    }
  } finally {
    await oracle.close();
    const report = { runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint(),schemaVersion: 1, sourceRef: baseline.ref, sourceFingerprint: baseline.sourceFingerprint,
      configuration, stats, results, fullParityVerified: false };
    fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2) + '\n');
  }
  console.log('Handle differential: ' + JSON.stringify(stats));
  if (stats.failures) throw new Error(`${stats.failures} handle differential mismatches.`);
  return stats;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await handleDifferential();
