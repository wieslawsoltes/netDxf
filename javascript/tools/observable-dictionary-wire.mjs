// Independent input/observation adapter. Dictionary behavior comes only from production code.
import { ObservableDictionary } from '../netDxf/Collections/ObservableDictionary.js';
import { Vector2 } from '../netDxf/Vector2.js';
import { BoxedScalar } from '../runtime/BoxedScalar.js';
import { BoxedString } from '../runtime/BoxedString.js';
import { BoxedBoolean } from '../runtime/BoxedBoolean.js';
import { Copy } from '../runtime/GeometryRuntime.js';
import { DefaultValue, KeyValuePair, ValueEquals } from '../runtime/GenericDictionary.js';
import { OrdinalIgnoreCaseKey } from '../runtime/Collections.js';
import { InvalidOperationException, NotSupportedException } from '../runtime/Errors.js';
import { fromBits, doubleBits } from './wire.mjs';
class Probe {
  constructor(p) { this.id=p.id;this.group=p.group;this.hash=p.hash;this.reflexive=p.reflexive??true; }
  Equals(other) { return other instanceof Probe && this.reflexive && this.group===other.group; }
  GetHashCode() { return this.hash; }
}
const stringValue = value => value instanceof BoxedString ? value.Value : value;
export function observableDictionaryCall(input) {
  const pool=new Map(),keyType=input.keyType==='Vector2'?Vector2:input.keyType,valueType=input.valueType==='Vector2'?Vector2:input.valueType;
  function read(value,type=null) {
    if(value==null)return null;
    if(typeof value==='string')return new BoxedString(value);
    if(typeof value==='number')return type==='int'||type==='double'?value:new BoxedScalar('Double',value);
    if(typeof value==='boolean')return new BoxedBoolean(value);
    if('ref'in value)return Copy(pool.get(value.ref));
    if('int'in value)return type==='int'?value.int:new BoxedScalar('Int32',value.int);
    if('double'in value)return type==='double'?fromBits(value.double):new BoxedScalar('Double',fromBits(value.double));
    if('string'in value)return new BoxedString(value.string);
    if('vector2'in value)return new Vector2(...value.vector2);
    if('probe'in value)return new Probe(value.probe);
    throw new Error('Unknown dictionary input.');
  }
  for(const item of input.pool??[])pool.set(item.id,read(item.value));
  const key=op=>{const v=read(op.key,input.keyType);return input.keyType==='string'?stringValue(v):v;},value=op=>read(op.value,input.valueType);
  function wire(value,type=null) {
    if(value==null)return null;
    if(value instanceof Probe)return{id:value.id,group:value.group,hash:value.hash,reflexive:value.reflexive};
    if(value instanceof Vector2)return{vector2:[doubleBits(value.X),doubleBits(value.Y)]};
    if(value instanceof BoxedString||value instanceof BoxedBoolean)return value.Value;
    if(value instanceof BoxedScalar)return wire(value.Value,value.Type==='Double'?'double':'int');
    return typeof value==='number'&&type==='double'?{double:doubleBits(value)}:value;
  }
  const pair=p=>({key:wire(p.Key,input.keyType),value:wire(p.Value,input.valueType)});
  const calls=[],mode=input.comparer;
  const comparer=mode?{
    GetHashCode(item){calls.push('hash');if(mode==='throw-hash')throw new InvalidOperationException('injected');if(mode==='ignore-case'){let h=0;for(const c of OrdinalIgnoreCaseKey(stringValue(item)))h=(Math.imul(h,31)+c.codePointAt(0))|0;return h;}return mode==='modulo'?item%5:0;},
    Equals(a,b){calls.push('equals');if(mode==='throw-equals')throw new InvalidOperationException('injected');if(mode==='ignore-case')return OrdinalIgnoreCaseKey(stringValue(a))===OrdinalIgnoreCaseKey(stringValue(b));return mode==='modulo'?a%5===b%5:ValueEquals(a,b);}
  }:null;
  let dictionary;
  try {
    const capacity=['default','comparer'].includes(input.constructor)?0:(input.capacity??0);
    const activeComparer=['default','capacity'].includes(input.constructor)?null:comparer;
    dictionary=new ObservableDictionary(capacity,activeComparer,keyType,valueType);
  }catch(error){return{constructorError:error.name,param:error.ParamName??null};}
  const keys=dictionary.Keys,values=dictionary.Values,events=[];
  let currentOp={},reentered=false,eventMode='',iterator,iteratorKind='pairs';
  function hook(name,args) {
    events.push({name,item:pair(args.Item),count:dictionary.Count,cancel:args.Cancel});
    if(eventMode==='cancel-add'&&name==='before-add'||eventMode==='cancel-remove'&&name==='before-remove'||eventMode==='cancel-even-remove'&&name==='before-remove'&&args.Item.Key%2===0)args.Cancel=true;
    if(eventMode==='throw-'+name)throw new NotSupportedException('injected');
    if(!reentered&&currentOp.reenter?.event===name){reentered=true;operate(currentOp.reenter);}
  }
  for(const [member,name]of [['BeforeAddItem','before-add'],['AddItem','add'],['BeforeRemoveItem','before-remove'],['RemoveItem','remove']])dictionary[member].Add((_,args)=>hook(name,args));
  if(input.uncancel)for(const [member,name]of [['BeforeAddItem','last-before-add'],['BeforeRemoveItem','last-before-remove']])dictionary[member].Add((_,args)=>{events.push({name,item:pair(args.Item),count:dictionary.Count,cancel:args.Cancel});args.Cancel=false;});
  const current=()=>iteratorKind==='pairs'?pair(iterator.Current):wire(iterator.Current,iteratorKind==='keys'?input.keyType:input.valueType);
  function operate(op) {
    const view=op.view==='keys'?keys:op.view==='values'?values:dictionary;
    switch(op.method){
      case 'Add':dictionary.Add(key(op),value(op));break;
      case 'AddPair':dictionary.AddPair(KeyValuePair(key(op),value(op)));break;
      case 'Set':dictionary.set_Item(key(op),value(op));break;
      case 'Get':return wire(dictionary.get_Item(key(op)),input.valueType);
      case 'Remove':return dictionary.Remove(key(op));
      case 'RemovePair':return dictionary.RemovePair(KeyValuePair(key(op),value(op)));
      case 'ContainsPair':return dictionary.ContainsPair(KeyValuePair(key(op),value(op)));
      case 'ContainsKey':return dictionary.ContainsKey(key(op));
      case 'ContainsValue':return dictionary.ContainsValue(value(op));
      case 'TryGetValue':{const box={};const found=dictionary.TryGetValue(key(op),box);return{found,value:wire(box.value,input.valueType)};}
      case 'Clear':dictionary.Clear();break;
      case 'Enumerate':iteratorKind=op.view??'pairs';iterator=view.GetEnumerator();return current();
      case 'Current':return current();
      case 'MoveNext':return{moved:iterator.MoveNext(),current:current()};
      case 'Reset':iterator.Reset();return current();
      case 'Dispose':iterator.Dispose();break;
      case 'ViewAdd':view.Add(op.view==='keys'?key(op):value(op));break;
      case 'ViewRemove':return view.Remove(op.view==='keys'?key(op):value(op));
      case 'ViewClear':view.Clear();break;
      case 'ViewContains':return view.Contains(op.view==='keys'?key(op):value(op));
      case 'CopyTo':{
        const array=op.length===null?null:Array.from({length:op.length},()=>op.view==='keys'?DefaultValue(keyType):op.view==='values'?DefaultValue(valueType):KeyValuePair(DefaultValue(keyType),DefaultValue(valueType)));
        view.CopyTo(array,op.index);return array.map(item=>op.view==='keys'?wire(item,input.keyType):op.view==='values'?wire(item,input.valueType):pair(item));
      }
      case 'Mutate':{let target=pool.get(op.id);if(target instanceof Probe){if('hash'in op)target.hash=op.hash;if('group'in op)target.group=op.group;}else if(target instanceof Vector2){target=Copy(target);target.X=op.x;pool.set(op.id,target);}break;}
      default:throw new Error('Unknown dictionary operation '+op.method);
    }
    return null;
  }
  return input.steps.map(op=>{
    currentOp=op;reentered=false;events.length=0;calls.length=0;eventMode=op.mode??'';
    let result=null,error=null,param=null;
    try{result=operate(op);}catch(e){error=e.name;param=e.ParamName??null;}
    return{result,error,param,items:Array.from(dictionary,pair),keys:Array.from(keys,v=>wire(v,input.keyType)),values:Array.from(values,v=>wire(v,input.valueType)),count:dictionary.Count,readOnly:dictionary.IsReadOnly,keysReadOnly:keys.IsReadOnly,valuesReadOnly:values.IsReadOnly,events:events.slice(),comparerCalls:calls.slice()};
  });
}
