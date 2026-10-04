// Explicit IReadOnlyDictionary<DxfObject,DxfObject> transport adapter.
import { ArgumentException, ArgumentNullException } from './Errors.js';
export function ReferenceMap(entries) {
  const result = new Map();
  if (entries != null) for (const pair of entries) {
    const key = Array.isArray(pair) ? pair[0] : pair.Key, value = Array.isArray(pair) ? pair[1] : pair.Value;
    if (key == null) throw new ArgumentNullException('key');
    if (result.has(key)) throw new ArgumentException('An item with the same key has already been added.');
    result.set(key, value);
  }
  return result;
}
