import { ArgumentException, ArgumentOutOfRangeException } from './Errors.js';
import { Copy } from './GeometryRuntime.js';
import { OrdinalIgnoreCaseKey } from './Collections.js';

export function IsAncestor(ancestor, item) {
  const seen = new Set();
  for (let next = item; next !== null && next !== undefined && !seen.has(next); next = next.Owner) {
    if (next === ancestor) return true; seen.add(next);
  }
  return false;
}
const reserved = new Set(['ACAD_GROUP','ACAD_LAYOUT','ACAD_MLINESTYLE','ACAD_IMAGE_DICT','ACAD_IMAGE_VARS','ACAD_DGNDEFINITIONS','ACAD_DWFDEFINITIONS','ACAD_PDFDEFINITIONS']);
export const IsReservedDictionaryName = name => reserved.has(OrdinalIgnoreCaseKey(name));

// Concrete optional model modules register their class identities after evaluation.
// This avoids ESM inheritance cycles; code-name strings alone never establish type.
const types = new Map();
export function RegisterDatabaseModel(name, type) {
  if (types.has(name) && types.get(name) !== type) throw new ArgumentException('Database model is already registered.', 'name');
  types.set(name, type);
}
export const IsDatabaseModel = (value, name) => types.has(name) && value instanceof types.get(name);

/** ReadOnlyCollection over a version-checked List; contained object references remain live. */
export function ReadOnlyReferenceView(list) {
  return Object.freeze({ get Count() { return list.Count; }, get length() { return list.Count; },
    get_Item(index) { return list.get_Item(index); }, GetEnumerator() { return list.GetEnumerator(); },
    [Symbol.iterator]() { return list[Symbol.iterator](); } });
}
/** Immutable snapshot with value-copy semantics for boxed coordinate cells. */
export function ImmutableCellView(values) {
  const cells = Array.from(values, Copy);
  return Object.freeze({ get Count() { return cells.length; }, get length() { return cells.length; },
    get_Item(index) {
      if (!Number.isInteger(index) || index < 0 || index >= cells.length) throw new ArgumentOutOfRangeException('index', index);
      return Copy(cells[index]);
    },
    GetEnumerator() { let index = -1; const owner = this;
      return { get Current() { return index < 0 || index >= cells.length ? null : owner.get_Item(index); },
        MoveNext() { if (index < cells.length) index++; return index < cells.length; }, Reset() { index = -1; }, Dispose() {},
        next() { return this.MoveNext() ? { done:false,value:this.Current } : {done:true}; }, [Symbol.iterator]() { return this; } };
    }, [Symbol.iterator]() { return this.GetEnumerator(); } });
}
