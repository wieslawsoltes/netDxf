import {test} from 'node:test';
import assert from 'node:assert/strict';
import {CommandSession} from '../../samples/netDxf.Shadow.Studio/wwwroot/cad/core.mjs';
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
