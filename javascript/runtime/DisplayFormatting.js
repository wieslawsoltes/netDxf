// Shared culture-aware display formatting for generated and native model APIs.
import { ArgumentException } from './Errors.js';
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
    return typeof value === 'number' ? NumberText(value) : typeof value === 'boolean' ? (value ? 'True' : 'False') : value == null ? '' : typeof value.ToString === 'function' ? value.ToString() : String(value);
  });
}
export class StringBuilder {
  #parts = [];
  Append(value) { this.#parts.push(String(value)); return this; }
  ToString() { return this.#parts.join(''); }
}
