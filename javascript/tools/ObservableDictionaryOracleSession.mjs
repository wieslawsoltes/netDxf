import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot } from './dotnet.mjs';

/** Strict transport validation, not an expected-output implementation.
 * Constructor exceptions are observed once; unexecuted steps are not fabricated.
 * A crashed or malformed response remains a failure and the next scenario restarts.
 */
export class ObservableDictionaryOracleSession {
  #client = null; #factory;
  constructor(factory = () => new OracleClient({ args: [path.join(oracleRoot, 'GeometryOracle.dll')] })) { this.#factory = factory; }
  #get() { return this.#client ??= this.#factory(); }
  async environment() { return this.#get().request({ op: 'environment' }); }
  async observe(request) {
    try {
      if (request.op !== 'observable-dictionary' || !Array.isArray(request.steps) || request.steps.length === 0)
        throw new Error('A nonempty observable dictionary scenario is required.');
      const value = await this.#get().request(request);
      const constructorFailure = value !== null && !Array.isArray(value) && typeof value.constructorError === 'string' &&
        (value.param === null || typeof value.param === 'string') && Object.keys(value).length === 2;
      const snapshots = Array.isArray(value) && value.length === request.steps.length && value.every(item =>
        item !== null && typeof item === 'object' && Object.hasOwn(item, 'result') &&
        (item.error === null || typeof item.error === 'string') && (item.param === null || typeof item.param === 'string') &&
        Number.isInteger(item.count) && item.count >= 0 && ['readOnly', 'keysReadOnly', 'valuesReadOnly'].every(key => typeof item[key] === 'boolean') &&
        ['items', 'keys', 'values', 'events', 'comparerCalls'].every(key => Array.isArray(item[key])) &&
        ['items', 'keys', 'values'].every(key => item[key].length === item.count));
      if (!constructorFailure && !snapshots) throw new Error('Incomplete or malformed dictionary response: ' + JSON.stringify(value));
      return { ok: true, value };
    } catch (error) {
      const failure = { message: error.stack ?? String(error), request };
      const client = this.#client; this.#client = null;
      if (client !== null) try { await client.close(); } catch (closeError) { failure.closeError = closeError.stack ?? String(closeError); }
      return { ok: false, failure };
    }
  }
  async close() { const client = this.#client; this.#client = null; if (client !== null) await client.close(); }
}
