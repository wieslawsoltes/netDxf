import { test } from 'node:test';
import assert from 'node:assert/strict';
import { drawingFormat, inspectDrawing, drawingFileName, exchangeCapabilities, saveRequest, maximumDrawingBytes } from '../../samples/netDxf.Shadow.Studio/wwwroot/cad/exchange-contracts.mjs';
import { CadClient } from '../../samples/netDxf.Shadow.Studio/wwwroot/cad/client.mjs';

const bytes = text => new TextEncoder().encode(text);
const capabilities = () => ({provider:'Fixture codec',dwgWriteVersions:['AC1015','AC1032'],defaultDwgVersion:'AC1032'});
const state = revision => ({state:{serverId:'server',documentId:'doc',workspaceId:'live',viewVersion:0,changeVersion:revision,revision}});
const json = value => new Response(JSON.stringify(value), {headers:{'Content-Type':'application/json'}});

test('DWG signatures, not names, determine the interchange route', async () => {
  for (const version of ['AC1014','AC1015','AC1018','AC1021','AC1024','AC1027','AC1032']) {
    assert.equal(drawingFormat(bytes(version+'payload')), 'dwg');
    const file = new Blob([version+'payload']); file.name='incorrect.dxf';
    assert.equal(await inspectDrawing(file),'dwg');
  }
});
test('ASCII and binary DXF stay on the native reader path', async () => {
  for(const value of ['  0\r\nSECTION\r\n','AutoCAD Binary DXF\r\n\u001a\0','ACX032invalid'])
    assert.equal(drawingFormat(bytes(value)),'dxf');
  const file=new Blob(['0\nSECTION\n']);file.name='not-dwg.dwg';
  assert.equal(await inspectDrawing(file),'dxf');
});
test('signature detection rejects missing bytes and non-byte input',()=>{
  for(const value of [bytes('AC'),bytes(''),new Array(8),null,'AC1032']) assert.throws(()=>drawingFormat(value));
});
test('upload quotas are checked before reading the file prefix',async()=>{
  for(const size of [0,5,maximumDrawingBytes+1,Infinity,-1,NaN,1.5])
    await assert.rejects(inspectDrawing({size,slice(){throw new Error('Must not read');}}),/6 bytes to 20 MiB/);
});
test('only the first six bytes are read to route an upload',async()=>{
  let slices=0;const file={size:maximumDrawingBytes,slice(start,end){slices++;assert.deepEqual([start,end],[0,6]);return new Blob(['AC1032']);}};
  assert.equal(await inspectDrawing(file),'dwg');assert.equal(slices,1);
});
test('download extension always matches the selected encoder',()=>{
  for(const [name,format,result] of [['Part.DWG','dxf','Part.dxf'],['Plan.dxf','dwg','Plan.dwg'],['Plan.backup','dwg','Plan.backup.dwg'],['.dwg','dxf','Drawing.dxf']])
    assert.equal(drawingFileName(name,format),result);
  assert.equal(drawingFileName('x'.repeat(200),'dwg').length,120);
});
for(const name of ['../test','a/b','a\\b','bad\0name','bad\nname','bad\x7fname',null])
  test(`unsafe download name refused: ${JSON.stringify(name)}`,()=>assert.throws(()=>drawingFileName(name,'dwg')));
test('capabilities are validated and detached from the host response',()=>{
  const source=capabilities(),actual=exchangeCapabilities(source);source.dwgWriteVersions.push('AC1018');
  assert.deepEqual(actual.dwgWriteVersions,['AC1015','AC1032']);assert(Object.isFrozen(actual));assert(Object.isFrozen(actual.dwgWriteVersions));
});
test('malformed or duplicate advertised profiles are not accepted',()=>{
  for(const value of [null,{}, {...capabilities(),dwgWriteVersions:[]},{...capabilities(),dwgWriteVersions:['AC1032','AC1032']},
    {...capabilities(),dwgWriteVersions:['<script>']},{...capabilities(),defaultDwgVersion:'AC1021'},
    {...capabilities(),provider:'x'.repeat(121)}]) assert.throws(()=>exchangeCapabilities(value));
});
test('save requests use only advertised DWG profiles and preserve native DXF default',()=>{
  const caps=exchangeCapabilities(capabilities());assert.deepEqual(saveRequest('dwg','AC1032',caps),{format:'dwg',version:'AC1032'});
  assert.deepEqual(saveRequest('dxf','AC1032',caps),{format:'dxf'});
  assert.throws(()=>saveRequest('dwg','AC1021',caps));assert.throws(()=>saveRequest('pdf','AC1032',caps));
});
test('format-aware save preserves payload and consumes the host state after encoding',async()=>{
  let revision=2,posted;
  const client=new CadClient(async(path,options)=>{
    if(path.endsWith('/state'))return json(state(revision));
    posted=JSON.parse(options.body);revision=3;return new Response('AC1032payload',{headers:{'Content-Type':'application/acad'}});
  });
  await client.activate('a');const result=await client.mutate('save',{format:'dwg',version:'AC1032'},{expectedKey:client.key});
  assert.deepEqual(posted,{format:'dwg',version:'AC1032',revision:2});assert.equal(result.type,'application/acad');assert.equal(await result.text(),'AC1032payload');
  assert.equal(client.state.revision,3);assert.equal(client.busy,0);
});
test('an export dialog cannot save a different document revision',async()=>{
  let revision=0,saves=0;const client=new CadClient(async path=>{
    if(path.endsWith('/state'))return json(state(revision));
    if(path.endsWith('/save')){saves++;return new Response('AC1032payload');}
    return json(state(++revision));
  });
  await client.activate('a');const expectedKey=client.key;
  const edit=client.mutate('command',{}),save=client.mutate('save',{format:'dwg',version:'AC1032'},{expectedKey});
  await edit;await assert.rejects(save,error=>error.status===409);assert.equal(saves,0);
});
test('failed conversion is surfaced once and does not poison subsequent edits',async()=>{
  let calls=0;const client=new CadClient(async path=>path.endsWith('/state')?json(state(0)):
    path.endsWith('/save')?(calls++,new Response('{"error":"Unsupported DWG content"}',{status:400})):json(state(1)));
  await client.activate('a');await assert.rejects(client.mutate('save',{format:'dwg'}),/Unsupported DWG content/);
  assert.equal(calls,1);assert.equal(client.state.revision,0);await client.mutate('command',{});assert.equal(client.state.revision,1);
});
