import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as Harness from './TestHarness.js';
import * as RawTagTests from './RawTagTests.js';
import * as RawDocumentTests from './RawDocumentTests.js';

/** C# partial Program is represented by the same named methods on a single class. */
export class Program {}
const modules = { RawTagTests, RawDocumentTests };
Object.assign(Program, Harness, ...Object.values(modules));
export async function Main() {
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
  const failed = results.filter(result => !result.passed).length;
  console.log(`JavaScript conformance: ${results.length - failed} passed; ${failed} failed. This is a partial port, not the full .NET suite.`);
  return failed ? 1 : 0;
}
Program.Main = Main;
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = await Main();
