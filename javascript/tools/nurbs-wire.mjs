// Test-only adapter for the extracted Spline static evaluator. No Spline model is fabricated.
import { NurbsEvaluator, CreateKnotVector } from '../runtime/NurbsEvaluator.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { doubleBits, fromBits } from './wire.mjs';
function read(value){
  if(value==null||typeof value!=='object')return value;
  if('double'in value)return fromBits(value.double);if('int'in value)return value.int;
  if('array'in value)return value.values.map(read);
  if(value.new==='Vector3')return new Vector3(...value.args.map(read));
  throw new Error('Unsupported NURBS test input.');
}
function wire(value){
  if(typeof value==='number')return {double:doubleBits(value)};
  if(value instanceof Vector3)return {type:'Vector3',values:['X','Y','Z'].map(k=>doubleBits(value[k])),normalized:value.IsNormalized};
  return Array.from(value,wire);
}
export function jsNurbs(input){return input.steps.map(step=>{
  try{
    const call=step.member==='NurbsEvaluator'?NurbsEvaluator:step.member==='CreateKnotVector'?CreateKnotVector:null;
    if(!call)throw new Error('Unknown NURBS test method.');
    return {ok:true,value:wire(call(...step.args.map(read)))};
  }catch(error){return {ok:false,error:error.name,param:error.ParamName??null};}
});}
