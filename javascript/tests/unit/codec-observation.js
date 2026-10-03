import test from 'node:test';
import assert from 'node:assert/strict';
import { ValidateCodecObservation } from '../../tools/codec-observation.mjs';
const host=()=>({calls:[],closed:false});
const valid=()=>({constructor:null,initial:null,results:[{value:null,error:null,state:null,host:host()}],host:host()});
test('codec observer validates the full command count without inventing results',()=>{
  const value=valid();assert.equal(ValidateCodecObservation(value,1),value);assert.throws(()=>ValidateCodecObservation(value,2));
});
test('codec constructor rejections must have no executable commands or instance',()=>{
  const value={constructor:{name:'ArgumentException',param:'input',message:null},initial:null,results:[],host:host()};assert.equal(ValidateCodecObservation(value,3),value);
  value.initial={};assert.throws(()=>ValidateCodecObservation(value,3));
});
test('codec observer rejects truncated response fields rather than treating absence as null',()=>{
  for(const key of ['constructor','initial','results','host']){const value=valid();delete value[key];assert.throws(()=>ValidateCodecObservation(value,1));}
  for(const key of ['value','error','state','host']){const value=valid();delete value.results[0][key];assert.throws(()=>ValidateCodecObservation(value,1));}
});
test('codec observer rejects invalid cardinality and malformed native errors',()=>{
  for(const requested of [0,-1,NaN,1.5])assert.throws(()=>ValidateCodecObservation(valid(),requested));
  for(const error of [{},'error',{name:'Exception',param:0,message:null}]){const value=valid();value.results[0].error=error;assert.throws(()=>ValidateCodecObservation(value,1));}
});
