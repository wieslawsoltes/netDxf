import test from 'node:test';
import assert from 'node:assert/strict';
import {moduleRequest,moduleSession} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/module-contracts.mjs';
const command={id:'route',name:'Route',minimumPoints:2,maximumPoints:4,fields:[{id:'tag',name:'Tag',required:true,default:'L-101'}]};
const descriptor={id:'piping',name:'Piping',commands:[command]};
test('module requests retain exact version and detached unique selection',()=>{
  const insert={...command,requiresSymbol:true};const d={...descriptor,commands:[insert]};
  const result=moduleRequest(d,insert,{tag:'P-42'},{symbolId:'pump',version:'1.2',attributes:[]},['A','A','B']);
  assert.equal(result.command.symbolVersion,'1.2');assert.deepEqual(result.command.entities,[{value:'A'},{value:'B'}]);assert.equal(result.command.properties.tag,'P-42');
  assert.throws(()=>moduleRequest(d,insert),/symbol/);
});
test('required fields and advertised commands fail before acquisition',()=>{
  assert.throws(()=>moduleRequest(descriptor,command,{tag:''}),/required/);
  assert.throws(()=>moduleRequest(descriptor,{id:'unknown'}),/available/);
  assert.throws(()=>moduleRequest({...descriptor,commands:[{...command,requiresSelection:true}]},{...command,requiresSelection:true}),/Select/);
});
test('native block TAG shares the command tag field without duplicate prompts',()=>{
  const result=moduleRequest(descriptor,command,{tag:'P-101'},{symbolId:'pump',version:'1.0',attributes:[{id:'TAG',name:'Tag',required:true}]});
  assert.equal(result.command.properties.tag,'P-101');assert.deepEqual(Object.keys(result.command.properties),['tag']);
});
test('module routes reuse relative CAD coordinate acquisition and finish bounds',()=>{
  const request=moduleRequest(descriptor,command);const session=moduleSession(descriptor,command,request,'exact-view');
  session.input('10,20');assert.throws(()=>session.input(''),/at least/);
  session.input('@5,0');session.input('');
  assert.equal(session.complete,true);assert.deepEqual(session.command.points,[{x:10,y:20,z:0},{x:15,y:20,z:0}]);
  assert.equal(session.expectedKey,'exact-view');assert.deepEqual(request.command.points,[]);
});
test('module route rejects closure and extra points instead of silently changing semantics',()=>{
  const session=moduleSession(descriptor,command,moduleRequest(descriptor,command),'view');
  session.input('0,0');session.input('1,0');assert.throws(()=>session.input('C'),/open route/);
  session.input('2,0');session.input('3,0');assert.throws(()=>session.input('4,0'),/At most 4/);session.input('');assert.ok(session.complete);
});
test('single-point symbol placement completes without an extra transaction',()=>{
  const insert={...command,minimumPoints:1,maximumPoints:1};const d={...descriptor,commands:[insert]};
  const session=moduleSession(d,insert,moduleRequest(d,insert),'v');session.pick({x:5,y:6});assert.ok(session.complete);assert.equal(session.command.points.length,1);
});
