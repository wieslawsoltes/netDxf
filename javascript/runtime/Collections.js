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
export const OrdinalIgnoreCaseEquals = (left, right) =>
  typeof left === 'string' && typeof right === 'string' && left.toUpperCase() === right.toUpperCase();
