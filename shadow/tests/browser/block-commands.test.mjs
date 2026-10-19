import {test} from 'node:test';
import assert from 'node:assert/strict';
import {CommandSession} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/core.mjs';
import {createInsertSession,insertOptions} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/block-contracts.mjs';
test('BLOCK captures ordered selection then base point and complete definition name',()=>{
 const s=new CommandSession('B');assert.equal(s.selecting,true);assert.throws(()=>s.acceptSelection(0));
 s.acceptSelection(2);s.input('10,20');s.input('Valve DN 50');assert.equal(s.complete,true);
 assert.equal(s.command.name,'BLOCK');assert.equal(s.command.text,'Valve DN 50');assert.deepEqual(s.command.points,[{x:10,y:20,z:0}]);
});
test('preselected BLOCK does not complete before a valid base point and name',()=>{
 const s=new CommandSession('BLOCK','Equipment',2);assert.equal(s.selecting,false);s.acceptSelection(2);
 assert.throws(()=>s.input('NaN,0'));assert.equal(s.command.points.length,0);s.input('0,0');
 assert.throws(()=>s.input(''));assert.equal(s.complete,false);s.input('Part');assert.equal(s.command.layer,'Equipment');
});

test('name-first and coordinate-first BLOCK sessions do not change shared prompts',()=>{
 const first=new CommandSession('BLOCK','0',1);first.input('10,20');first.input('Valve');
 const second=new CommandSession('BLOCK','0',1);assert.equal(second.step.kind,'text');
 second.input('Valve');second.input('10,20');assert.deepEqual(second.command,first.command);
});
test('malformed coordinate-first input preserves the complete command receipt',()=>{
 const s=new CommandSession('BLOCK','0',1), before=JSON.stringify(s);
 assert.throws(()=>s.input('NaN,0'));assert.equal(JSON.stringify(s),before);
 s.input('10,20');const after=JSON.stringify(s);assert.throws(()=>s.input('bad/name'));assert.equal(JSON.stringify(s),after);
});
test('configured INSERT captures one point with XYZ, angle and alternate base options',()=>{
 const options=insertOptions({id:'H:1',name:'Valve DN 50'},{scale:{x:2,y:-3,z:1},rotation:30,basePoint:{x:5,y:6},repeat:true});
 const s=createInsertSession(options,'Equipment');assert.equal(s.step.kind,'point');assert.match(s.prompt,/Escape ends/);
 s.pick({x:100,y:200,z:0});assert.equal(s.complete,true);
 assert.deepEqual(s.command,{name:'INSERT',points:[{x:100,y:200,z:0}],layer:'Equipment',block:'Valve DN 50',scale:{x:2,y:-3,z:1},rotation:30,basePoint:{x:5,y:6,z:0}});
 const next=createInsertSession(s.insertOptions,'Equipment');assert.equal(next.complete,false);assert.deepEqual(next.command.points,[]);
 next.pick({x:300,y:400,z:0});assert.deepEqual(s.command.points,[{x:100,y:200,z:0}]);
});
test('INSERT configuration rejects invalid scales and external definitions before placement',()=>{
 const block={id:'H:1',name:'Part'};
 for(const x of [0,NaN,Infinity,1e13,1e-12])assert.throws(()=>insertOptions(block,{scale:{x,y:1,z:1}}));
 assert.throws(()=>insertOptions({...block,isExternal:true}));assert.throws(()=>insertOptions(block,{basePoint:{x:NaN,y:0}}));
 assert.throws(()=>insertOptions(block,{rotation:Infinity}));
 assert.throws(()=>insertOptions({...block,scaleUniformly:true},{scale:{x:2,y:3,z:2}}));
 assert.deepEqual(insertOptions(block).scale,{x:1,y:1,z:1});
});
test('configured insertion does not change command-line INSERT prompts or external options',()=>{
 const options=insertOptions({id:'H:1',name:'Part'}),s=createInsertSession(options);
 options.scale.x=99;assert.equal(s.command.scale.x,1);
 s.pick({x:1,y:2});assert.equal(new CommandSession('INSERT').step.kind,'text');
 assert.equal(s.insertOptions.repeat,false);
});
