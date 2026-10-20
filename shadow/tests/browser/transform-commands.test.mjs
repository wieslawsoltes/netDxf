import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CommandSession, point } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/core.mjs';

function start(name,base='10,20,3') { const s=new CommandSession(name,'0',2);s.input(base);return s; }
function unchanged(s,action) { const before=JSON.stringify(s);assert.throws(action);assert.equal(JSON.stringify(s),before); }

test('ROTATE and SCALE retain their direct numeric, default and selection contracts',()=>{
  for(const [name,value] of [['ROTATE',90],['SCALE',2]]) {
    const s=new CommandSession(name);assert.throws(()=>s.input('0,0'));s.acceptSelection(2);s.input('0,0');s.input('');
    assert.equal(s.command.value,value);assert(s.complete);assert.equal(s.command.transform,undefined);
  }
  for(const value of [0,360,-360]) { const s=start('ROTATE');s.input(String(value));assert.equal(s.command.value,value);assert(s.complete); }
});
test('Copy combines with direct or reference transforms without completing early',()=>{
  for(const name of ['ROTATE','SCALE']) {
    const s=start(name);s.input('C');s.input('Copy');assert(!s.complete);assert.match(s.prompt,/Copy/);
    assert.deepEqual(s.command.transform,{copy:true});s.input(name==='ROTATE'?'45':'3');assert(s.complete);
    const r=start(name);r.input('Reference');r.input(name==='ROTATE'?'30':'5');r.input('Copy');r.input(name==='ROTATE'?'90':'20');
    assert.deepEqual(r.command.transform,{copy:true,reference:{value:name==='ROTATE'?30:5},target:{value:name==='ROTATE'?90:20}});
  }
});
test('reference and target point acquisition use independent anchors including the base point',()=>{
  const s=start('ROTATE');s.input('R');s.pick(point(10,20,3));assert.deepEqual(s.anchor,point(10,20,3));
  s.input('@10,10');assert.deepEqual(s.command.points,[point(10,20,3)]);assert(!s.complete);
  s.input('P');s.pick(point(100,100,7));assert.deepEqual(s.anchor,point(100,100,7));assert.deepEqual(s.previewPoints,[point(100,100,7)]);
  s.input('@0,10,0');assert(s.complete);
  assert.deepEqual(s.command.transform.reference.points,[point(10,20,3),point(20,30,3)]);
  assert.deepEqual(s.command.transform.target.points,[point(100,100,7),point(100,110,7)]);
});
test('new-angle point uses the rotation base, not the last reference endpoint',()=>{
  const s=start('ROTATE');s.input('R');s.pick(point(100,100));s.pick(point(110,110));
  assert.deepEqual(s.anchor,point(10,20,3));s.pick(point(10,25,3));
  assert.deepEqual(s.command.transform.target.points,[point(10,20,3),point(10,25,3)]);
});
test('SCALE reference captures 3D lengths and accepts typed target or two target points',()=>{
  const s=start('SCALE');s.input('R');s.input('0,0,0');s.input('0,3,4');s.input('10');
  assert.deepEqual(s.command.transform,{reference:{points:[point(),point(0,3,4)]},target:{value:10}});
  const t=start('SCALE');t.input('R');t.input('5');t.input('Points');t.input('100,100,10');t.input('@0,0,20');
  assert.deepEqual(t.command.transform.target.points,[point(100,100,10),point(100,100,30)]);
});
test('direct picked scale measures base-to-point and rotation rejects a vertical-only vector',()=>{
  const s=start('SCALE');s.pick(point(10,23,7));assert.equal(s.command.value,5);
  const r=start('ROTATE');unchanged(r,()=>r.pick(point(10,20,99)));r.pick(point(10,30,99));assert.equal(r.command.value,90);
});
test('invalid reference inputs and rejected ratios leave the complete acquisition receipt unchanged',()=>{
  const s=start('SCALE');s.input('R');
  for(const value of ['0','-1','NaN','Infinity','1e13'])unchanged(s,()=>s.input(value));
  s.pick(point(2,3,4));unchanged(s,()=>s.pick(point(2,3,4)));s.pick(point(2,3,5));
  for(const value of ['0','-1','1e-10'])unchanged(s,()=>s.input(value));
  const ratio=start('SCALE');ratio.input('R');ratio.input('1e-9');unchanged(ratio,()=>ratio.input('1e12'));
  ratio.input('1');assert(ratio.complete);
  const long=start('ROTATE');long.input('R');long.pick(point(-1e12,0));unchanged(long,()=>long.pick(point(1e12,0)));
});
test('cancelled reference sessions cannot leak options or picked points into a replacement command',()=>{
  const cancelled=start('ROTATE');cancelled.input('C');cancelled.input('R');cancelled.pick(point(50,60));
  const next=start('ROTATE');assert.equal(next.command.transform,undefined);assert.match(next.prompt,/Copy\/Reference/);
  assert.deepEqual(next.previewPoints,[point(10,20,3)]);next.input('30');assert(next.complete);assert(!cancelled.complete);
});
test('reference defaults and zero-degree targets are valid while zero scale is refused',()=>{
  const r=start('ROTATE');r.input('R');r.input('');r.input('0');assert.deepEqual(r.command.transform,{reference:{value:0},target:{value:0}});
  const s=start('SCALE');s.input('R');s.input('');s.input('');assert.deepEqual(s.command.transform,{reference:{value:1},target:{value:1}});
});
