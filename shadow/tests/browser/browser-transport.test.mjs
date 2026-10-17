import test from 'node:test';
import assert from 'node:assert/strict';
import { apiPath, installBrowserTransport } from '../../samples/netDxf.Shadow.Studio.Browser/wwwroot/browser-transport.mjs';

const baseURI = 'https://example.test/netDxf/';
const tick = () => new Promise(resolve => setImmediate(resolve));
const gate = () => { let resolve, reject; const promise = new Promise((a,b) => { resolve=a; reject=b; }); return {promise,resolve,reject}; };
const reply = (value = {}) => ({status:200,contentType:'application/json',json:JSON.stringify(value),bytes:null});
const utf8Reply = (value, status = 200) => ({status,contentType:'application/json',json:null,bytes:new TextEncoder().encode(JSON.stringify(value))});
async function fixture(t, invoke, nativeFetch = async () => new Response('asset')) {
  const calls=[];
  const transport=installBrowserTransport({baseURI,nativeFetch,invoke:async (name,...args)=>{
    calls.push([name,...args]);
    if(name==='Shutdown') return;
    return invoke(name,...args);
  }});
  t.after(async()=>{transport.dispose();await tick();});
  return {transport,calls,fetch:transport.fetch};
}

test('root, project, diagnostics and foreign URLs retain their intended boundary',()=>{
  assert.equal(apiPath('/api/state?x=1',baseURI),'/api/state?x=1');
  assert.equal(apiPath('api/state',baseURI),'/api/state');
  assert.equal(apiPath('/netDxf/api/state',baseURI),'/api/state');
  assert.equal(apiPath('api/events','https://example.test/netDxf/diagnostics.html'),'/api/events');
  for(const path of ['cad/app.mjs','_framework/dotnet.js','/other/api/state','https://foreign.test/api/state'])
    assert.equal(apiPath(path,baseURI),null);
});
test('non-API requests are delegated without modifying their options',async t=>{
  const options={cache:'no-store'}; const expected=new Response('asset');
  const f=await fixture(t,()=>assert.fail('Unexpected interop'),async (input,init)=>{
    assert.equal(input,'cad/app.mjs');assert.equal(init,options);return expected;
  });
  assert.equal(await f.fetch('cad/app.mjs',options),expected);
});
test('JSON requests preserve identity headers and route shape',async t=>{
  const f=await fixture(t,(name,request,upload)=>{
    assert.equal(name,'Dispatch');assert.equal(request.method,'POST');assert.equal(request.path,'/api/cad/drawing/command');
    assert.equal(request.headers['x-shadow-document'],'doc');assert.deepEqual(request.body,{revision:7,command:'LINE'});assert.equal(upload,null);
    return reply({revision:8});
  });
  const response=await f.fetch('/api/cad/drawing/command',{method:'POST',headers:{'Content-Type':'application/json','X-Shadow-Document':'doc','X-Shadow-Studio':'1'},body:JSON.stringify({revision:7,command:'LINE'})});
  assert.deepEqual(await response.json(),{revision:8});
});
test('raw uploads and binary replies use bytes, including sliced buffers',async t=>{
  const bytes=new Uint8Array([0,1,2,3,4]).subarray(1,4);
  const f=await fixture(t,(name,request,upload)=>{
    assert.equal(name,'Dispatch');assert.equal(request.body,null);assert.deepEqual([...upload],[1,2,3]);
    return {status:200,contentType:'application/dxf',json:null,bytes:new Uint8Array([5,6,7])};
  });
  const response=await f.fetch('/api/load',{method:'POST',body:bytes});
  assert.deepEqual([...new Uint8Array(await response.arrayBuffer())],[5,6,7]);assert.equal(response.headers.get('content-type'),'application/dxf');
});
test('failed writes retain their status and are never retried',async t=>{
  const f=await fixture(t,()=>({status:409,contentType:'application/json',json:'{"error":"stale"}',bytes:null}));
  const response=await f.fetch('/api/save',{method:'POST',headers:{'Content-Type':'application/json'},body:'{"revision":1}'});
  assert.equal(response.status,409);assert.deepEqual(await response.json(),{error:'stale'});assert.equal(f.calls.length,1);
});
test('UTF-8 JSON replies preserve Unicode, numbers, clone and body ownership',async t=>{
  const value={name:'Příčný 日本語 🚀',quoted:'"\\\n\t',position:{x:1/3,y:-1.7976931348623157e308,z:null},enabled:false};
  const expected=JSON.stringify(value), bytes=new TextEncoder().encode(` ${expected} `), view=bytes.subarray(1,bytes.length-1);
  const f=await fixture(t,()=>({status:200,contentType:'application/json',json:null,bytes:view}));
  const response=await f.fetch('/api/state'), clone=response.clone();
  bytes.fill(0);
  assert.equal(response.headers.get('content-type'),'application/json');
  assert.equal(await response.text(),expected);
  assert.deepEqual(await clone.json(),value);
  await assert.rejects(response.json(),TypeError);
});
test('UTF-8 errors and compact unchanged replies preserve fetch semantics',async t=>{
  let count=0;
  const f=await fixture(t,()=>++count===1?utf8Reply({error:'Drawing “synthetic” changed.'},409):reply(null));
  const error=await f.fetch('/api/save',{method:'POST',body:new Uint8Array([1])});
  assert.equal(error.ok,false);assert.equal(error.status,409);
  assert.deepEqual(await error.json(),{error:'Drawing “synthetic” changed.'});
  const unchanged=await f.fetch('/api/cad/drawing/state?knownServer=server&knownChange=7');
  assert.equal(unchanged.ok,true);assert.equal((await unchanged.arrayBuffer()).byteLength,4);
  assert.equal(f.calls.length,2);
});
test('document dirty tracking accepts UTF-8 and legacy string replies',async t=>{
  const add=globalThis.addEventListener,remove=globalThis.removeEventListener,handlers=new Map();
  globalThis.addEventListener=(name,handler)=>handlers.set(name,handler);
  globalThis.removeEventListener=(name,handler)=>{if(handlers.get(name)===handler)handlers.delete(name);};
  let result=utf8Reply([{id:'drawing',name:'Synthetic',dirty:true}]);
  const f=await fixture(t,()=>result);
  t.after(()=>{f.transport.dispose();globalThis.addEventListener=add;globalThis.removeEventListener=remove;});
  const prevented=()=>{let value=false;handlers.get('beforeunload')({preventDefault(){value=true;},returnValue:null});return value;};
  assert.equal(prevented(),false);
  assert.equal((await (await f.fetch('/api/cad/documents')).json())[0].dirty,true);
  assert.equal(prevented(),true);
  result=utf8Reply([{id:'drawing',name:'Synthetic',dirty:false}]);
  await f.fetch('/api/cad/documents');assert.equal(prevented(),false);
  result=reply([{id:'drawing',dirty:true}]);
  await f.fetch('/api/cad/documents');assert.equal(prevented(),true);
  result=reply([]);await f.fetch('/api/cad/documents');assert.equal(prevented(),false);
});
test('interop calls are serialized and a failed call does not poison the queue',async t=>{
  const first=gate();let count=0;
  const f=await fixture(t,async()=>{ if(++count===1){await first.promise;throw new Error('first failed');}return reply({count}); });
  const a=f.fetch('/api/state');const rejected=assert.rejects(a,/first failed/);const b=f.fetch('/api/state');
  await tick();assert.equal(count,1);first.resolve();await rejected;assert.deepEqual(await (await b).json(),{count:2});
});
test('aborted queued calls never invoke .NET and later calls remain usable',async t=>{
  const first=gate();let count=0;
  const f=await fixture(t,async()=>{if(++count===1)await first.promise;return reply();});
  const a=f.fetch('/api/state');const controller=new AbortController();
  const b=f.fetch('/api/state',{signal:controller.signal});const rejected=assert.rejects(b,error=>error.name==='AbortError');
  controller.abort();first.resolve();await a;await rejected;await f.fetch('/api/state');assert.equal(count,2);
});
test('aborting an in-flight mutation fences its response without replaying the edit',async t=>{
  const pending=gate();const started=gate();const controller=new AbortController();
  const f=await fixture(t,async()=>{started.resolve();await pending.promise;return reply();});
  const request=f.fetch('/api/save',{method:'POST',body:new Uint8Array([1]),signal:controller.signal});
  const rejected=assert.rejects(request,error=>error.name==='AbortError');
  await started.promise;controller.abort();pending.resolve();await rejected;assert.equal(f.calls.length,1);
});
test('pre-aborted, oversized and invalid URL requests do not dispatch',async t=>{
  const f=await fixture(t,()=>assert.fail('Unexpected interop'));
  const controller=new AbortController();controller.abort();await assert.rejects(f.fetch('/api/state',{signal:controller.signal}),error=>error.name==='AbortError');
  assert.equal((await f.fetch('/api/load',{method:'POST',body:new Uint8Array(20*1024*1024+1)})).status,400);
  await assert.rejects(f.fetch('https://['),TypeError);assert.equal(f.calls.length,0);
});
test('SSE reader cancellation releases the subscription once, without double-closing',async t=>{
  const pending=gate();let reads=0;
  const f=await fixture(t,async(name,...args)=>{
    if(name==='Subscribe'){assert.equal(args[0],'server:7');return 'subscription';}
    if(name==='ReadEvent')return ++reads===1?{eventId:'server:8',data:'{"revision":8}'}:pending.promise;
    if(name==='Unsubscribe'){pending.resolve(null);return;}
    assert.fail(name);
  });
  const response=await f.fetch('api/events',{headers:{'Last-Event-ID':'server:7'}});const reader=response.body.getReader();
  assert.match(new TextDecoder().decode((await reader.read()).value),/id: server:8\nevent: state\ndata: {"revision":8}/);
  await reader.cancel();await tick();assert.equal(f.calls.filter(c=>c[0]==='Unsubscribe').length,1);
});
test('an aborted event reader releases a pending read',async t=>{
  const pending=gate(),started=gate();const controller=new AbortController();
  const f=await fixture(t,async name=>{
    if(name==='Subscribe')return 's';if(name==='ReadEvent'){started.resolve();return pending.promise;}
    if(name==='Unsubscribe'){pending.resolve(null);return;}assert.fail(name);
  });
  const reader=(await f.fetch('api/events',{signal:controller.signal})).body.getReader();const reading=reader.read();
  const rejected=assert.rejects(reading,error=>error.name==='AbortError');await started.promise;controller.abort();await rejected;await tick();
  assert.equal(f.calls.filter(c=>c[0]==='Unsubscribe').length,1);
});
test('cancelling before subscription creation completes still disposes its handle',async t=>{
  const pending=gate();const f=await fixture(t,async name=>{if(name==='Subscribe')return pending.promise;if(name==='Unsubscribe')return;assert.fail(name);});
  const reader=(await f.fetch('api/events')).body.getReader();await reader.cancel();pending.resolve('late');await tick();
  assert.deepEqual(f.calls.filter(c=>c[0]==='Unsubscribe'),[['Unsubscribe','late']]);
});
test('EventSource does not deliver notifications after close',async t=>{
  const pending=gate();const f=await fixture(t,async name=>{
    if(name==='Subscribe')return 's';if(name==='ReadEvent')return pending.promise;if(name==='Unsubscribe')return;assert.fail(name);
  });
  const source=new EventSource('api/events');let notices=0;source.addEventListener('state',()=>notices++);
  await tick();assert.equal(source.readyState,source.OPEN);source.close();pending.resolve({eventId:'s:1',data:'{}'});await tick();
  assert.equal(source.readyState,source.CLOSED);assert.equal(notices,0);assert.equal(f.calls.filter(c=>c[0]==='Unsubscribe').length,1);
});
