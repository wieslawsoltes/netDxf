import { Copy } from './GeometryRuntime.js';
import { IndexOutOfRangeException, NotSupportedException } from './Errors.js';

/** Fixed-length CLR-array adapter. Bracket access exposes an element location;
 * get_Item returns a value copy (important for Vector3[]). Assignment copies in.
 * Element validation is supplied by the typed owner, not guessed from values.
 */
export function FixedArray(items, convert = Copy) {
  const values = Array.from(items, convert), array = new Array(values.length);
  const check = index => {
    if (!Number.isInteger(index) || index < 0 || index >= values.length) throw new IndexOutOfRangeException();
  };
  for (let i = 0; i < values.length; i++) Object.defineProperty(array, i, {
    enumerable: true, get() { return values[i]; }, set(value) { values[i] = convert(value); }
  });
  Object.defineProperties(array, {
    length: { writable: false }, Length: { get() { return values.length; } }, Count: { get() { return values.length; } },
    get_Item: { value(index) { check(index); return Copy(values[index]); } },
    set_Item: { value(index, value) { check(index); values[index] = convert(value); } },
    Clone: { value() { return FixedArray(values, convert); } }
  });
  Object.seal(array);
  return new Proxy(array, {
    set(target, property, value) {
      if (property === 'length') { if (value !== values.length) throw new NotSupportedException('Array length is fixed.'); return true; }
      if (typeof property === 'string' && /^-?\d+$/.test(property)) check(Number(property));
      return Reflect.set(target, property, value);
    },
    get(target, property, receiver) {
      if (typeof property === 'string' && /^-?\d+$/.test(property)) check(Number(property));
      return Reflect.get(target, property, receiver);
    }
  });
}
