/** Invariant .NET 8 G17 formatting, including midpoint-to-even and signed zero. */
const bits = new DataView(new ArrayBuffer(8));
function trailingZeroBits(value) { return 31 - Math.clz32((value & -value) >>> 0); }
export function FormatDouble(value) {
  if (value === 0) return Object.is(value, -0) ? '-0.0' : '0.0';
  if (!Number.isFinite(value)) return Number.isNaN(value) ? 'NaN' : value < 0 ? '-Infinity' : 'Infinity';
  const negative = value < 0, absolute = Math.abs(value);
  const [mantissa, exponentText] = absolute.toExponential(16).split('e');
  const exponent = Number(exponentText);
  let digits = mantissa.replace('.', '');
  // JS rounds decimal ties upward; .NET 8 G17 uses ties-to-even. A binary64 midpoint
  // at 17 significant digits is possible only in this exponent interval. Inspect the
  // exact binary exponent after removing factors of two; no decimal approximation.
  if ((Number(digits[16]) & 1) && exponent >= -8 && exponent <= 15) {
    bits.setFloat64(0, absolute, true);
    const low = bits.getUint32(0, true), high = bits.getUint32(4, true);
    const binaryExponent = ((high >>> 20) & 2047) - 1023 - 52;
    const zeros = low !== 0 ? trailingZeroBits(low) : 32 + trailingZeroBits((high & 0xfffff) | 0x100000);
    if (binaryExponent + zeros + (16 - exponent) === -1)
      digits = digits.slice(0, 16) + String(Number(digits[16]) - 1);
  }
  digits = digits.replace(/0+$/, '');
  let text;
  if (exponent < -4 || exponent >= 17) {
    text = digits[0] + (digits.length > 1 ? '.' + digits.slice(1) : '') + 'E' +
      (exponent < 0 ? '-' : '+') + String(Math.abs(exponent)).padStart(2, '0');
  } else if (exponent < 0) text = '0.' + '0'.repeat(-exponent - 1) + digits;
  else if (digits.length <= exponent + 1) text = digits + '0'.repeat(exponent + 1 - digits.length) + '.0';
  else text = digits.slice(0, exponent + 1) + '.' + digits.slice(exponent + 1);
  return (negative ? '-' : '') + text;
}
const white = /^[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+|[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+$/g;
export function NormalizeHandle(text) {
  const trimmed = text.replace(white, '');
  return /^[0-9a-fA-F]{1,16}$/.test(trimmed) ? trimmed.replace(/^0+(?=.)/, '').toUpperCase() : null;
}
export function HexBytes(bytes) {
  let result = '';
  for (const b of bytes) result += b.toString(16).toUpperCase().padStart(2, '0');
  return result;
}
