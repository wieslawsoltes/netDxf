import { runtimeFingerprint, verificationFingerprint } from '../../tools/evidence.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as RawObjectBoundaryTests from './RawObjectBoundaryTests.js';
import * as RawRecordTests from './RawRecordTests.js';
import * as ObservableCollectionInsertTests from './ObservableCollectionInsertTests.js';
import * as ClassDefinitionTests from './ClassDefinitionTests.js';
import * as Harness from './TestHarness.js';
import * as RawTagTests from './RawTagTests.js';
import * as RawDocumentTests from './RawDocumentTests.js';
import * as RawHandleIndexTests from './RawHandleIndexTests.js';
import * as RawHandleOperationsTests from './RawHandleOperationsTests.js';
import * as RawEmbeddedHandleTests from './RawEmbeddedHandleTests.js';

/** C# partial Program is represented by the same named methods on a single class. */
export class Program {}
const modules = { ClassDefinitionTests, ObservableCollectionInsertTests, RawRecordTests, RawObjectBoundaryTests, RawTagTests, RawDocumentTests, RawHandleIndexTests, RawHandleOperationsTests, RawEmbeddedHandleTests };
Object.assign(Program, Harness, ...Object.values(modules));
export async function Main() {
  const proof = { runtimeFingerprint: runtimeFingerprint(), verificationFingerprint: verificationFingerprint() };
  Harness.cases.length = 0;
  for (const [filename, module] of Object.entries(modules))
    Harness.WithSource(`tests/netDxf.Conformance/${filename}.cs`, () => module[`Register${filename}`]());
  const filter = process.env.DXF_TEST_FILTER || '';
  const selected = Harness.cases.filter(test => test.name.startsWith(filter));
  if (!selected.length) throw new Error('No JavaScript conformance tests matched DXF_TEST_FILTER=' + filter);
  const results = [];
  for (const test of selected) {
    try {
      await test.action(); results.push({ name: test.name, source: test.source, passed: true, error: null });
      if (process.env.DXF_TEST_VERBOSE) console.log('PASS ' + test.name);
    } catch (error) {
      results.push({ name: test.name, source: test.source, passed: false, error: error.stack });
      console.error(`FAIL ${test.name}: ${error.stack}`);
    }
  }
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
  const output = path.resolve(process.env.DXF_JS_TEST_ARTIFACTS || path.join(root, 'artifacts', 'conformance'));
  fs.mkdirSync(output, { recursive: true });
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(results, null, 2) + '\n');
  if (proof.runtimeFingerprint !== runtimeFingerprint() || proof.verificationFingerprint !== verificationFingerprint()) throw new Error('Code changed during conformance tests.');
  fs.writeFileSync(path.join(output, 'metadata.json'), JSON.stringify({ ...proof, fullSuite: !filter, filter: filter || null }, null, 2) + '\n');
  const failed = results.filter(result => !result.passed).length;
  console.log(`JavaScript conformance: ${results.length - failed} passed; ${failed} failed. This is a partial port, not the full .NET suite.`);
  return failed ? 1 : 0;
}
Program.Main = Main;
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = await Main();
