import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { javascriptRoot, sourceRoot, baseline, configuration } from './dotnet.mjs';
import { jsRaw, wireTag, doubleBits, fromBits, attempt } from './wire.mjs';
import { RawFixtureTags, RawFixtureBytes } from '../tests/netDxf.Conformance/RawDocumentTests.js';
import { ExpectedTagTypes, RawTagSample } from '../tests/netDxf.Conformance/RawTagTests.js';
import { DxfTag, DxfRawDocument, DxfTagValueType as T } from '../index.js';
import { FormatDouble } from '../runtime/NumberFormatting.js';
import { Encoding } from '../runtime/Encoding.js';
import { CodePages } from '../runtime/CodePages.generated.js';
const hash = bytes => createHash('sha256').update(bytes).digest('hex');

export async function differential() {
  const output = path.join(javascriptRoot, 'artifacts', 'differential', configuration);
  fs.mkdirSync(output, { recursive: true });
  const inventory = JSON.parse(fs.readFileSync(path.join(javascriptRoot, 'artifacts/inventory/source-inventory.json')));
  if (inventory.sourceFingerprint !== baseline.sourceFingerprint) throw new Error('Oracle source fingerprint does not match the pinned baseline.');
  if (inventory.fixtures.length !== baseline.counts.dxfFixtures) throw new Error('Fixture count changed.');
  const oracle = new OracleClient(), results = [];
  const stats = { sourceFixtures: 0, acceptedSourceFixtures: 0, rejectedSourceFixtures: 0, rawComparisons: 0,
    emittedByteComparisons: 0, exactRetainedSaves: 0, normalizedSaves: 0, crossRuntimeLoads: 0,
    doubleFormats: 0, encodingComparisons: 0, typedSemanticChecks: 0, failures: 0 };
  function fail(name, request, expected, actual) {
    stats.failures++;
    const file = `failure-${stats.failures}.json`;
    fs.writeFileSync(path.join(output, file), JSON.stringify({ name, request, expected, actual }, null, 2) + '\n');
    console.error(`DIFFERENTIAL FAIL ${name}; see ${file}`);
    if (stats.failures >= 25) throw new Error('Aborting after 25 mismatches; no passing report is produced.');
  }
  async function check(name, request, roundTrip = true) {
    const expected = await oracle.request({ op: 'raw', ...request });
    const actual = jsRaw(request); stats.rawComparisons++;
    const same = isDeepStrictEqual(expected, actual);
    results.push({ name, passed: same, accepted: expected.ok, error: expected.ok ? null : expected.error,
      inputSha256: request.bytes ? hash(Buffer.from(request.bytes, 'base64')) : hash(JSON.stringify(request.tags)) });
    if (!same) { fail(name, request, expected, actual); return expected; }
    if (!expected.ok) return expected;
    for (const transport of ['text', 'binaryOutput']) {
      if (!expected.value[transport].ok) continue;
      stats.emittedByteComparisons++;
      const retained = expected.value.original && ((transport === 'binaryOutput') === expected.value.binary);
      if (retained) stats.exactRetainedSaves++; else stats.normalizedSaves++;
      if (roundTrip) {
        // .NET -> JS, and JS -> .NET are separate executions. No tolerant/normalized comparator.
        const fromNet = jsRaw({ bytes: expected.value[transport].value });
        const fromJs = await oracle.request({ op: 'raw', bytes: actual.value[transport].value });
        stats.crossRuntimeLoads += 2;
        if (!isDeepStrictEqual(fromNet, fromJs)) fail(`${name}/${transport}/cross-runtime`, request, fromJs, fromNet);
        // Re-emission after the cross-runtime read must also agree in both transports.
        if (fromNet.ok) for (const t of ['text', 'binaryOutput']) if (fromNet.value[t].ok) stats.emittedByteComparisons++;
      }
    }
    return expected;
  }
  try {
    for (const fixture of inventory.fixtures) {
      const bytes = fs.readFileSync(path.join(sourceRoot, fixture.path));
      if (bytes.length !== fixture.length || hash(bytes) !== fixture.sha256) throw new Error('Changed shared fixture: ' + fixture.path);
      const request = { bytes: bytes.toString('base64') };
      const response = await check(`fixture/${fixture.path}/load`, request);
      stats.sourceFixtures++;
      if (response.ok) {
        stats.acceptedSourceFixtures++;
        await check(`fixture/${fixture.path}/normalize`, { ...request, normalize: true });
        const index = response.value.tags.findIndex(([code, type]) => code === 10 && type === T.Double);
        if (index >= 0) await check(`fixture/${fixture.path}/edit`, { ...request, edit: { index, tag: [10, T.Double, doubleBits(7.123456789012345)] } });
      } else stats.rejectedSourceFixtures++;
      if (stats.sourceFixtures % 50 === 0) console.log(`Compared ${stats.sourceFixtures}/${inventory.fixtures.length} shared DXF fixtures.`);
    }
    for (let version = 10; version <= 18; version++) {
      const tags = RawFixtureTags(version);
      for (const binary of [false, true]) {
        const id = `authored/${version}/${binary}`;
        await check(id, { tags: tags.map(wireTag), binary });
        const bytes = RawFixtureBytes(tags, binary, Encoding.UTF8, '\n', version < 11);
        await check(id + '/independent-fixture', { bytes: Buffer.from(bytes).toString('base64') });
        await check(id + '/byte-budget', { bytes: Buffer.from(bytes).toString('base64'), options: { maximumBytes: bytes.length - 1 } });
        await check(id + '/tag-budget', { tags: tags.map(wireTag), binary, options: { maximumTags: tags.length - 1 } });
      }
    }
    // Every supported tag code passes through authored documents and both production writers.
    for (const [code, type] of ExpectedTagTypes()) {
      const tags = RawFixtureTags(18);
      tags.splice(tags.length - 2, 0, new DxfTag(code, RawTagSample(type)));
      await check(`group-code/${code}`, { tags: tags.map(wireTag) }, false);
    }
    // A reproducible binary64 corpus, including signed zero, subnormals and decimal midpoint ties.
    const values = [0, -0, Number.MIN_VALUE, -Number.MIN_VALUE, Number.MAX_VALUE, -Number.MAX_VALUE];
    let state = 0x4e45544458464a53n;
    for (let i = 0; i < 20000; i++) {
      state ^= state << 13n; state ^= state >> 7n; state ^= state << 17n; state = BigInt.asUintN(64, state);
      const value = fromBits(state.toString(16)); if (Number.isFinite(value)) values.push(value);
    }
    for (let p = -20; p <= 20; p++) for (let n = 1; n <= 100; n++) values.push(n * 2 ** p, -n * 2 ** p);
    for (let start = 0; start < values.length; start += 1000) {
      const part = values.slice(start, start + 1000);
      const response = await oracle.request({ op: 'format', bits: part.map(doubleBits) });
      if (!response.ok) throw new Error('Production numeric oracle failed: ' + response.error);
      for (let i = 0; i < part.length; i++) {
        stats.doubleFormats++;
        const actual = FormatDouble(part[i]);
        if (actual !== response.value[i]) fail('G17/' + doubleBits(part[i]), { bits: doubleBits(part[i]) }, response.value[i], actual);
      }
    }
    // Exhaust all single bytes, then representative multi-byte and Unicode paths in every generated page.
    for (const codePage of [65001, ...Object.keys(CodePages).map(Number)]) {
      const encoding = Encoding.GetEncoding(codePage);
      for (let byte = 0; byte < 256; byte++) {
        const bytes = Uint8Array.of(byte), request = { op: 'encoding', codePage, bytes: Buffer.from(bytes).toString('base64') };
        const expected = await oracle.request(request), actual = attempt(() => encoding.GetString(bytes));
        stats.encodingComparisons++;
        if (!isDeepStrictEqual(expected, actual)) fail(`encoding/${codePage}/byte/${byte}`, request, expected, actual);
      }
      for (const text of ['ASCII\0\r\n', 'Résumé €', 'Zażółć', 'Привет', '東京', '中文', '한국어', '😀', '\u00ad', '\u200b']) {
        const request = { op: 'encoding', codePage, text }, expected = await oracle.request(request);
        const actual = attempt(() => Buffer.from(encoding.GetBytes(text)).toString('base64')); stats.encodingComparisons++;
        if (!isDeepStrictEqual(expected, actual)) fail(`encoding/${codePage}/text/${text}`, request, expected, actual);
      }
    }
    // The JavaScript raw editor must produce bytes the .NET typed reader interprets correctly.
    // These are differential checks, NOT claims that the JavaScript typed API has been ported.
    for (let version = 13; version <= 18; version++) for (const binary of [false, true]) {
      const fixture = await oracle.request({ op: 'typed-fixture', version, binary });
      if (!fixture.ok) throw new Error('Cannot create typed control: ' + fixture.error);
      const doc = DxfRawDocument.Load(Buffer.from(fixture.value, 'base64'));
      const record = doc.Sections.find(s => s.Name === 'ENTITIES').Records.find(r => r.Name === 'LINE');
      const replacement = Array.from(record.Tags, tag => tag.Code === 11 ? new DxfTag(11, 7.123456789012345) : tag);
      const edited = doc.WithRecord(record, replacement);
      for (const target of [false, true]) {
        const bytes = Buffer.from(edited.ToBytes(target)).toString('base64');
        const result = await oracle.request({ op: 'typed-read', bytes });
        const expected = { ok: true, value: { version,
          lines: [[1e-20, 2, 3, 7.123456789012345, 5, 6].map(doubleBits)],
          circles: [[7, 8, 9, 1.25].map(doubleBits)] } };
        stats.typedSemanticChecks++;
        if (!isDeepStrictEqual(expected, result)) fail(`typed-edit/${version}/${binary}/${target}`, { bytes }, expected, result);
        await check(`typed-fixture/${version}/${binary}/${target}`, { bytes });
      }
    }
  } finally {
    await oracle.close();
    fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint(),baseline: baseline.ref, sourceFingerprint: inventory.sourceFingerprint,
      configuration, comparison: 'Exact bytes and exact numeric bits; no metadata stripping, handle remapping, rounding or tolerance.',
      scope: 'Raw document/tag layer and .NET typed-reader controls, not JavaScript typed API parity.', stats, results }, null, 2) + '\n');
  }
  if (stats.failures) throw new Error(`${stats.failures} differential comparisons failed.`);
  console.log(JSON.stringify(stats, null, 2));
  return stats;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await differential();
