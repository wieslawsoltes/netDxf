import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CommandSession } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/core.mjs';
import { authoringRequest, blockName, islandStyle, parsePat } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/authoring-contracts.mjs';
import { CadClient } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/client.mjs';
const data = { selectedEntities: ['H:1','H:2'], blocks: [{id:'H:99',name:'Part'}] };
function execute(name, inputs, selected = 2) {
  const session = new CommandSession(name, 'Walls', selected);
  for (const input of inputs) session.input(input);
  assert.equal(session.complete, true);
  return authoringRequest(session.command, data);
}
test('BLOCK command binds source identities and base point to typed definition request', () => {
  const r = execute('B', ['Valve','10,20']); assert.equal(r.path,'block/define');
  assert.equal(r.body.definition.name,'Valve'); assert.deepEqual(r.body.definition.basePoint,{x:10,y:20,z:0});
  assert.deepEqual(r.body.definition.entities,[{value:'H:1'},{value:'H:2'}]); assert.equal(r.body.definition.disposition,1);
  assert.equal(r.body.definition.insertLayer,'Walls');
});
test('redefinition preserves the actual case-sensitive native name and explicitly retains sources', () => {
  const r = execute('BREDEFINE',['part','0,0']); assert.equal(r.body.definition.name,'Part');
  assert.deepEqual(r.body.definition.redefine,{value:'H:99'}); assert.equal(r.body.definition.disposition,0);
});
test('rename accepts an unselected definition, preserving its source identity', () => {
  const r = execute('BRENAME',['PART','New name'],0);assert.deepEqual(r.body,{block:{value:'H:99'},name:'New name'});
});
for (const [name,path] of [['H','hatch/create'],['HE','hatch/pattern']]) test(`${name} builds explicit pattern parameters`, () => {
  const r=execute(name,['cross','2','45','outer']);assert.equal(r.path,path);
  const p=r.body.pattern||r.body.hatch.pattern;assert.deepEqual(p,{name:'CROSS',scale:2,angle:45,islands:1});
});
test('polygon hatch remains detached until a valid completed chain',()=>{
  const r=execute('HP',['0,0','30,0','30,20','C'],0);
  assert.equal(r.body.hatch.polygons[0].length,3);assert.equal(r.body.hatch.pattern.name,'SOLID');
});
test('authoring contracts refuse missing selections and unknown definitions',()=>{
  assert.throws(()=>authoringRequest({name:'BLOCK',text:'Block',points:[{x:0,y:0}]},{...data,selectedEntities:[]}));
  assert.throws(()=>execute('BREDEFINE',['Absent','0,0']));assert.throws(()=>execute('H',['Mystery','1','0','Normal']));
  assert.throws(()=>execute('H',['LINE','1','0','mystery']));
});
for(const value of ['', 'a/b', 'a:b', '*Internal', 'bad\nname', 'x'.repeat(256)]) test(`unsafe block name refused: ${JSON.stringify(value).slice(0,40)}`,()=>assert.throws(()=>blockName(value)));
test('non-authoring commands stay on the existing dispatcher',()=>assert.equal(authoringRequest({name:'LINE'},data),null));
test('PAT parses decimal scientific notation, BOM, CRLF, comments and multiple patterns',()=>{
  const patterns=parsePat('\uFEFF; comment\r\n*FIRST, Example\r\n45,0,0,.5,2,1e-1,-.2,0 ; family\r\n*SECOND, Other\r\n90,1,2,0,-3\r\n');
  assert.equal(patterns.length,2);assert.equal(patterns[0].name,'FIRST');assert.deepEqual(patterns[0].lines[0].dashes,[.1,-.2,0]);
  assert.deepEqual(patterns[1].lines[0].delta,{x:0,y:-3,z:0});
});
for(const value of ['', '0,0,0,0,1', '*EMPTY', '*SOLID\n0,0,0,0,1', '*A\n0,0,0,0,1\n*a\n0,0,0,0,2',
  '*A\n0,0,0,0,0', '*A\n0,0,0,0,1,0,0', '*A\n0,0,0,0,1,NaN', '*A\n0,0,0,0,1,1/2',
  '*A\n0,0,0,0,1,', '*A\n0,0,0,0,1e90', '*A\n0,0,0,0,1\n*EMPTY'])
  test(`malformed PAT is rejected without partial results: ${JSON.stringify(value)}`,()=>assert.throws(()=>parsePat(value)));
test('PAT work limits are enforced before native geometry expansion',()=>{
  assert.throws(()=>parsePat('x'.repeat(1_048_577)));
  assert.throws(()=>parsePat('*A\n'+'0,0,0,0,1\n'.repeat(129)));
  assert.throws(()=>parsePat('*A\n0,0,0,0,1,'+Array(129).fill('1').join(',')));
});
test('source island policies use explicit enum values',()=>{
  assert.equal(islandStyle('normal'),0);assert.equal(islandStyle('OUTER'),1);assert.equal(islandStyle('Ignore'),2);assert.throws(()=>islandStyle(null));
});
const state=revision=>({state:{serverId:'server',documentId:'doc',workspaceId:'live',viewVersion:0,revision,changeVersion:revision}});
const response=value=>new Response(JSON.stringify(value));
test('authoring dialogs cannot replay against a later revision in the serialized queue',async()=>{
  let revision=0,calls=0;
  const client=new CadClient(async path=>path.endsWith('/state')?response(state(revision)):(calls++,response(state(++revision))));
  await client.activate('a');const key=client.key;
  const first=client.mutate('command',{}),second=client.mutate('block/define',{}, {expectedKey:key});
  await first;await assert.rejects(second,error=>error.status===409);assert.equal(calls,1);
  await client.mutate('hatch/create',{}, {expectedKey:client.key});assert.equal(calls,2);
});
