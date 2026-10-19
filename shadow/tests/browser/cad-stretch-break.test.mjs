import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CommandSession, commandName, point } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/core.mjs';

const window = s => {s.input('15,5');s.input('5,-5');s.acceptSelection(1);};
test('new modify aliases resolve to their full command contracts',()=>{
  for(const [alias,name] of [['S','STRETCH'],['BR','BREAK'],['BRP','BREAKATPOINT']])assert.equal(commandName(alias),name);
});
test('STRETCH requires an explicit window even with preselected objects',()=>{
  const s=new CommandSession('S','0',4);
  assert.equal(s.step.kind,'window');assert.equal(s.selecting,false);assert.throws(()=>s.acceptSelection(4),/crossing window/);
  s.input('15,5,7');assert.equal(s.anchor,null);s.input('@-10,-10');
  assert.deepEqual(s.command.points,[point(15,5),point(5,-5)]);assert(s.selecting);
  assert.throws(()=>s.input('0,0'),/not accepting/);assert.throws(()=>s.acceptSelection(0),/at least 1/);
  s.acceptSelection(1);assert.equal(s.index,2);assert.equal(s.anchor,null);
  s.input('0,0');s.input('@5,3');assert(s.complete);
  assert.deepEqual(s.command.points,[point(15,5),point(5,-5),point(),point(5,3)]);
});
test('STRETCH rectangle validation does not consume the next prompt',()=>{
  const s=new CommandSession('S');s.input('10,10');const version=s.version;
  for(const p of [point(10,20),point(20,10),point(Infinity,20)])assert.throws(()=>s.pick(p));
  assert.equal(s.version,version);assert.equal(s.index,1);assert.deepEqual(s.command.points,[point(10,10)]);
});
test('STRETCH can replace the crossing window before confirmation',()=>{
  const s=new CommandSession('S');s.captureWindow(point(20,10),point(5,-5));s.captureWindow(point(12,5),point(8,-5));
  assert.deepEqual(s.command.points,[point(12,5),point(8,-5)]);assert(s.selecting);
  s.resetWindow();assert.equal(s.step.kind,'window');assert.equal(s.selecting,false);assert.deepEqual(s.command.points,[]);
});
test('STRETCH displacement preserves its crossing bounds',()=>{
  for(const keyword of ['D','Displacement','']){
    const s=new CommandSession('S');window(s);s.input(keyword);s.input('4,3');assert(s.complete);
    assert.deepEqual(s.command.points,[point(15,5),point(5,-5),point(),point(4,3)]);
  }
  const s=new CommandSession('S');window(s);s.input('4,3');s.input('');
  assert.deepEqual(s.command.points,[point(15,5),point(5,-5),point(),point(4,3)]);
});
test('STRETCH refuses zero displacement and cannot redefine bounds during movement',()=>{
  const s=new CommandSession('S');window(s);s.input('0,0');
  assert.throws(()=>s.input(''),/nonzero/);assert.throws(()=>s.input('0,0'),/distinct/);
  assert.throws(()=>s.captureWindow(point(),point(1,1)),/not acquiring/);assert.equal(s.index,3);
});
test('BREAK uses the pointer selection point as its first break, with First override',()=>{
  const s=new CommandSession('BR');assert(s.selecting);s.acceptPickedSelection(1,point(7,2));
  assert.equal(s.index,1);assert.deepEqual(s.command.points,[point(7,2)]);
  s.input('F');assert.equal(s.index,0);assert.deepEqual(s.command.points,[]);
  s.input('3,0');s.input('8,0');assert(s.complete);assert.deepEqual(s.command.points,[point(3,0),point(8,0)]);
});
test('BREAK preselection asks both points and @ requests a gap-free split',()=>{
  const s=new CommandSession('BREAK','0',1);assert(!s.selecting);assert.equal(s.index,0);
  s.input('5,0');s.input('@');assert(s.complete);assert.deepEqual(s.command.points,[point(5,0),point(5,0)]);
});
test('BREAK rejects invalid selection and nonfinite pick without advancing',()=>{
  const s=new CommandSession('BREAK');assert.throws(()=>s.acceptPickedSelection(2,point()),/exactly 1/);
  assert.throws(()=>s.acceptPickedSelection(1,point(NaN,0)),/finite/);assert(s.selecting);assert.equal(s.version,0);
});
test('BREAKATPOINT separates the selection pick from the break point',()=>{
  const s=new CommandSession('BRP');s.acceptPickedSelection(1,point(9,1));
  assert.equal(s.index,0);assert.deepEqual(s.command.points,[]);s.input('5,0');assert(s.complete);
  assert.deepEqual(s.command.points,[point(5,0)]);
});
