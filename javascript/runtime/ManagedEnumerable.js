import { NullReferenceException } from './Errors.js';
// Synchronous IEnumerable<T>/IDisposable adaptation for callback-sensitive APIs.
// Enumeration and disposal finish before the caller is allowed to commit state.
// Native JS iterables are supported too: return() is their explicit cleanup hook.
export function ConsumeManagedEnumerable(source, consume) {
  if (typeof source.GetEnumerator === 'function') {
    const iterator = source.GetEnumerator();
    try { if (iterator == null) throw new NullReferenceException(); while (iterator.MoveNext()) consume(iterator.Current); }
    finally { iterator?.Dispose?.(); }
  } else {
    const iterator = source[Symbol.iterator]();
    try {
      for (;;) { const next = iterator.next(); if (next.done) break; consume(next.value); }
    } finally { iterator?.return?.(); }
  }
}
