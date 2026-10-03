import test from 'node:test';
import assert from 'node:assert/strict';
import { ValidateDatabaseObservation } from '../../tools/database-io-observation.mjs';
for(const kind of ['payload','metadata'])for(const rows of [null,[],[{}],[{ok:true,value:{}}]])test('database evidence rejects '+kind+' '+JSON.stringify(rows),()=>{
  assert.throws(()=>ValidateDatabaseObservation(rows,1,kind));
});
test('database evidence preserves the exact observed values and errors',()=>{
  const rows=[{ok:true,value:{result:null,error:{type:'FormatException',param:null,message:'exact'},record:{model:null},pending:{},seed:'AB',apps:[],classes:[],output:''}}];
  assert.equal(ValidateDatabaseObservation(rows,1,'payload'),rows);assert.equal(rows[0].value.error.message,'exact');
});
