// DEVELOPMENT-ONLY independent high-precision mathematical reference, MIT license.
// It is not imported by the production entry and does not replace the pinned reference-math backend.
// No native library, code generation, fixture lookup, or host-math approximation.
// Integer fixed-point arithmetic keeps intermediate rounding independent of engines.
// This computes a high-precision mathematical result; it does not promise to reproduce
// every platform libm rounding error. The strict .NET differential gate remains authoritative.
// See doc/NUMERICS.md for reference precision, measured cost, and retained counterexamples.
const P = 256n, ONE = 1n << P;
const HIGH_P = 1536n, HIGH_ONE = 1n << HIGH_P;
const bits = new DataView(new ArrayBuffer(8));
export function QuietNaN(value) { bits.setFloat64(0, value); bits.setBigUint64(0, bits.getBigUint64(0) | 0x8000000000000n); return bits.getFloat64(0); }
function negativeNaN() { bits.setBigUint64(0, 0xfff8000000000000n); return bits.getFloat64(0); }
function parts(value) {
  bits.setFloat64(0,value);
  const raw = bits.getBigUint64(0), fraction = raw & 0xfffffffffffffn, exponent = Number((raw >> 52n) & 2047n);
  return { sign: raw >> 63n ? -1n : 1n, mantissa: exponent ? fraction | 0x10000000000000n : fraction,
    exponent: exponent ? exponent - 1075 : -1074 };
}
function fixed(value, precision = P) {
  const d = parts(value), shift = BigInt(d.exponent) + precision;
  return d.sign * (shift >= 0 ? d.mantissa << shift : d.mantissa >> -shift);
}
function nearestQuotient(numerator, denominator) {
  const negative = numerator < 0n;
  if(negative) numerator = -numerator;
  const q = numerator / denominator, r = numerator % denominator;
  const rounded = q + (2n*r > denominator || (2n*r === denominator && (q & 1n)) ? 1n : 0n);
  return negative ? -rounded : rounded;
}
function roundFixed(value, precision=P) {
  if(value === 0n) return 0;
  const negative = value < 0n; if(negative) value = -value;
  const length = value.toString(2).length;
  const shift = Math.max(length - 53, Number(precision) - 1074);
  let significand;
  if(shift > 0) significand=nearestQuotient(value,1n<<BigInt(shift));
  else significand = value << BigInt(-shift);
  const magnitude = Number(significand) * 2 ** (shift - Number(precision));
  return negative ? -magnitude : magnitude;
}
function atanSeries(x, precision) {
  const squared=(x*x)>>precision;
  let power=x,result=x;
  for(let n=3n; ; n+=2n) {
    power=-((power*squared)>>precision);
    const term=power/n;
    if(term===0n) return result;
    result+=term;
  }
}
// Machin's identity. Constants are calculated, not extracted from a native libm.
const PI_HIGH=16n*atanSeries(HIGH_ONE/5n,HIGH_P)-4n*atanSeries(HIGH_ONE/239n,HIGH_P);
const PI=PI_HIGH>>(HIGH_P-P), HALF_PI=PI/2n, QUARTER_PI=PI/4n;
const HALF_PI_DOUBLE=roundFixed(HALF_PI), PI_DOUBLE=roundFixed(PI);
function positiveAtan(x) {
  if(x>ONE) return HALF_PI-positiveAtan((ONE*ONE)/x);
  if(x>ONE/2n) return QUARTER_PI+atanSeries(((x-ONE)*ONE)/(x+ONE),P);
  return atanSeries(x,P);
}
// The Taylor coefficients are exact rational factorial reciprocals, rounded once
// to the working integer scale. Horner evaluation avoids a BigInt division per term.
function coefficients(odd) {
  let factorial = 1n;
  const result = [];
  for (let n = 0; ; n++) {
    if (n) factorial *= BigInt(n);
    if ((n & 1) !== odd) continue;
    const coefficient = nearestQuotient(ONE, factorial);
    if (!coefficient) return result;
    result.push(((n >> 1) & 1) ? -coefficient : coefficient);
  }
}
const SINE_COEFFICIENTS = coefficients(1), COSINE_COEFFICIENTS = coefficients(0);
function polynomial(coefficients, xSquared) {
  let result = coefficients.at(-1);
  for (let i = coefficients.length - 2; i >= 0; i--)
    result = ((result * xSquared) >> P) + coefficients[i];
  return result;
}
let lastArgument = NaN, lastResult = null;
function calculateSinCos(value) {
  const argument = fixed(value, HIGH_P), half = PI_HIGH / 2n;
  const quadrant = nearestQuotient(argument, half);
  const x = (argument - quadrant * half) >> (HIGH_P - P), xSquared = (x * x) >> P;
  const sine = (x * polynomial(SINE_COEFFICIENTS, xSquared)) >> P;
  const cosine = polynomial(COSINE_COEFFICIENTS, xSquared);
  switch (Number((quadrant % 4n + 4n) % 4n)) {
    case 0: return [sine, cosine];
    case 1: return [cosine, -sine];
    case 2: return [-sine, -cosine];
    default: return [-cosine, sine];
  }
}
// Rotation and polar routines request sine and cosine of the same angle in sequence.
// One retained pair is bounded independently of document size; callers cannot mutate it.
function sinCos(value) {
  if (value === lastArgument) return lastResult;
  const result = calculateSinCos(value);
  lastArgument = value; lastResult = result;
  return result;
}
function sqrtInteger(n) {
  if(n===0n)return 0n;
  let x=1n<<BigInt((n.toString(2).length+1)>>1);
  for(;;){const y=(x+n/x)>>1n;if(y>=x)return x;x=y;}
}
export function Sin(value) {
  if(Number.isNaN(value))return QuietNaN(value);
  if(!Number.isFinite(value))return negativeNaN();
  if(Math.abs(value)<2**-27)return value;
  return roundFixed(sinCos(value)[0]);
}
export function Cos(value) {
  if(Number.isNaN(value))return QuietNaN(value);
  if(!Number.isFinite(value))return negativeNaN();
  if(Math.abs(value)<2**-27)return 1;
  return roundFixed(sinCos(value)[1]);
}
export function Tan(value) {
  if(Number.isNaN(value))return QuietNaN(value);
  if(!Number.isFinite(value))return negativeNaN();
  if(Math.abs(value)<2**-27)return value;
  const [s,c]=sinCos(value), ratio=(s*ONE)/c;
  return roundFixed(ratio);
}
export function Atan(value) {
  if(Number.isNaN(value))return QuietNaN(value);
  if(Math.abs(value)<2**-27)return value;
  const sign=value<0?-1:1;
  if(Math.abs(value)>2**54)return sign*HALF_PI_DOUBLE;
  return sign*roundFixed(positiveAtan(fixed(Math.abs(value))));
}
export function Atan2(y,x) {
  if(Number.isNaN(x))return QuietNaN(x);
  if(Number.isNaN(y))return QuietNaN(y);
  const negativeY=y<0||Object.is(y,-0),negativeX=x<0||Object.is(x,-0),sign=negativeY?-1:1;
  if(y===0)return negativeX?sign*PI_DOUBLE:y;
  if(x===0)return sign*HALF_PI_DOUBLE;
  if(!Number.isFinite(y))return Number.isFinite(x)?sign*HALF_PI_DOUBLE:sign*roundFixed(negativeX?3n*QUARTER_PI:QUARTER_PI);
  if(!Number.isFinite(x))return negativeX?sign*PI_DOUBLE:sign*0;
  const ay=Math.abs(y),ax=Math.abs(x), ratio=ay/ax;
  if(ratio<2**-56 && !negativeX)return sign*ratio;
  // Do not replace a large finite ratio with pi/2 before quadrant selection.
  // On the negative-x side the tiny positive correction can cross a binary64 midpoint.
  const a=parts(ay),b=parts(ax), shift=BigInt(a.exponent-b.exponent)+P;
  const r=shift>=0?(a.mantissa<<shift)/b.mantissa:a.mantissa/(b.mantissa<<-shift);
  const angle=positiveAtan(r);
  return sign*roundFixed(negativeX?PI-angle:angle);
}
export function Asin(value) {
  if(Number.isNaN(value))return QuietNaN(value);
  if(Math.abs(value)>1)return NaN;
  if(Math.abs(value)<2**-27)return value;
  const x=fixed(Math.abs(value)), root=sqrtInteger(ONE*ONE-x*x);
  return (value<0?-1:1)*roundFixed(root===0n?HALF_PI:HALF_PI-positiveAtan(root*ONE/x));
}
export function Acos(value) {
  if(Number.isNaN(value))return QuietNaN(value);
  if(Math.abs(value)>1)return NaN;
  if(value===0)return HALF_PI_DOUBLE;
  const x=fixed(Math.abs(value)),root=sqrtInteger(ONE*ONE-x*x);
  const angle=x===0n?HALF_PI:positiveAtan(root*ONE/x);
  return roundFixed(value<0?PI-angle:angle);
}
