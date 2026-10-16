import test from 'node:test';
import assert from 'node:assert/strict';
import { CadClient, ApiError } from '../../samples/netDxf.Shadow.Studio/wwwroot/cad/client.mjs';

function setup() {
  let reply;
  const pending = new Promise(resolve => { reply = resolve; });
  const client = new CadClient(() => pending);
  client.id = 'first';
  client.adopt({state:{serverId:'server',documentId:'document',workspaceId:'live',viewVersion:0,revision:0,changeVersion:0}});
  return {client,fail:()=>reply(new Response('{"error":"stale render"}',{status:409,headers:{'Content-Type':'application/json'}}))};
}
for (const [name, invalidate] of [
  ['document switch', c => { c.id = 'second'; c.epoch++; }],
  ['new view epoch', c => { c.epoch++; }],
  ['new state receipt', c => c.adopt({state:{...c.state,changeVersion:1,revision:1}})],
  ['pending edit', c => { c.busy++; }]
]) test(`obsolete read errors cannot affect the UI after ${name}`, async () => {
  const {client,fail}=setup();
  const reading=client.read('graph');
  invalidate(client); fail();
  assert.equal(await reading,null);
});
test('current read errors preserve the actual conflict',async()=>{
  const {client,fail}=setup();const reading=client.read('graph');fail();
  await assert.rejects(reading,error=>error instanceof ApiError&&error.status===409&&error.message==='stale render');
});
test('write failures remain observable even when the read epoch changes',async()=>{
  const {client,fail}=setup();const writing=client.mutate('command',{command:{name:'LINE'}});
  await Promise.resolve(); client.epoch++; fail();
  await assert.rejects(writing,error=>error instanceof ApiError&&error.status===409);
  assert.equal(client.busy,0);
});
