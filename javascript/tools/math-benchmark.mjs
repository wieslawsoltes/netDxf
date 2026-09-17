// Descriptive performance cost, not compatibility evidence or a release threshold.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { performance } from 'node:perf_hooks';
import { DotNetMath } from '../runtime/GeometryRuntime.js';
import * as HighPrecision from './HighPrecisionMath.mjs';
import { javascriptRoot } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
const proof = { runtimeFingerprint: runtimeFingerprint(), verificationFingerprint: verificationFingerprint() };
const count = 1024, warmups = 3, samples = 12;
let seed = 0x4d415448;
const inputs = Array.from({ length: count }, () => {
  seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
  return ((seed / 0x100000000) * 2 - 1) * 10000;
});
function measure(name, action) {
  let checksum;
  for (let i = 0; i < warmups; i++) checksum = action();
  const timings = [];
  for (let i = 0; i < samples; i++) {
    const start = performance.now(); checksum = action(); timings.push(performance.now() - start);
  }
  if (!Number.isFinite(checksum)) throw new Error('Nonfinite benchmark checksum.');
  const sorted = [...timings].sort((a, b) => a - b);
  return { name, medianMs: (sorted[5] + sorted[6]) / 2, p95Ms: sorted[11], timings, checksum };
}
const pair = (sin, cos) => () => {
  let sum = 0;
  for (const x of inputs) sum += sin(x) + cos(x);
  return sum;
};
const results = [
  measure('host-sin-cos-pairs', pair(Math.sin, Math.cos)),
  measure('production-reference-sin-cos-pairs', pair(DotNetMath.Sin, DotNetMath.Cos)),
  measure('development-high-precision-sin-cos-pairs', pair(HighPrecision.Sin, HighPrecision.Cos)),
  measure('host-atan', () => { let sum = 0; for (const x of inputs) sum += Math.atan(x / 10000); return sum; }),
  measure('production-reference-atan', () => { let sum = 0; for (const x of inputs) sum += DotNetMath.Atan(x / 10000); return sum; }),
  measure('development-high-precision-atan', () => { let sum = 0; for (const x of inputs) sum += HighPrecision.Atan(x / 10000); return sum; }),
];
if (runtimeFingerprint() !== proof.runtimeFingerprint || verificationFingerprint() !== proof.verificationFingerprint)
  throw new Error('Code changed during math performance measurement.');
const report = { ...proof, completed: true, node: process.version, v8: process.versions.v8,
  platform: process.platform, architecture: process.arch, cpu: os.cpus()[0]?.model,
  count, warmups, samples, results,
  productionSinCosCostRatio: results[1].medianMs / results[0].medianMs,
  developmentSinCosCostRatio: results[2].medianMs / results[0].medianMs,
  productionAtanCostRatio: results[4].medianMs / results[3].medianMs,
  developmentAtanCostRatio: results[5].medianMs / results[3].medianMs,
  scope: 'Local warmed Node measurements. The host math path is a timing comparator, not an expected-output source. Import startup is excluded. These data do not establish production performance acceptance.' };
const out = path.join(javascriptRoot, 'artifacts/math-benchmark'); fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
