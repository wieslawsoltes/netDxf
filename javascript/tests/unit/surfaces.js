import test from 'node:test';
import assert from 'node:assert/strict';
import {BasisFunctionInput,UniqueKnot,BasisFunction,BSplineSurface,NURBSSurface,Vector3} from '../../index.js';
import {Copy} from '../../runtime/GeometryRuntime.js';
import {ArgumentException,IndexOutOfRangeException,NotSupportedException} from '../../runtime/Errors.js';
const points=()=>[new Vector3(),new Vector3(2,0,0),new Vector3(0,2,0),new Vector3(2,2,1)];
const xy=v=>[v.X,v.Y,v.Z];
const basis=()=>new BasisFunctionInput(2,1);
test('basis input struct copies share knot array references but UniqueKnot values copy',()=>{
 const input=new BasisFunctionInput(5,3),copy=Copy(input);copy.Degree=2;assert.equal(input.Degree,3);assert.equal(input.UniqueKnots,copy.UniqueKnots);
 const knot=Copy(input.UniqueKnots[0]);knot.T=9;assert.equal(input.UniqueKnots[0].T,0);copy.UniqueKnots[0].T=-1;assert.equal(input.UniqueKnots[0].T,-1);
});
test('basis creation owns new array and knot values; recreation replaces exposed arrays',()=>{
 const input=new BasisFunctionInput(4,2),b=new BasisFunction(input),old=b.Knots;input.UniqueKnots[0].T=-1;
 assert.equal(b.UniqueKnots[0].T,0);b.Create(input);assert.notEqual(b.Knots,old);assert.equal(old[0],0);assert.equal(b.Knots[0],-1);
});
test('cubic basis partition and derivative sums retain local support',()=>{
 const b=new BasisFunction(new BasisFunctionInput(7,3)),lo={},hi={};
 for(const t of [0,.125,.3,.5,.9,1]){b.Evaluate(t,3,lo,hi);for(let order=0;order<4;order++){let sum=0;for(let i=lo.value;i<=hi.value;i++)sum+=b.GetValue(order,i);assert.ok(Math.abs(sum-(order===0?1:0))<1e-12);}}
});
test('periodic basis wraps both directions before clamping',()=>{
 const input=new BasisFunctionInput();Object.assign(input,{NumControls:4,Degree:2,Periodic:true,Uniform:true,NumUniqueKnots:9,UniqueKnots:Array.from({length:9},(_,i)=>new UniqueKnot((i-2)/4,1))});
 const b=new BasisFunction(input),lo={},hi={};const sample=t=>{b.Evaluate(t,3,lo,hi);return [lo.value,hi.value,...Array.from({length:3},(_,d)=>Array.from({length:hi.value-lo.value+1},(_,i)=>b.GetValue(d,i+lo.value))).flat()];};
 assert.deepEqual(sample(-.75),sample(.25));assert.deepEqual(sample(2.25),sample(.25));assert.deepEqual(sample(1),sample(0));
});
test('bilinear surface values and first/mixed derivatives are exact',()=>{
 const s=new BSplineSurface(basis(),basis(),points()),out={};s.Evaluate(.5,.5,2,out);
 assert.deepEqual(out.value.map(xy),[[1,1,.25],[2,0,.5],[0,2,.5],[0,0,0],[0,0,1],[0,0,0]]);
 assert.equal(s.IsConstructed,true);assert.equal(s.IsIsRectangular,true);assert.equal(s.UMin,0);assert.equal(s.UMax,1);
});
test('surface constructors copy controls, allow partial/deferred data, reject oversized copies',()=>{
 const p=points(),s=new BSplineSurface(basis(),basis(),p);p[0].X=9;assert.equal(s.GetControl(0,0).X,0);
 const partial=new BSplineSurface(basis(),basis(),p.slice(0,1));assert.equal(partial.GetControl(1,1).X,0);
 assert.throws(()=>new BSplineSurface(basis(),basis(),[...p,new Vector3()]),{name:'ArgumentException',ParamName:'destinationArray'});
 assert.deepEqual(new BSplineSurface(basis(),basis(),null).Controls().map(xy),Array.from({length:4},()=>[0,0,0]));
});
test('surface array access exposes locations; GetControl returns copies; invalid pairs use element zero',()=>{
 const s=new BSplineSurface(basis(),basis(),points());s.Controls()[0].X=7;s.GetControl(0,0).X=20;s.SetControl(-1,0,new Vector3(99,99,99));
 assert.equal(s.GetControl(-1,9).X,7);assert.equal(s.GetControl(0,0).X,7);assert.throws(()=>{s.Controls().length=1;},NotSupportedException);
 assert.throws(()=>s.BasisFunction(-1),IndexOutOfRangeException);assert.throws(()=>s.NumControls(2),IndexOutOfRangeException);
});
test('surface domain metadata stays cached when callers edit exposed knots',()=>{
 const s=new BSplineSurface(basis(),basis(),points()),b=s.BasisFunction(0);b.Knots[0]=-2;assert.equal(s.UMin,0);assert.equal(b.MinDomain,0);assert.equal(b.UniqueKnots[0].T,0);
});
test('surface output jets are independent and unsupported high orders return zero vectors',()=>{
 const s=new BSplineSurface(basis(),basis(),points()),out={};s.Evaluate(.5,.5,2,out);const old=out.value;s.Evaluate(.1,.2,0,out);assert.notEqual(out.value,old);assert.deepEqual(xy(old[0]),[1,1,.25]);
 s.Evaluate(.5,.5,6,out);assert.deepEqual(out.value.map(xy),Array.from({length:6},()=>[0,0,0]));
});
test('rational and polynomial surfaces agree for unit weights including derivatives',()=>{
 const b=new BSplineSurface(basis(),basis(),points()),n=new NURBSSurface(basis(),basis(),points(),[1,1,1,1]),a={},c={};
 for(const [u,v] of [[0,0],[.5,.5],[1,1]]){b.Evaluate(u,v,2,a);n.Evaluate(u,v,2,c);assert.deepEqual(c.value.map(xy),a.value.map(xy));}
});
test('rational surfaces retain independent raw weight edits and do not invent unit defaults',()=>{
 const w=[1,2,3,4],n=new NURBSSurface(basis(),basis(),points(),w);w[0]=99;assert.equal(n.GetWeight(0,0),1);n.Weights[0]=5;assert.equal(n.GetWeight(-2,9),5);n.SetWeight(-1,0,6);assert.equal(n.Weights[0],5);
 const zero=new NURBSSurface(basis(),basis(),points(),null);assert.deepEqual([...zero.Weights],[0,0,0,0]);assert.ok(Number.isNaN(zero.GetPosition(.5,.5).X));
});
test('basis GetValue keeps original exception classes and named parameters',()=>{
 const b=new BasisFunction(new BasisFunctionInput(4,2));assert.throws(()=>b.GetValue(4,0),{name:'ArgumentException',ParamName:'order'});assert.throws(()=>b.GetValue(0,6),{name:'ArgumentException',ParamName:'i'});assert.throws(()=>b.GetValue(-1,0),IndexOutOfRangeException);
});
test('surface differential preserves unique scenarios and all comparison counts',async()=>{
 const {surfaceCorpus}=await import('../../tools/surface-corpus.mjs');const c=surfaceCorpus();assert.equal(c.length,1066);assert.equal(c.reduce((n,v)=>n+v.request.steps.length,0),17258);assert.equal(new Set(c.map(v=>v.name)).size,c.length);
});
