// Native algorithms live in runtime modules; this is a Node/browser observation adapter.
import * as api from '../index.js';
import { GteSortedDictionary } from '../runtime/GteRuntime.js';
import { Copy } from '../runtime/GeometryRuntime.js';
import { wire } from './model-wire.mjs';
import { fromBits, doubleBits } from './wire.mjs';
import { typeName } from './model-types.mjs';
import { InvalidOperationException, KeyNotFoundException } from '../runtime/Errors.js';
const resolve=name=>name.startsWith('GTE.')?api.Gte[name.slice(4)]:api[name.replace(/^netDxf\./,'')];
const signatureName=name=>name==='DoubleFunction'?'System.Func<double, double>':name==='SortedDoubleInt'?'System.Collections.Generic.SortedDictionary<double, int>':name.endsWith('&')?signatureName(name.slice(0,-1))+'&':typeName(name);
const normalize=s=>s.replace(/\b(?:out|ref) ([^,]+(?:<[^>]*>)?(?:\[\])?)/g,'$1&').replace(/\s+/g,'');
export function gteWire(value) {
  if(value==null)return null;
  if(value instanceof api.Gte.GVector)return {type:'netDxf.GTE.GVector',data:Array.from(value.Vector,doubleBits)};
  if(value instanceof api.Gte.GMatrix)return {type:'netDxf.GTE.GMatrix',rows:value.NumRows,cols:value.NumCols,data:Array.from(value.Elements.Vector,doubleBits)};
  if(value instanceof GteSortedDictionary)return Array.from(value,p=>({key:doubleBits(p.Key),value:p.Value}));
  if(typeof value==='object'&&value[Symbol.iterator])return Array.from(value,gteWire);
  if(typeof value==='object'&&Object.values(api.Gte).some(Type=>typeof Type==='function'&&value.constructor===Type))return {type:'netDxf.GTE.'+value.constructor.name};
  return wire(value);
}
export function gteCall(input,manifest) {
  api.Gte.GTE.UseRowMajor=input.rowMajor??true;
  const values=new Map(),trace=[];
  const ref=id=>{if(!values.has(id))throw new KeyNotFoundException();return values.get(id);};
  function read(value) {
    if(value==null||typeof value!=='object')return value;
    if('ref'in value)return ref(value.ref);
    if('cell'in value)return {value:Copy(ref(value.cell))};
    if('out'in value)return {value:null};
    if('int'in value)return value.int;
    if('short'in value)return value.short;
    if('double'in value)return fromBits(value.double);
    if('array'in value)return value.values.map(read);
    if('function'in value){const description=value.function,coefficients=description.coefficients.map(read);let calls=0;return x=>{trace.push({argument:doubleBits(x)});if(++calls===description.throwAt)throw new InvalidOperationException('Injected numerical callback failure.');let y=0;for(const c of coefficients)y=y*x+c;return y;};}
    if('new'in value){const Type=resolve(value.new),args=(value.args??[]).map(read);return value.signature&&Type.CreateOverload?Type.CreateOverload(value.signature.map(signatureName).join(','),...args):new Type(...args);}
    throw new Error('Unknown GTE input descriptor.');
  }
  function method(Type,step,target,args){
    const contract=manifest.files.flatMap(f=>f.types).find(t=>t.name==='netDxf.GTE.'+Type.name);
    const signature=step.signature?.map(signatureName).join(',');
    const matches=contract?.members.filter(m=>m.name===step.member&&m.isStatic===!target&&m.implementation&&(!signature||normalize(m.signature)===normalize(signature)));
    const name=matches?.length===1?matches[0].implementation:step.member;
    if(typeof (target??Type)[name]!=='function')throw new InvalidOperationException('Unbound GTE method.');
    return (target??Type)[name](...args);
  }
  return input.steps.map(step=>{
    try {
      const target=step.target?ref(step.target):null,Type=step.type?resolve(step.type):target?.constructor,args=(step.args??[]).map(read);let result=null,outputs=[];
      switch(step.kind){
        case 'new':case 'value':result=read(step.value);break;
        case 'get':result=Copy((target??Type)[step.member]);break;
        case 'set':(target??Type)[step.member]=Copy(read(step.value));break;
        case 'index':result=target.get_Item?target.get_Item(...args):Copy(target[args[0]]);break;
        case 'set-index':target.set_Item?target.set_Item(...args,read(step.value)):target[args[0]]=Copy(read(step.value));break;
        case 'call':result=method(Type,step,target,args);for(let j=0;j<args.length;j++)if(step.args[j]?.out||step.args[j]?.cell){values.set(step.args[j].out??step.args[j].cell,args[j].value);outputs.push(gteWire(args[j].value));}break;
        case 'snapshot':result=target;break;
        case 'trace':return {ok:true,value:structuredClone(trace),outputs:[]};
        default:throw new Error('Unknown GTE observation kind.');
      }
      if(step.id)values.set(step.id,result);return {ok:true,value:gteWire(result),outputs};
    }catch(error){return {ok:false,error:error.name,param:error.ParamName??null};}
  });
}
