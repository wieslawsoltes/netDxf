import { createHash } from 'node:crypto';
import { baseline } from './dotnet.mjs';
/** Check the actual production .NET comparer profile; never substitute a JS comparator. */
export function validateOrdinalProfile(pairs, expected = baseline.globalization) {
  if (!Array.isArray(pairs) || !expected || typeof expected.profile !== 'string')
    throw new Error('A pinned .NET ordinal globalization profile is required.');
  const actual = createHash('sha256').update(JSON.stringify(pairs)).digest('hex');
  if (pairs.length !== expected.scalarMappings || actual !== expected.scalarMapSha256)
    throw new Error(`The .NET globalization profile differs from ${expected.profile}: ` +
      `${pairs.length} scalar mappings, SHA-256 ${actual}. Run the oracle on the documented reference platform. ` +
      'Do not silently regenerate the JavaScript tables or ignore the mismatches.');
  return Object.freeze({ profile: expected.profile, scalarMappings: pairs.length, scalarMapSha256: actual });
}
