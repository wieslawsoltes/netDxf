import path from 'node:path';
import { ModelOracleSession } from './ModelOracleSession.mjs';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot } from './dotnet.mjs';
import { validateGteObservation } from './gte-observation.mjs';
/** Native process aborts and malformed envelopes remain unavailable evidence.
 * Each subsequent scenario starts afresh; no source assertion is waived. */
export class GteOracleSession extends ModelOracleSession {
  constructor(factory = () => new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]})) {
    super(() => {
      const client = factory();
      return {async request(input) {
        const result = await client.request(input);
        return input.op === 'gte' ? validateGteObservation(input,result) : result;
      }, close: () => client.close()};
    });
  }
}
