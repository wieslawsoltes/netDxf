import test from 'node:test';
import assert from 'node:assert/strict';
import { OracleClient } from '../../tools/OracleClient.mjs';
const client = (source, timeout = 5000) => new OracleClient({ executable: process.execPath, args: ['-e', source], timeout });

test('oracle shutdown resolves after an already observed process failure', async () => {
  const oracle = client('process.stdin.once("data", () => { console.error("injected failure"); process.exit(17); });');
  await assert.rejects(oracle.request({}), /17.*injected failure/s);
  await assert.rejects(oracle.close(), /17.*injected failure/s);
});
test('oracle malformed response fails the request and shutdown', async () => {
  const oracle = client('process.stdin.once("data", () => { console.log("not json"); });');
  await assert.rejects(oracle.request({}), SyntaxError);
  await assert.rejects(oracle.close(), SyntaxError);
});
test('oracle timeout rejects rather than producing an incomplete passing report', async () => {
  const oracle = client('setInterval(() => {}, 1000);', 150);
  await assert.rejects(oracle.request({}), /timed out/);
  await assert.rejects(oracle.close(), /timed out/);
});
test('oracle ordered responses and repeated close', async () => {
  const oracle = client('require("readline").createInterface({input:process.stdin}).on("line", line => console.log(line));');
  assert.deepEqual(await Promise.all([oracle.request({ n: 1 }), oracle.request({ n: 2 })]), [{ n: 1 }, { n: 2 }]);
  await oracle.close(); await oracle.close();
  await assert.rejects(oracle.request({}), /closed/);
});
test('closing with pending work rejects every request', async () => {
  const oracle = client('setInterval(() => {}, 1000);');
  const pending = assert.rejects(oracle.request({}), /pending requests/);
  await assert.rejects(oracle.close(), /pending requests/); await pending;
});
test('missing oracle executable cannot hang shutdown', async () => {
  const oracle = new OracleClient({ executable: '/missing-netdxf-oracle', args: [] });
  await assert.rejects(oracle.request({}), /ENOENT/);
  await assert.rejects(oracle.close(), /ENOENT/);
});
