import test from 'node:test';
import assert from 'node:assert/strict';
import {InsertAttributeSession,insertionDefinitions,insertionValue} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/insert-attribute-contracts.mjs';
import {insertionAttributeDialog} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/insert-attribute-ui.mjs';
import {installDom,byLabel,gate,tick} from './ui-fixture.mjs';
const definitions=[
  {id:{value:'a'},tag:'TAG',prompt:'Equipment tag',value:'P-000',verify:true},
  {id:{value:'b'},tag:'SERVICE',prompt:'Service',value:'Water',preset:true},
  {id:{value:'c'},tag:'CONST',value:'Fixed',constant:true},
  {id:{value:'d'},tag:'DETAIL',value:'First\\Psecond',multiline:true,invisible:true}
];
const completed={command:{name:'INSERT',block:'Pump',points:[{x:1,y:2,z:0}]},insertOptions:{repeat:true}};
test('insertion command prompts retain definition order, skip preset/constants and verify assigned values',()=>{
  const s=new InsertAttributeSession(completed,definitions,'drawing/1');
  assert.match(s.prompt,/Equipment tag.*P-000/);s.input('P-101');assert.match(s.prompt,/DETAIL/);
  s.input('Cooling\nwater');assert.match(s.prompt,/Verify Equipment tag.*P-101/);s.input('P-102');
  assert.equal(s.complete,true);assert.deepEqual(s.attributeAssignments,[
    {definition:{value:'a'},value:'P-102'},{definition:{value:'b'},value:'Water'},{definition:{value:'d'},value:'Cooling\\Pwater'}]);
  assert.deepEqual(s.command,completed.command);assert.equal(s.insertOptions.repeat,true);assert.equal(s.expectedKey,'drawing/1');
});
test('attribute input preserves spaces and snap-like text, supports empty values and verifies preset defaults',()=>{
  const s=new InsertAttributeSession(completed,[{...definitions[0],verify:false}, {...definitions[1],verify:true}], 'd/1');
  s.input(' END ');assert.match(s.prompt,/Verify Service.*Water/);s.input('""');
  assert.equal(s.attributeAssignments[0].value,' END ');assert.equal(s.attributeAssignments[1].value,'');
  assert.equal(new InsertAttributeSession(completed,[definitions[2]],'d/1').complete,true);
  const empty=new InsertAttributeSession(completed,[{...definitions[0],value:'',verify:false}],'d/1');empty.input('');assert.equal(empty.attributeAssignments[0].value,'');
});
test('insertion values reject invalid single-line text without advancing the session',()=>{
  const s=new InsertAttributeSession(completed,definitions,'d/1');assert.throws(()=>s.input('bad\nvalue'));
  assert.equal(s.index,0);assert.throws(()=>s.pick({x:1,y:2}));assert.throws(()=>insertionValue(definitions[3],'bad\0value'));
  assert.throws(()=>insertionDefinitions({definitions:[{appearance:definitions[0]},{appearance:definitions[0]}]}));
});
test('attribute dialog includes presets, excludes constants and collects explicit verification before insertion',async t=>{
  const root=installDom(t),answers=[],titles=[];
  let checks=0;const pending=insertionAttributeDialog({definitions,name:'Pump',current(){checks++;},dialog(title,children){
    titles.push(title);root.replaceChildren(...children);const result=gate();answers.push(result);return result.promise;
  }});
  assert.equal(byLabel(root,'Insert attribute CONST'),undefined);assert.equal(byLabel(root,'Insert attribute SERVICE').value,'Water');
  byLabel(root,'Insert attribute TAG').value='P-101';byLabel(root,'Insert attribute DETAIL').value='Cooling\nwater';
  answers[0].resolve(true);await tick();assert.equal(byLabel(root,'Verify attribute TAG').value,'P-101');
  byLabel(root,'Verify attribute TAG').value='P-102';answers[1].resolve(true);
  const values=await pending;assert.deepEqual(titles,['Insert block attributes','Verify block attributes']);assert.equal(checks,2);
  assert.equal(values[0].value,'P-102');assert.equal(values[2].value,'Cooling\\Pwater');
});
test('cancelling verification discards all collected attribute input',async t=>{
  const root=installDom(t);let dialogs=0;
  const result=await insertionAttributeDialog({definitions,name:'Pump',current(){},dialog(title,children){root.replaceChildren(...children);return ++dialogs===1;}});
  assert.equal(result,null);assert.equal(dialogs,2);
});
test('stale attribute dialog results cannot proceed to verification',async t=>{
  installDom(t);let dialogs=0;
  await assert.rejects(insertionAttributeDialog({definitions,name:'Pump',current(){throw new Error('Drawing changed');},dialog(){dialogs++;return true;}}),/Drawing changed/);
  assert.equal(dialogs,1);
});
