import test from 'node:test';
import assert from 'node:assert/strict';
import {ModelBoxing} from '../../tools/model-boxing.mjs';
import {Leader,Vector2,BoxedBoolean,BoxedString,DimensionStyleOverride,DimensionStyleOverrideType as O,HeaderVariable} from '../../index.js';
import {BoxedScalar} from '../../runtime/BoxedScalar.js';
import {wire} from '../../tools/model-wire.mjs';
import {ArgumentException} from '../../runtime/Errors.js';

test('object-valued inputs retain reference identity while independent equal literals allocate fresh boxes',()=>{
 const values=new Map([['number',2],['bool',false],['text','same']]),boxer=new ModelBoxing(values);
 for(const [key,descriptor]of [['number',{double:'4000000000000000'}],['bool',false],['text','same']]){
  boxer.Register(key,descriptor);const value=values.get(key),ref={ref:key},a=boxer.Read(ref,value);
  assert.equal(boxer.Read(ref,value),a);assert.notEqual(boxer.Read(descriptor,value),a);
  values.set(key,value);boxer.Register(key,descriptor);assert.notEqual(boxer.Read(ref,value),a);
 }
});
test('CLR string empty identity is shared but nonempty adapters remain distinct references',()=>{
 assert.equal(new BoxedString(''),new BoxedString(''));const a=new BoxedString('same'),b=new BoxedString('same');assert.notEqual(a,b);assert.ok(a.Equals(b));assert.equal(a.Clone(),a);
 assert.throws(()=>new BoxedString(null),ArgumentException);assert.throws(()=>new BoxedBoolean(0),ArgumentException);assert.equal(new BoxedBoolean(false).ToString(),'False');
});
test('typed object boxes keep Int16 and enum input type metadata rather than inferring from numeric value',()=>{
 const boxer=new ModelBoxing(new Map()),precision=boxer.Read({short:4},4),wrong=boxer.Read({int:4},4);
 assert.equal(precision.Type,'Int16');assert.equal(wrong.Type,'Int32');assert.equal(new DimensionStyleOverride(O.LengthPrecision,precision).Value,precision);assert.throws(()=>new DimensionStyleOverride(O.LengthPrecision,wrong),ArgumentException);
 const e=boxer.Read({enum:'Tables.DimensionStyleTextVerticalPlacement',value:1},1);assert.equal(e.Type,'DimensionStyleTextVerticalPlacement');assert.equal(e.Value,1);
});
test('object-valued observation emits bool and string payloads without leaking JavaScript adapter names',()=>{
 for(const [value,type,payload]of [[new BoxedBoolean(false),'Boolean',false],[new BoxedString('text'),'String','text']]){
  const observed=wire(new HeaderVariable('$APP',1,value));assert.equal(observed.valueType,type);assert.equal(observed.value,payload);
 }
 const shared=new BoxedScalar('Double',2),boxer=new ModelBoxing(new Map([['a',shared]]));boxer.Register('a',null);assert.equal(boxer.Read({ref:'a'},shared),shared);
});

test('canonical empty string identity survives mixing primitive and explicit object representations',()=>{
 for(const [a,b]of [[new BoxedString(''),''],['',new BoxedString('')]]){
  const leader=new Leader([Vector2.Zero,Vector2.UnitX]),old=new DimensionStyleOverride(O.DimPrefix,a);
  leader.StyleOverrides.Add(old);leader.StyleOverrides.set_Item(O.DimPrefix,new DimensionStyleOverride(O.DimPrefix,b));assert.equal(leader.StyleOverrides.get_Item(O.DimPrefix),old);
 }
});
