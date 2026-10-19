import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CommandSession, constrain, trackPoint, point } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/core.mjs';
import { CadViewport } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/viewport.mjs';
const near=(a,b)=>assert(Math.abs(a-b)<1e-8,`${a} != ${b}`);
test('MOVE supports displacement keyword and produces one detached source operation',()=>{
  const move=new CommandSession('MOVE','0',1);move.input('D');assert.equal(move.complete,false);move.input('25,-10,3');
  assert.deepEqual(move.command.points,[point(),point(25,-10,3)]);assert(move.complete);
});
test('MOVE Enter at second point uses first point as displacement from origin',()=>{
  const move=new CommandSession('M','0',2);move.input('12,15');move.input('');
  assert.deepEqual(move.command.points,[point(),point(12,15)]);assert(move.complete);
});
test('MOVE supports default displacement mode and rejects zero without consuming prompt',()=>{
  const move=new CommandSession('M','0',1);move.input('');const version=move.version;
  assert.throws(()=>move.input('0,0'));assert.equal(move.version,version);move.input('@5,0');assert(move.complete);
});
test('MOVE remains in selection phase until selected entities are confirmed',()=>{
  const move=new CommandSession('M');assert.throws(()=>move.input('D'));assert.equal(move.version,0);
  move.acceptSelection(1);move.input('D');move.input('10,0');assert(move.complete);
});
test('ortho preserves exact vertical/horizontal coordinates and anchor elevation',()=>{
  assert.deepEqual(constrain(point(9,30,99),point(4,5,8),{ortho:true}),point(4,30,8));
  assert.deepEqual(constrain(point(-40,9,99),point(4,5,8),{ortho:true}),point(-40,5,8));
});
test('temporary Shift reverses Ortho without changing the saved mode',()=>{
  const settings={ortho:false,temporaryOrtho:true};assert.deepEqual(constrain(point(10,3),point(),settings),point(10,0));assert.equal(settings.ortho,false);
  assert.deepEqual(constrain(point(10,3),point(),{ortho:true,temporaryOrtho:true}),point(10,3));
});
test('polar tracks configurable angles only inside acquisition aperture',()=>{
  const close=constrain(point(10,9),point(),{polar:true,polarAngle:45,polarTolerance:5});near(close.x,close.y);
  const free=point(10,5);assert.deepEqual(constrain(free,point(),{polar:true,polarAngle:45,polarTolerance:5}),free);
});
test('grid snap combines with exact Ortho and invalid persisted grid cannot create NaN',()=>{
  assert.deepEqual(constrain(point(14,23),point(3,4),{snap:true,gridStep:10,ortho:true}),point(3,20));
  assert.deepEqual(constrain(point(14,23),null,{snap:true,gridStep:Infinity}),point(10,20));
});
test('tracking resolves independent X/Y alignments from acquired points',()=>{
  const origins=[point(10,90),point(99,20)];const tracked=trackPoint(point(10.4,20.6),origins,1);
  assert.deepEqual(tracked.position,point(10,20));assert.deepEqual(tracked.origins,origins);
  assert.deepEqual(trackPoint(point(40,50),origins,1).position,point(40,50));
});
test('typed absolute coordinates override helper modes while direct distance uses the constrained cursor',()=>{
  const move=new CommandSession('M','0',1);move.input('10,10');move.input('5',constrain(point(30,13),move.anchor,{ortho:true}));
  assert.deepEqual(move.command.points,[point(10,10),point(15,10)]);
  const line=new CommandSession('L');line.input('0,0');line.input('#8,3',point(20,0));assert.deepEqual(line.anchor,point(8,3));
});
function view(read){return Object.assign(Object.create(CadViewport.prototype),{settings:{osnap:true,snapModes:23},client:{state:{},key:'a',epoch:0,busy:0,read},snapshot:null,camera:{scale:2},inputVersion:1,snapGeneration:0,invalidate(){}});}
test('one-shot NONE bypasses running object snaps and END works when running snaps are off',async()=>{
  let requests=0,mask;const v=view(async(_,o)=>{requests++;mask=o.body.modes;return{position:point(9,9)};});
  v.snapOverride=0;assert.deepEqual(await v.snapped(point(3,4),null,true),point(3,4));assert.equal(requests,0);
  v.snapOverride=1;v.settings.osnap=false;assert.deepEqual(await v.snapped(point(3,4),null,true),point(9,9));assert.equal(mask,1);
});
test('tracking retains only four distinct acquired snap points',async()=>{
  let p=point();const v=view(async()=>({position:p}));v.settings.tracking=true;
  for(let i=0;i<6;i++){p=point(i,i);await v.snapped(p,null,true);}assert.equal(v.trackingOrigins.length,4);
  await v.snapped(p,null,true);assert.equal(v.trackingOrigins.length,4);
});
