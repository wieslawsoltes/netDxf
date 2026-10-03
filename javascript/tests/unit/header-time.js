import test from 'node:test';
import assert from 'node:assert/strict';
import {userInfo} from 'node:os';
import {HeaderVariables,HeaderVariable,HeaderDateTime,HeaderTimeSpan,HeaderEnum,SetHeaderEnvironment,
  AciColor,UCS,Vector3,LinearUnitType,DrawingUnits,DxfVersion,HeaderVariableCode} from '../../index.js';
import {GetHeaderEnvironment} from '../../runtime/HeaderTime.js';
import {BoxedScalar} from '../../runtime/BoxedScalar.js';
import {Copy,Format} from '../../runtime/GeometryRuntime.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidCastException,
  NullReferenceException,NotSupportedException} from '../../runtime/Errors.js';
const fixed=Object.freeze({UserName:()=> 'Test user',Now:()=>new HeaderDateTime(638000000000000123n,2),UtcNow:()=>new HeaderDateTime(638000000000000124n,1)});
const make=()=>new HeaderVariables(fixed);
const entry=(h,name)=>[...h.KnownValues()].find(v=>v.Name===HeaderVariableCode[name]);

test('header environment injection calls the four independent clocks in source order',()=>{
  const calls=[],env={UserName(){calls.push('user');return 'Named';},Now(){calls.push('local');return fixed.Now();},UtcNow(){calls.push('utc');return fixed.UtcNow();}};
  const h=new HeaderVariables(env);assert.deepEqual(calls,['user','local','utc','local','utc']);assert.equal(h.LastSavedBy,'Named');
  assert.equal(h.TdCreate.Kind,2);assert.equal(h.TduCreate.Kind,1);assert.equal(h.TdinDwg.Ticks,0n);
});
test('header host installation is reversible and does not mutate existing instances',()=>{
  const old=GetHeaderEnvironment();try{SetHeaderEnvironment(fixed);const h=new HeaderVariables();assert.equal(h.LastSavedBy,'Test user');
    assert.throws(()=>SetHeaderEnvironment({}),ArgumentException);assert.equal(new HeaderVariables().LastSavedBy,'Test user');
    SetHeaderEnvironment(null);assert.equal(new HeaderVariables().LastSavedBy,'');assert.equal(h.LastSavedBy,'Test user');
  }finally{SetHeaderEnvironment(old);}
});
test('explicit Node entry supplies the real host username and UTC time within local clock bounds',async()=>{
  await import('../../node-entry.js');const before=Date.now(),h=new HeaderVariables(),after=Date.now();
  assert.equal(h.LastSavedBy,userInfo().username);assert.equal(h.TduCreate.Kind,1);assert.equal(h.TdCreate.Kind,2);
  const ms=Number((h.TduCreate.Ticks-621355968000000000n)/10000n);assert.ok(ms>=before&&ms<=after);
});
test('header timestamps retain all 100 ns ticks and kind without mutable aliases',()=>{
  const h=make(),date=new HeaderDateTime(638000000000000123n,2);h.TdCreate=date;assert.equal(h.TdCreate.Ticks,638000000000000123n);assert.equal(h.TdCreate.Kind,2);
  assert.equal(Copy(date),date);assert.throws(()=>{date.Ticks=0n;},TypeError);assert.equal(h.TdCreate.Ticks,date.Ticks);
});
test('header date adapters decompose CLR minimum maximum and Unix boundary ticks exactly',()=>{
  assert.deepEqual(HeaderDateTime.MinValue.Calendar,[1,1,1,0,0,0,0]);assert.deepEqual(HeaderDateTime.MaxValue.Calendar,[9999,12,31,23,59,59,999]);
  assert.deepEqual(new HeaderDateTime(621355967999999999n).Calendar,[1969,12,31,23,59,59,999]);
  assert.deepEqual(new HeaderDateTime(621355968000000000n).Calendar,[1970,1,1,0,0,0,0]);
  assert.equal(HeaderDateTime.MaxValue.Ticks,3155378975999999999n);
});
test('header date adapters reject invalid ticks kind and Date inputs without truncating values',()=>{
  for(const n of [-1n,3155378976000000000n])assert.throws(()=>new HeaderDateTime(n),ArgumentOutOfRangeException);
  assert.throws(()=>new HeaderDateTime(0),ArgumentException);assert.throws(()=>new HeaderDateTime(0n,3),ArgumentOutOfRangeException);
  assert.throws(()=>HeaderDateTime.FromDate(new Date(NaN)),ArgumentException);
  assert.equal(HeaderDateTime.FromDate(new Date('2026-01-01T00:00:00.123Z')).Millisecond,123);
});
test('header date value equality follows DateTime ticks and ignores the Kind field',()=>{
  const a=new HeaderDateTime(123n,0),b=new HeaderDateTime(123n,2);assert.ok(a.Equals(b));assert.equal(a.Equals(new HeaderDateTime(124n)),false);
});
test('header durations retain signed Int64 endpoints without a floating-point intermediary',()=>{
  for(const n of [-9223372036854775808n,-1n,0n,1n,9223372036854775807n]){const s=new HeaderTimeSpan(n),h=make();h.TdinDwg=s;assert.equal(h.TdinDwg.Ticks,n);assert.ok(s.Equals(new HeaderTimeSpan(n)));}
  assert.throws(()=>new HeaderTimeSpan(9223372036854775808n),ArgumentOutOfRangeException);
});
test('invariant header time formatting retains fractional ticks and correct negative day components',()=>{
  assert.equal(new HeaderTimeSpan(-1n).ToString(),'-00:00:00.0000001');assert.equal(new HeaderTimeSpan(864000000001n).ToString(),'1.00:00:00.0000001');
  assert.equal(HeaderDateTime.MinValue.ToString(),'01/01/0001 00:00:00');assert.equal(HeaderDateTime.MaxValue.ToString(),'12/31/9999 23:59:59');
});
test('boxed scalar and boolean formatting now follows invariant CLR output in HeaderVariable',()=>{
  assert.equal(new HeaderVariable('$S',70,new BoxedScalar('Int16',4)).ToString(),'$S:4');
  assert.equal(new HeaderVariable('$B',290,false).ToString(),'$B:False');assert.equal(Format('{0}/{1}',true,false),'True/False');
  assert.equal(new HeaderVariable('$L',160,new BoxedScalar('Int64',9007199254740993n)).ToString(),'$L:9007199254740993');
});
test('header known enum formatting preserves names while undefined values remain numeric',()=>{
  const h=make();assert.equal(entry(h,'AcadVer').ToString(),'$ACADVER:AutoCad2000');h.AcadVer=2147483647;
  assert.equal(entry(h,'AcadVer').ToString(),'$ACADVER:2147483647');assert.equal(entry(h,'AUprec').ToString(),'$AUPREC:0');
});
test('header comparison corpus is deterministic and contains no embedded expected model values',async()=>{
  const {headerCorpus}=await import('../../tools/header-corpus.mjs');const a=headerCorpus();assert.deepEqual(a,headerCorpus());
  assert.equal(a.length,665);assert.equal(new Set(a.map(x=>x.name)).size,665);assert.equal(a.reduce((n,p)=>n+p.request.steps.length,0),6596);
  assert.ok(a.every(p=>!Object.hasOwn(p,'expected')));
});
