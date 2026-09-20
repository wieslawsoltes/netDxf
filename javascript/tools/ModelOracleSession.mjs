import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot } from './dotnet.mjs';

/** Retain a failed native observation and restart before the next independent scenario.
 * A missing/crashed/malformed result is never an expected successful API result.
 * Consumers must report failures and cannot qualify an incomplete observation.
 */
export class ModelOracleSession {
  #client=null;
  #factory;
  constructor(factory=()=>new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]})) { this.#factory=factory; }
  #get(){return this.#client??=this.#factory();}
  async environment(){return this.#get().request({op:'environment'});}
  async observe(request){
    try{
      if(!Array.isArray(request.steps)||request.steps.length===0)throw new Error('A nonempty model scenario is required.');
      const value=await this.#get().request(request);
      if(!Array.isArray(value)||value.length!==request.steps.length||value.some(item=>item==null||typeof item.ok!=='boolean'))
        throw new Error('Incomplete or malformed model response: '+JSON.stringify(value));
      return {ok:true,value};
    }catch(error){
      const failure={message:error.stack??String(error),request};
      const client=this.#client;this.#client=null;
      if(client!==null)try{await client.close();}catch(closeError){failure.closeError=closeError.stack??String(closeError);}
      return {ok:false,failure};
    }
  }
  async close(){const client=this.#client;this.#client=null;if(client!==null)await client.close();}
}
