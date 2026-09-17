import { FormatException } from './Errors.js';
// Read NaN from binary storage on each parse. Optimizers may canonicalize an imported
// NaN constant; PAT/LIN NumberStyles.Float parsing must retain the pinned .NET bits.
const parsedNaN = new DataView(new ArrayBuffer(8));
parsedNaN.setUint32(0, 0xfff80000);
parsedNaN.setUint32(4, 0);
export const TrimDotNet = value => value.replace(/^[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+|[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+$/g, '');
/** NumberStyles.Float with the invariant provider; no thousands, hex, or partial parses. */
export function ParseInvariantFloat(token) {
  // Finite .NET parsing permits terminal NULs after optional ASCII whitespace.
  // The special-symbol path does not, and whitespace after NUL is rejected.
  const match = /^[\t-\r ]*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)[\t-\r ]*\0*$/.exec(token);
  if (match) return Number(match[1]);
  const special = TrimDotNet(token).toLowerCase();
  if (/^[+-]?nan$/.test(special)) return parsedNaN.getFloat64(0);
  if (/^[+]?infinity$/.test(special)) return Infinity;
  if (special === '-infinity') return -Infinity;
  throw new FormatException('The input string was not in a correct format.');
}
const invariantCase = new Intl.Collator('en', { sensitivity: 'accent', usage: 'sort' });
export const InvariantIgnoreCaseEquals = (a, b) => a == null || b == null ? a === b : invariantCase.compare(a, b) === 0;
