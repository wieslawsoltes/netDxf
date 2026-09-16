// .NET primitive adaptations used by the native geometry port. MIT License.
import { ArgumentException, ArgumentOutOfRangeException, ArithmeticException } from './Errors.js';

export function NumberValue(value, parameter = 'value') {
  if (typeof value !== 'number') throw new ArgumentException('A JavaScript Number is required.', parameter);
  return value;
}

/** Math.Round(double, digits), midpoint-to-even; arithmetic order follows the CLR. */
export function RoundToEven(value, digits = 0) {
  NumberValue(value);
  if (!Number.isInteger(digits) || digits < 0 || digits > 15)
    throw new ArgumentOutOfRangeException('digits', digits);
  if (Math.abs(value) >= 1e16 || !Number.isFinite(value)) return value;
  const factor = [1,10,100,1000,10000,100000,1000000,10000000,100000000,1000000000,
    10000000000,100000000000,1000000000000,10000000000000,100000000000000,1000000000000000][digits];
  const scaled = value * factor, magnitude = Math.abs(scaled), integer = Math.floor(magnitude);
  const fraction = magnitude - integer;
  const rounded = fraction < 0.5 ? integer : fraction > 0.5 ? integer + 1 : integer + (integer % 2);
  return (scaled < 0 || Object.is(scaled, -0) ? -rounded : rounded) / factor;
}

export function Sign(value) {
  if (Number.isNaN(value)) throw new ArithmeticException('Function does not accept floating point Not-a-Number values.');
  return value < 0 ? -1 : value > 0 ? 1 : 0;
}

const bits = new DataView(new ArrayBuffer(8));
export function DoubleHashCode(value) {
  if (value === 0) return 0;
  if (Number.isNaN(value)) return 0x7ff00000;
  bits.setFloat64(0, value, true);
  return bits.getInt32(0, true) ^ bits.getInt32(4, true);
}

/** Explicit invariant-G/provider adaptation; no ambient browser locale is consulted. */
export function GeneralNumber(value, provider = null) {
  const format = provider?.NumberFormat ?? provider ?? {};
  if (Number.isNaN(value)) return format.NaNSymbol ?? 'NaN';
  if (value === Infinity) return format.PositiveInfinitySymbol ?? 'Infinity';
  if (value === -Infinity) return format.NegativeInfinitySymbol ?? '-Infinity';
  const negative = value < 0 || Object.is(value, -0);
  const [mantissa, e] = Math.abs(value).toExponential().split('e');
  const exponent = Number(e), digits = mantissa.replace('.', '');
  let result;
  if (exponent < -4 || exponent >= 17) result = mantissa + 'E' + (exponent < 0 ? '-' : '+') + String(Math.abs(exponent)).padStart(2, '0');
  else if (exponent < 0) result = '0.' + '0'.repeat(-exponent - 1) + digits;
  else if (digits.length <= exponent + 1) result = digits + '0'.repeat(exponent + 1 - digits.length);
  else result = digits.slice(0, exponent + 1) + '.' + digits.slice(exponent + 1);
  result = result.replace('.', format.NumberDecimalSeparator ?? '.');
  return (negative ? format.NegativeSign ?? '-' : '') + result;
}
export const ListSeparator = provider => provider?.TextInfo?.ListSeparator ?? provider?.ListSeparator ?? ',';

// .NET Double.NaN has this sign/payload in the pinned runtime. Keep even nonfinite bit tests exact.
const nanBits = new DataView(new ArrayBuffer(8));
nanBits.setBigUint64(0, 0xfff8000000000000n, false);
export const DoubleNaN = nanBits.getFloat64(0, false);
