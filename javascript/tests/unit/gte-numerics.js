import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import * as api from '../../index.js';
import { GteArray, GteCopyTo, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';
import { gteCorpus } from '../../tools/gte-corpus.mjs';
import { gteCall } from '../../tools/gte-wire.mjs';
import { validateGteObservation } from '../../tools/gte-observation.mjs';
import { GteOracleSession } from '../../tools/GteOracleSession.mjs';
const g = api.Gte;
const manifest = JSON.parse(fs.readFileSync(new URL('../../gte-port-manifest.json',import.meta.url),'utf8'));
const controls = () => [0,1,2,3].map(x=>new api.Vector3(x,0,0));
test('GTE namespace preserves both BezierCurve identities and standalone paths',async()=>{
  assert.notEqual(api.BezierCurve,g.BezierCurve); assert.equal(api.GteBezierCurve,g.BezierCurve);
  for(const name of ['GMatrix','GVector','BSplineCurve','NURBSCurve','BSplineCurveFit','BSplineSurfaceFit','BSplineReduction']) {
    const module=await import(`../../netDxf/GTE/${name}.js`);assert.equal(module[name],g[name]);
  }
});
for(const rowMajor of [true,false]) {
  test('matrix overloaded indexers agree in '+(rowMajor?'row':'column')+' major storage',()=>{
    const previous=g.GTE.UseRowMajor;try{g.GTE.UseRowMajor=rowMajor;
      const matrix=new g.GMatrix(2,3,[1,2,3,4,5,6]); matrix.set_Item(1,2,19);
      assert.equal(matrix.get_Item(1,2),19);assert.equal(matrix.get_Item(rowMajor?5:5),19);
      matrix.set_Item(0,1,31);assert.equal(matrix.get_Item(rowMajor?1:2),31);
      matrix.set_Item(0,41);assert.equal(matrix.get_Item(0,0),41);
      assert.throws(()=>matrix.get_Item(),{name:'ArgumentException'});
    }finally{g.GTE.UseRowMajor=previous;}
  });
  test('matrix arithmetic matches a hand computed product in '+rowMajor+' layout',()=>{
    const previous=g.GTE.UseRowMajor;try{g.GTE.UseRowMajor=rowMajor;
      const a=new g.GMatrix(2,2),b=new g.GMatrix(2,2);
      for(let row=0;row<2;row++)for(let col=0;col<2;col++){a.set_Item(row,col,1+row*2+col);b.set_Item(row,col,5+row*2+col);}
      const result=g.GMatrix.op_Multiply(a,b);
      assert.deepEqual([result.get_Item(0,0),result.get_Item(0,1),result.get_Item(1,0),result.get_Item(1,1)],[19,22,43,50]);
      result.set_Item(0,0,99);assert.equal(a.get_Item(0,0),1);
      const rows={value:0},cols={value:0};result.GetSize(rows,cols);assert.equal(rows.value,2);assert.equal(cols.value,2);
    }finally{g.GTE.UseRowMajor=previous;}
  });
}
test('vector constructors copy inputs and unit mutations keep fixed size',()=>{
  const input=[1,2,3],v=new g.GVector(input);input[0]=99;assert.deepEqual(Array.from(v.Vector),[1,2,3]);
  v.MakeUnit(1);assert.deepEqual(Array.from(v.Vector),[0,1,0]);v.MakeZero();assert.deepEqual(Array.from(v.Vector),[0,0,0]);
  assert.throws(()=>v.set_Item(3,7),{name:'IndexOutOfRangeException'});
});
test('fixed array allocation, overlapping copy and captured ref cells keep value semantics',()=>{
  assert.throws(()=>GteArray(-1),{name:'OverflowException'});
  const values=GteArray(4,(_,i)=>i);GteCopyTo([1,2,3],values,1);assert.deepEqual(Array.from(values),[0,1,2,3]);
  const point=new api.Vector3(1,2,3),dest=GteArray(1,()=>api.Vector3.Zero);GteCopyTo([point],dest,0);point.X=5;assert.equal(dest[0].X,1);
  const cell=GteElementRef(values,[1],false);cell.value=9;assert.equal(values[1],9);assert.equal(cell.value,9);
  assert.throws(()=>GteCopyTo([1],[],0),{name:'ArgumentException',ParamName:'destinationArray'});
});
test('sorted numerical dictionary retains NaN and signed-zero equality and duplicate parameter',()=>{
  const dictionary=new GteSortedDictionary();dictionary.Add(2,1);dictionary.Add(NaN,3);dictionary.Add(-0,4);
  assert.deepEqual(Array.from(dictionary,p=>p.Value),[3,4,1]);assert.equal(dictionary.get_Item(0),4);
  for(const key of [NaN,0,2])assert.throws(()=>dictionary.Add(key,7),{name:'ArgumentException',ParamName:null});
  const iterator=dictionary[Symbol.iterator]();iterator.next();dictionary.set_Item(2,2);assert.throws(()=>iterator.next(),{name:'InvalidOperationException'});
});
test('polynomial out parameters return sorted roots and multiplicity without sharing later results',()=>{
  const first={value:null},second={value:null};g.RootsPolynomial.SolveQuadratic(-1,0,1,first);g.RootsPolynomial.SolveQuadratic(1,-2,1,second);
  assert.deepEqual(Array.from(first.value,p=>[p.Key,p.Value]),[[-1,1],[1,1]]);assert.deepEqual(Array.from(second.value,p=>[p.Key,p.Value]),[[1,2]]);
  assert.notEqual(first.value,second.value);
});
test('Bezier jets preserve analytic derivatives and constructor control copies',()=>{
  const input=controls(),curve=new g.BezierCurve(input,3);input[0].X=99;const jet={value:null};curve.Evaluate(.5,3,jet);
  assert.deepEqual(jet.value.map(v=>v.X),[1.5,3,0,0]);jet.value[0].X=88;curve.Evaluate(.5,1,jet);assert.equal(jet.value[0].X,1.5);
});
test('B-spline and unit-weight NURBS evaluations preserve straight-line endpoints',()=>{
  const input=new g.BasisFunctionInput(4,3),a=new g.BSplineCurve(input,controls()),b=new g.NURBSCurve(input,controls(),[1,1,1,1]);
  for(const t of [0,.25,.5,.75,1]) {const x={value:null},y={value:null};a.Evaluate(t,1,x);b.Evaluate(t,1,y);assert.equal(x.value[0].X,3*t);assert.equal(y.value[0].X,3*t);}
});
test('integration uses actual function callbacks and propagates caller exceptions',()=>{
  let calls=0;assert.equal(g.Integration.TrapezoidRule(5,0,1,x=>{calls++;return x;}),.5);assert.equal(calls,5);
  const failure=new Error('callback');assert.throws(()=>g.Integration.Romberg(3,0,1,()=>{throw failure;}),e=>e===failure);
});
test('first and last list adapters preserve cleanup and empty-sequence failures',()=>{
  let closed=0;const values={*[Symbol.iterator](){try{yield 1;yield 2;}finally{closed++;}}};assert.equal(GteFirst(values),1);assert.equal(closed,1);assert.equal(GteLast(values),2);assert.equal(closed,2);
  assert.throws(()=>GteFirst([]),{name:'InvalidOperationException'});assert.throws(()=>GteLast([]),{name:'InvalidOperationException'});
});
test('GTE corpus has deterministic distinct inputs and preserves source-failure probes',()=>{
  const corpus=gteCorpus();assert.deepEqual(corpus,gteCorpus());assert.equal(corpus.length,401);assert.equal(new Set(corpus.map(p=>p.name)).size,401);
  assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),5435);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
  assert.equal(corpus.filter(p=>p.category==='source-failures').length,3);assert.equal(manifest.files.length,17);
});
test('GTE observation adapter resolves namespace-qualified matrix and curve cases',()=>{
  for(const probe of gteCorpus().filter(p=>p.category==='vector-storage'||p.category==='curves').slice(0,6))
    assert.doesNotThrow(()=>validateGteObservation(probe.request,gteCall(probe.request,manifest)));
});
const request={op:'gte',steps:[{kind:'call',args:[{out:'result'}]}]};
for(const invalid of [null,[],[{ok:true}],[{ok:true,value:null,outputs:[]}],[{ok:false,error:'Error'}],[{ok:false,error:'',param:null}]])
  test('strict GTE observation rejects malformed envelopes '+JSON.stringify(invalid),async()=>{
    let factories=0,closed=0;const session=new GteOracleSession(()=>{const index=++factories;return {request:async()=>index===1?invalid:[{ok:false,error:'ArgumentException',param:null}],close:async()=>{closed++;}};});
    assert.equal((await session.observe(request)).ok,false);const second=await session.observe(request);assert.equal(second.ok,true);assert.equal(second.value[0].error,'ArgumentException');await session.close();assert.equal(factories,2);assert.equal(closed,2);
  });
test('GTE source process failures remain unavailable instead of synthesized error equality',async()=>{
  const session=new GteOracleSession(()=>({request:async()=>{throw new Error('Stack overflow');},close:async()=>{}}));
  const result=await session.observe(request);assert.equal(result.ok,false);assert.match(result.failure.message,/Stack overflow/);assert.equal(Object.hasOwn(result,'value'),false);await session.close();
});
