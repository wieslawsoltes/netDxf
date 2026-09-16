/** Native adapters for the audited geometry cluster; no CLR, code evaluator or server. */
import {
  ArgumentException, ArgumentNullException, ArgumentOutOfRangeException,
  ArithmeticException, IndexOutOfRangeException, NullReferenceException, NotSupportedException,
  RequireInteger,
} from './Errors.js';

const nanView = new DataView(new ArrayBuffer(8));
nanView.setBigUint64(0, 0xfff8000000000000n, true);
export const DotNetNaN = nanView.getFloat64(0, true);
// Match the pinned .NET/SSE operand choice when both multiplication operands are NaNs.
// Finite arithmetic remains one binary64 multiplication with no rounding/epsilon adjustment.
export function MultiplyDouble(a, b) { return Number.isNaN(a) ? a : a * b; }
export function RemainderDouble(a, b) { return Number.isNaN(a) || Number.isNaN(b) ? DotNetNaN : a % b; }
export const ConstructorTag = Symbol('exact C# constructor');
export const CopyValue = Symbol('copy C# value');
export function Copy(value) { return value?.[CopyValue]?.() ?? value; }
export function Init(value, initialize) { initialize(value); return value; }
export function GetElement(values, index) {
  if (values == null) throw new NullReferenceException('Array reference is null.');
  if (!Number.isInteger(index) || index < 0 || index >= values.length)
    throw values instanceof List ? new ArgumentOutOfRangeException('index', index) : new IndexOutOfRangeException('Index was outside the bounds of the array.');
  return values[index];
}
export function SetElement(values, index, value) {
  GetElement(values, index); values[index] = value; return value;
}
export class List extends Array {
  static get [Symbol.species]() { return Array; }
  constructor(values = []) {
    super();
    if (values == null) throw new ArgumentNullException('collection');
    if (typeof values === 'number') { RequireInteger(values, 0, 2147483647, 'capacity'); return; }
    for (const item of values) this.push(Copy(item));
  }
  get Count() { return this.length; }
  get_Item(index) { return Copy(GetElement(this, index)); }
  set_Item(index, value) { SetElement(this, index, Copy(value)); }
  Add(value) { this.push(Copy(value)); }
  AddRange(values) {
    if (values == null) throw new ArgumentNullException('collection');
    // List.AddRange(self) is well-defined, unlike extending a live JS array iterator.
    for (const value of values === this ? this.slice() : values) this.Add(value);
  }
  Clear() { this.length = 0; }
  ToArray() { return Array.from(this, Copy); }
  GetEnumerator() { return this[Symbol.iterator](); }
}
export class Tuple {
  constructor(...items) {
    items.forEach((value, index) => {
      const item = Copy(value);
      Object.defineProperty(this, `Item${index + 1}`, { enumerable: true, get: () => Copy(item) });
    });
    Object.freeze(this);
  }
}
export function Int32(value) {
  // .NET 8 x64 unchecked conv.i4 uses the integer-indefinite value outside the range.
  const integer = Math.trunc(value);
  return !Number.isFinite(integer) || integer < -2147483648 || integer > 2147483647 ? -2147483648 : integer | 0;
}
export const Int16 = value => (Int32(value) << 16) >> 16;
export const Byte = value => Int32(value) & 255;
export function Int32FromBytes(bytes, start) {
  return GetElement(bytes, start) | (GetElement(bytes, start + 1) << 8) |
    (GetElement(bytes, start + 2) << 16) | (GetElement(bytes, start + 3) << 24);
}
const bits = new DataView(new ArrayBuffer(8));
export function DoubleHash(value) {
  bits.setFloat64(0, value, true);
  let low = bits.getUint32(0, true), high = bits.getUint32(4, true);
  if (value === 0) high = low = 0;
  else if (Number.isNaN(value)) { high = 0x7ff00000; low = 0; }
  return (high ^ low) | 0;
}
const powers = [1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000,
  1000000000, 10000000000, 100000000000, 1000000000000, 10000000000000,
  100000000000000, 1000000000000000];
export const DotNetMath = Object.freeze({
  Abs: Math.abs, Sqrt: Math.sqrt, Sin: Math.sin, Cos: Math.cos, Tan: Math.tan,
  Asin: value => Number.isNaN(value) ? value : Math.abs(value) > 1 ? DotNetNaN : Math.asin(value),
  Acos: value => Number.isNaN(value) ? value : Math.abs(value) > 1 ? DotNetNaN : Math.acos(value),
  Atan: Math.atan, Atan2: Math.atan2,
  Pow: Math.pow, Exp: Math.exp, Log: Math.log, Log10: Math.log10,
  Min: Math.min, Max: Math.max, Floor: Math.floor, Ceiling: Math.ceil, Truncate: Math.trunc,
  Sign(value) {
    if (Number.isNaN(value)) throw new ArithmeticException('Function does not accept floating point Not-a-Number values.');
    return value > 0 ? 1 : value < 0 ? -1 : 0;
  },
  Round(value, digits = 0) {
    RequireInteger(digits, 0, 15, 'digits');
    if (Math.abs(value) >= 1e16 || !Number.isFinite(value)) return value;
    const power = powers[digits], scaled = value * power, absolute = Math.abs(scaled);
    const floor = Math.floor(absolute), fraction = absolute - floor;
    const result = (fraction > 0.5 || (fraction === 0.5 && floor % 2 !== 0) ? floor + 1 : floor) / power;
    return scaled < 0 || Object.is(scaled, -0) ? -result : result;
  },
});

const invariant = Object.freeze({ Name: '', NumberDecimalSeparator: '.', ListSeparator: ',', PositiveInfinitySymbol: 'Infinity', NegativeInfinitySymbol: '-Infinity', NaNSymbol: 'NaN' });
let current = invariant;
export const Culture = {
  Invariant: invariant,
  get Current() { return current; },
  set Current(value) { current = resolveProvider(value); },
  get ListSeparator() { return current.ListSeparator; },
  // Reference evidence targets Linux. Callers can select CRLF for Windows-style display text.
  NewLine: '\n',
};
function resolveProvider(value) {
  if (value == null) return current;
  if (value === invariant) return invariant;
  const name = typeof value === 'string' ? value : value.Name;
  if (typeof name !== 'string') throw new ArgumentException('Provide a culture name or numeric format provider.', 'provider');
  if (name === '' || name.toLowerCase() === 'invariant') return invariant;
  const parts = new Intl.NumberFormat(name).formatToParts(1.5);
  const decimal = parts.find(part => part.type === 'decimal')?.value ?? '.';
  return { ...invariant, Name: name, NumberDecimalSeparator: decimal, ListSeparator: decimal === ',' ? ';' : ',',
    PositiveInfinitySymbol: '∞', NegativeInfinitySymbol: '-∞', ...(typeof value === 'object' ? value : {}) };
}
export function NumberText(value, provider = null) {
  const format = resolveProvider(provider);
  if (Number.isNaN(value)) return format.NaNSymbol;
  if (!Number.isFinite(value)) return value > 0 ? format.PositiveInfinitySymbol : format.NegativeInfinitySymbol;
  if (Object.is(value, -0)) return '-0';
  if (value === 0) return '0';
  const negative = value < 0, s = Math.abs(value).toString();
  let digits, exponent;
  if (s.includes('e')) {
    const [mantissa, e] = s.split('e'); digits = mantissa.replace('.', ''); exponent = Number(e);
  } else {
    const dot = s.indexOf('.'), full = s.replace('.', '');
    let first = 0; while (full[first] === '0') first++;
    digits = full.slice(first); exponent = (dot < 0 ? s.length : dot) - first - 1;
  }
  digits = digits.replace(/0+$/, '');
  let text;
  if (exponent < -4 || exponent >= 17) text = digits[0] + (digits.length > 1 ? '.' + digits.slice(1) : '') +
    'E' + (exponent < 0 ? '-' : '+') + String(Math.abs(exponent)).padStart(2, '0');
  else if (exponent < 0) text = '0.' + '0'.repeat(-exponent - 1) + digits;
  else if (digits.length <= exponent + 1) text = digits + '0'.repeat(exponent + 1 - digits.length);
  else text = digits.slice(0, exponent + 1) + '.' + digits.slice(exponent + 1);
  return (negative ? '-' : '') + text.replace('.', format.NumberDecimalSeparator);
}
export function Format(format, ...values) {
  // Only the parameter substitutions used by the selected C# sources are admitted.
  return format.replace(/\{(\d+)\}/g, (_, index) => {
    const value = values[Number(index)];
    return typeof value === 'number' ? NumberText(value) : value == null ? '' : typeof value.ToString === 'function' ? value.ToString() : String(value);
  });
}
export class StringBuilder {
  #parts = [];
  Append(value) { this.#parts.push(String(value)); return this; }
  ToString() { return this.#parts.join(''); }
}
/** System.Drawing.Color migration adapter. AciColor's conversion uses only ARGB components. */
export class Color {
  #name = null;
  #empty = false;
  constructor(a = 0, r = 0, g = 0, b = 0) {
    this.#empty = arguments.length === 0;
    for (const [name, value] of Object.entries({ A: a, R: r, G: g, B: b })) {
      RequireInteger(value, 0, 255, name); Object.defineProperty(this, name, { value, enumerable: true });
    }
    Object.freeze(this);
  }
  static get White() { const color = new Color(255, 255, 255, 255); color.#name = 'White'; return color; }
  static get Empty() { return new Color(); }
  get Name() { return this.#empty ? '0' : this.#name ?? (this.ToArgb() >>> 0).toString(16); }
  get IsEmpty() { return this.#empty; }
  get IsNamedColor() { return this.#name !== null; }
  get IsKnownColor() { return this.#name !== null; }
  Equals(other) { return other instanceof Color && this.ToArgb() === other.ToArgb() && this.#name === other.#name && this.#empty === other.#empty; }
  static FromArgb(...args) {
    if (args.length === 2) return new Color(args[0], args[1].R, args[1].G, args[1].B);
    if (args.length === 3) return new Color(255, ...args);
    if (args.length === 4) return new Color(...args);
    if (args.length === 1) { const n = args[0]; return new Color((n >>> 24) & 255, (n >>> 16) & 255, (n >>> 8) & 255, n & 255); }
    throw new ArgumentException('Expected RGB, ARGB or packed ARGB.');
  }
  ToArgb() { return (this.A << 24) | (this.R << 16) | (this.G << 8) | this.B; }
}

export function MemberwiseClone(value) {
  const clone = Object.create(Object.getPrototypeOf(value));
  for (const key of Reflect.ownKeys(value)) {
    const descriptor = Object.getOwnPropertyDescriptor(value,key);
    if ('value' in descriptor) descriptor.value = Copy(descriptor.value);
    Object.defineProperty(clone,key,descriptor);
  }
  return clone;
}
const whitespace = /^[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]*$/;
export const NativeString = Object.freeze({
  IsNullOrEmpty: value => value == null || value.length === 0,
  IsNullOrWhiteSpace: value => value == null || whitespace.test(value),
  IndexOfAny(value,chars) {
    if(value==null)throw new NullReferenceException('String is null.');
    if(chars==null)throw new ArgumentNullException('anyOf');
    for(let i=0;i<value.length;i++)if(chars.includes(value[i]))return i;
    return -1;
  },
  StartsWith(value,prefix,comparison=4) {
    if(value==null)throw new NullReferenceException('String is null.');
    if(prefix==null)throw new ArgumentNullException('value');
    // Audited source use is HeaderVariable's ASCII '$' prefix; don't pretend to
    // implement general invariant linguistic comparison for other prefixes.
    if(comparison!==4 && prefix!=='$')throw new NotSupportedException('Linguistic prefix comparison is not yet ported.');
    return value.startsWith(prefix);
  },
});
