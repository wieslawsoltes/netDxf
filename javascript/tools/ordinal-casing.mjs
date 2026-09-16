// Generate native case-fold data from the pinned .NET oracle; no .NET dependency at runtime.
import fs from 'node:fs';
import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, baseline } from './dotnet.mjs';
const oracle = new OracleClient();
try {
  const result = await oracle.request({ op: 'ordinal-map' });
  if (!result.ok) throw new Error('Ordinal case mapping failed: ' + result.error);
  const pairs = result.value, ranges = [];
  for (let i = 0; i < pairs.length;) {
    const [start, to] = pairs[i], delta = to - start;
    let step = 1, end = start, j = i + 1;
    if (j < pairs.length && pairs[j][1] - pairs[j][0] === delta) step = pairs[j][0] - start;
    if (step > 2) step = 1;
    while (j < pairs.length && pairs[j][0] === end + step && pairs[j][1] - pairs[j][0] === delta) end = pairs[j++][0];
    ranges.push([start, end, step, delta]); i = j;
  }
  const text = '// Generated from .NET OrdinalIgnoreCase-compatible invariant Rune mappings.\n' +
    '// Source pin: ' + baseline.ref + '; runtime: ' + baseline.toolchain.runtime + '.\n' +
    'export const OrdinalCaseRanges = Object.freeze(' + JSON.stringify(ranges) + ');\n';
  const target = path.join(javascriptRoot, 'runtime', 'OrdinalCasing.generated.js');
  if (process.argv.includes('--check')) {
    if (fs.readFileSync(target, 'utf8') !== text) throw new Error('Ordinal casing data drift.');
  } else fs.writeFileSync(target, text);
  console.log(`Ordinal mapping: ${pairs.length} scalar mappings in ${ranges.length} ranges.`);
} finally { await oracle.close(); }
