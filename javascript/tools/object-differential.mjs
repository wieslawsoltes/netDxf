import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { fileURLToPath } from 'node:url';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, sourceRoot, baseline, configuration } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint, sha256 } from './evidence.mjs';
import { jsObjects } from './object-wire.mjs';
import { wireTag } from './wire.mjs';
import { DxfRawDocument, DxfRawHandleIndex, DxfRawHandleRole as R, DxfTag } from '../index.js';
import { ObjectFixture } from '../tests/support/ObjectFixture.js';

const ref = name => ({ ref: name });
const step = (method, args = [], as) => ({ method, args, ...(as ? { as } : {}) });
export async function objectDifferential() {
  const output = path.join(javascriptRoot, 'artifacts', 'objects-differential', configuration);
  fs.mkdirSync(output, { recursive: true });
  const inventory = JSON.parse(fs.readFileSync(path.join(javascriptRoot, 'artifacts/inventory/source-inventory.json')));
  if (inventory.sourceFingerprint !== baseline.sourceFingerprint || inventory.fixtures.length !== baseline.counts.dxfFixtures)
    throw new Error('Unpinned object test fixture inventory.');
  const oracle = new OracleClient(), results = [];
  const proof = { runtimeFingerprint: runtimeFingerprint(), verificationFingerprint: verificationFingerprint() };
  const stats = { fixtures: 0, comparisons: 0, operations: 0, rejectedOperations: 0, byteComparisons: 0, roundTrips: 0, failures: 0 };
  let completed = false, fatal = null;
  async function check(name, input, cycle = false) {
    const expected = await oracle.request({ op: 'objects', ...input }), actual = jsObjects(input);
    stats.comparisons++;
    const same = isDeepStrictEqual(expected, actual);
    results.push({ name, passed: same, accepted: expected.ok });
    if (!same) {
      const number = ++stats.failures;
      fs.writeFileSync(path.join(output, `failure-${number}.json`), JSON.stringify({ name, input, expected, actual }, null, 2));
      console.error('OBJECT DIFFERENTIAL FAIL ' + name);
      if (number >= 10) throw new Error('Aborting after ten object mismatches.');
      return;
    }
    if (!expected.ok) return;
    stats.operations += expected.value.results.length;
    stats.rejectedOperations += expected.value.results.filter(x => !x.ok).length;
    for (const key of ['text', 'binaryOutput']) {
      const net = expected.value.document[key], js = actual.value.document[key];
      if (!net.ok) continue;
      stats.byteComparisons++;
      if (cycle) {
        const netRead = await oracle.request({ op: 'objects', bytes: js.value }), jsRead = jsObjects({ bytes: net.value });
        stats.roundTrips += 2;
        if (!isDeepStrictEqual(netRead, jsRead)) throw new Error('Object cross-runtime round trip mismatch: ' + name);
      }
    }
  }
  try {
    for (const fixture of inventory.fixtures) {
      const bytes = fs.readFileSync(path.join(sourceRoot, fixture.path));
      if (sha256(bytes) !== fixture.sha256) throw new Error('Modified shared fixture: ' + fixture.path);
      await check('fixture/' + fixture.path, { bytes: bytes.toString('base64') }); stats.fixtures++;
    }
    for (let version = 13; version <= 18; version++) for (const binary of [false, true]) {
      // Real typed .NET writer output, plus a separately authored native raw input.
      const produced = await oracle.request({ op: 'typed-fixture', version, binary });
      if (!produced.ok) throw new Error('Typed source fixture failed.');
      for (const [sourceName, bytes] of [['typed-dotnet', produced.value], ['raw-native', Buffer.from(ObjectFixture(version,binary).ToBytes()).toString('base64')]]) {
        const raw = DxfRawDocument.Load(Buffer.from(bytes, 'base64')), index = DxfRawHandleIndex.Create(raw);
        const definition = type => index.Occurrences.find(x => x.Role === R.Identity && x.Record.Name === type);
        const line = definition('LINE'), circle = definition('CIRCLE').Handle;
        const block = index.GetOccurrences(line.Record).find(x => x.Role === R.Owner).CanonicalHandle;
        for (let cloning = 0; cloning <= 5; cloning++) {
          const payload = [wireTag(new DxfTag(160,-9223372036854775808n)),wireTag(new DxfTag(10,-0)),
            wireTag(new DxfTag(310,Uint8Array.of(0,255))),[330,7,ref('leaf')],[320,7,'DEAD']];
          const steps = [step('BeginEdit'), step('EnsureRootDictionary',[],'root'), step('CreateDictionary',[ref('root'),'Custom'],'custom'),
            step('CreatePlaceholder',[ref('custom'),'Leaf'],'leaf'), step('CreateXRecord',[ref('custom'),'Data',payload,cloning],'data'),
            step('CreateVariable',[ref('custom'),'Variable','Żółć Ω 🧪 \\U+0041'],'variable'),
            step('CreateIdBuffer',[ref('custom'),'Buffer',[line.Handle,'0',circle,line.Handle]],'buffer'),
            step('CreateDictionary',[ref('custom'),'Defaults',true,true],'defaults'),
            step('SetDictionaryFlags',[ref('custom'),true,cloning]),step('SetDictionaryEntry',[ref('custom'),'Alias',ref('data'),true]),
            step('RenameEntry',[ref('custom'),'variable','VARIABLE']), step('CloneDictionaryTree',[ref('custom'),ref('root'),'Copy'],'copy')];
          if (version >= 14) steps.push(step('SetDrawOrder',[block,[[line.Handle,'0'],[circle,'FFFFFFFFFFFFFFFE']]],'sort'));
          steps.push(step('Commit'),step('BeginEdit'),step('RemoveEntry',[ref('custom'),'Alias',true]),step('Get',[ref('custom')]),
            step('RemoveEntry',[ref('custom'),'Alias']),step('SetXRecord',[ref('data'),[[1,0,'changed']]]),
            step('SetVariable',[ref('variable'),null,null]),step('SetIdBuffer',[ref('buffer'),[]]),step('Commit'),
            step('BeginEdit'),step('EnsureExtensionDictionary',[line.Handle],'ext'),step('CreateXRecord',[ref('ext'),'Extension data',[]]),
            step('Commit'),step('BeginEdit'),step('RemoveExtensionDictionary',[line.Handle,true]),step('Commit'));
          await check(`workflow/${sourceName}/${version}/${binary}/${cloning}`, { bytes, steps }, true);
        }
      }
    }
    for (const maximumChanges of [1,2,3]) {
      const bytes = Buffer.from(ObjectFixture().ToBytes()).toString('base64');
      await check('rollback/change-budget/'+maximumChanges,{bytes,storeOptions:{maximumChanges},steps:[step('BeginEdit'),
        step('CreateDictionary',['10','Defaults',true,true]),step('Get',['10']),step('CreatePlaceholder',['10','After']),step('Commit')]});
    }
    completed = true;
  } catch (error) { fatal = error.stack; throw error; }
  finally {
    try { await oracle.close(); } catch (error) { fatal ??= error.stack; completed = false; }
    if (proof.runtimeFingerprint !== runtimeFingerprint() || proof.verificationFingerprint !== verificationFingerprint()) { fatal = 'Code changed during verification.'; completed = false; }
    fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({ sourceRef:baseline.ref, sourceFingerprint:baseline.sourceFingerprint,
      configuration, ...proof, completed, fatal, stats, results,
      scope:'Native raw OBJECTS schemas and transactions. JavaScript typed DxfDocument is not implemented.', fullParityVerified:false },null,2)+'\n');
  }
  if (!completed || fatal || stats.failures) throw new Error('Object differential verification failed.');
  console.log('Object differential: '+JSON.stringify(stats)); return stats;
}
if(process.argv[1] && path.resolve(process.argv[1])===fileURLToPath(import.meta.url)) await objectDifferential();
