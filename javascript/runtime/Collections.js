import { OrdinalCaseRanges } from './OrdinalCasing.generated.js';
import { ArgumentOutOfRangeException } from './Errors.js';
/** Immutable snapshot with JS iteration/indexing and the C# Count/get_Item accessors. */
export function ReadOnlyList(values) {
  const list = Array.from(values);
  Object.defineProperties(list, {
    Count: { get() { return this.length; } },
    get_Item: { value(index) {
      if (!Number.isInteger(index) || index < 0 || index >= this.length)
        throw new ArgumentOutOfRangeException('index', index);
      return this[index];
    } },
    GetEnumerator: { value() { return this[Symbol.iterator](); } },
  });
  return Object.freeze(list);
}
const ordinalUpper = new Map();
for (const [start, end, step, delta] of OrdinalCaseRanges)
  for (let code = start; code <= end; code += step) ordinalUpper.set(code, code + delta);
/** One-scalar folding only: unlike String.toUpperCase(), it never expands ß to SS. */
export function OrdinalIgnoreCaseKey(text) {
  let result = '';
  for (const character of text) {
    const code = character.codePointAt(0);
    result += String.fromCodePoint(ordinalUpper.get(code) ?? code);
  }
  return result;
}
export function OrdinalIgnoreCaseEquals(left, right) {
  if (typeof left !== 'string' || typeof right !== 'string' || left.length !== right.length) return false;
  if (left === right) return true;
  for (let i = 0; i < left.length; i++) {
    let a = left.charCodeAt(i), b = right.charCodeAt(i);
    if (a === b) continue;
    if (a > 127 || b > 127) return OrdinalIgnoreCaseKey(left) === OrdinalIgnoreCaseKey(right);
    if (a >= 97 && a <= 122) a -= 32;
    if (b >= 97 && b <= 122) b -= 32;
    if (a !== b) return false;
  }
  return true;
}
