import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot } from './dotnet.mjs';
/** Observe complete scenarios; crashes and malformed responses remain failures.
 * Source exceptions are reported per operation, not synthesized by this transport.
 */
export class DocumentOracleSession {
  #client = null; #factory;
  constructor(factory = () => new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]})) { this.#factory = factory; }
  #get() { return this.#client ??= this.#factory(); }
  async environment() { return this.#get().request({op:'environment'}); }
  async observe(request) {
    try {
      if (request.op !== 'document-ownership' || !Array.isArray(request.steps) || request.steps.length === 0) throw new Error('A nonempty document scenario is required.');
      const value = await this.#get().request(request);
      if (!Array.isArray(value) || value.length !== request.steps.length || value.some(item => item == null || !Object.hasOwn(item,'result') || !(item.error === null || typeof item.error === 'string') || !(item.param === null || typeof item.param === 'string')))
        throw new Error('Incomplete or malformed document response: ' + JSON.stringify(value));
      return {ok:true,value};
    } catch(error) {
      const failure = {message:error.stack ?? String(error),request}, client = this.#client; this.#client = null;
      if(client !== null) try {await client.close();} catch(closeError) {failure.closeError=closeError.stack ?? String(closeError);}
      return {ok:false,failure};
    }
  }
  async close() { const client = this.#client; this.#client = null; if(client !== null) await client.close(); }
}
