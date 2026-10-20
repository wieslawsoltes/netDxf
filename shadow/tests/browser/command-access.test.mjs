import test from 'node:test';
import assert from 'node:assert/strict';
import { createCommandAccess } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/command-access.mjs';
import { buildCommandCatalog } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/command-catalog.mjs';
import { installDom, byLabel, all, tick } from './ui-fixture.mjs';
const query = f => all(f.root).find(node=>node.tagName==='INPUT'&&node.getAttribute('aria-label')==='Search commands');

function fixture(t) {
  const root=installDom(t),create=document.createElement;
  document.body=root;
  document.createElement=tag=>{
    const node=create(tag),dispatch=node.dispatch.bind(node);
    node.dispatch=(name,options={})=>dispatch(name,{stopPropagation(){},...options});
    node.getBoundingClientRect=()=>({left:10,right:600,top:10,bottom:500});
    if(tag==='dialog'){node.showModal=()=>{node.open=true;};node.close=()=>{node.open=false;};}
    return node;
  };
  document.querySelector=selector=>selector==='dialog[open]'?all(root).find(node=>node.tagName==='DIALOG'&&node.open)||null:null;
  const toolbar=document.createElement('div');toolbar.clientWidth=244;root.append(toolbar);
  const source={hasDocument:true,busy:false,readOnly:false,blockCount:1,selectionCount:1,undoCount:1,redoCount:0,modules:[{id:'pid',enabled:true}]};
  let entries=buildCommandCatalog({modules:[{descriptor:{id:'pid',name:'P&ID',commands:[{id:'route',name:'Route pipe'}]}}]});
  let stored=null,failStorage=false;
  const calls=[],errors=[];
  const controller=createCommandAccess({toolbar,host:root,loadStyles:false,catalog:()=>entries,state:()=>source,
    execute:entry=>calls.push(entry.target),report:error=>errors.push(error.message),storage:{
      getItem:()=>stored,setItem:(key,value)=>{if(failStorage)throw new Error('full');stored=value;}
    }});
  t.after(()=>controller.destroy());
  return {root,toolbar,source,controller,calls,errors,stored:()=>stored,
    failStorage:()=>{failStorage=true;},setCatalog:value=>{entries=value;}};
}

test('opening and filtering never dispatch commands, and Escape restores the original focus',async t=>{
  const f=fixture(t),origin=byLabel(f.toolbar,'Search commands (Ctrl+K)');origin.focus();
  f.controller.search('layer manager');
  assert(f.controller.dialog.open);assert.equal(document.activeElement,query(f));
  assert(byLabel(f.root,'Layer manager'));assert.deepEqual(f.calls,[]);
  await f.controller.dialog.dispatch('keydown',{key:'Escape'});
  assert(!f.controller.dialog.open);assert.equal(document.activeElement,origin);assert.deepEqual(f.calls,[]);
});

test('ordinary refresh preserves the focused query node and focused result while updating availability',async t=>{
  const f=fixture(t);f.controller.search('line');
  const input=query(f);input.value='line';input.focus();
  f.source.busy=true;f.controller.refresh();
  assert.equal(document.activeElement,input);assert.equal(input.value,'line');
  assert(byLabel(f.root,'Line').disabled);
  f.source.busy=false;f.controller.refresh();
  const result=byLabel(f.root,'Line');result.focus();f.controller.refresh();
  assert.equal(byLabel(f.root,'Line'),result);assert.equal(document.activeElement,result);
});

test('pin controls persist only IDs and customization reorders and removes without drawing actions',async t=>{
  const f=fixture(t);f.controller.search('layer manager');
  await byLabel(f.root,'Pin Layer manager').click();
  assert.equal(f.controller.snapshot().pinned.at(-1),'action:layer-manager');
  assert.deepEqual(Object.keys(JSON.parse(f.stored())),['version','pinned']);
  f.controller.customize();
  await byLabel(f.root,'Move Layer manager earlier').click();
  assert.equal(f.controller.snapshot().pinned.at(-2),'action:layer-manager');
  await byLabel(f.root,'Remove Layer manager from Quick Access').click();
  assert(!f.controller.snapshot().pinned.includes('action:layer-manager'));assert.deepEqual(f.calls,[]);
});

test('Escape restores a connected toolbar origin after pinning or resetting rebuilt controls',async t=>{
  const f=fixture(t);
  const search=byLabel(f.toolbar,'Search commands (Ctrl+K)');search.focus();f.controller.search('layer manager');
  await byLabel(f.root,'Pin Layer manager').click();
  assert.equal(search.isConnected,false,'pinning replaced the toolbar controls');
  await f.controller.dialog.dispatch('keydown',{key:'Escape'});
  assert.equal(document.activeElement,byLabel(f.toolbar,'Search commands (Ctrl+K)'));
  assert.equal(document.activeElement.isConnected,true);
  for(const label of ['Customize Quick Access','More Quick Access commands']) {
    f.toolbar.clientWidth=135;f.controller.refresh();
    const origin=byLabel(f.toolbar,label);origin.focus();f.controller.customize();
    await byLabel(f.root,'Reset Quick Access').click();
    assert.equal(origin.isConnected,false);
    await f.controller.dialog.dispatch('keydown',{key:'Escape'});
    assert.equal(document.activeElement,byLabel(f.toolbar,label));
    assert.equal(document.activeElement.isConnected,true);
  }
  assert.deepEqual(f.calls,[]);
});

test('stale result controls cannot dispatch a disabled module or removed catalog entry',async t=>{
  const f=fixture(t);f.controller.search('route pipe');
  let result=byLabel(f.root,'P&ID: Route pipe');assert(!result.disabled);
  f.source.modules[0].enabled=false;
  await result.click();await tick();assert.deepEqual(f.calls,[]);assert.match(f.errors.at(-1),/Enable/);
  f.source.modules[0].enabled=true;f.controller.refresh();f.controller.search('route pipe');
  result=byLabel(f.root,'P&ID: Route pipe');f.setCatalog(buildCommandCatalog());
  await result.click();await tick();assert.deepEqual(f.calls,[]);assert.match(f.errors.at(-1),/no longer registered/);
});

test('Enter dispatches the registered command and closes the overlay; backdrop dismissal only closes',async t=>{
  const f=fixture(t);f.controller.search('RECTANG');
  await f.controller.dialog.dispatch('keydown',{key:'Enter',target:query(f)});
  await tick();assert.deepEqual(f.calls,['RECTANG']);assert(!f.controller.dialog.open);
  f.controller.search('line');
  await f.controller.dialog.dispatch('pointerdown',{clientX:2,clientY:2,target:f.controller.dialog});
  assert(!f.controller.dialog.open);assert.deepEqual(f.calls,['RECTANG']);
});

test('Enter with no matching or only disabled results never falls through to hidden pins',async t=>{
  const f=fixture(t);
  for(const text of ['nonexistent command xyzzzz','LINE']) {
    f.source.readOnly=text==='LINE';f.controller.search(text);
    await f.controller.dialog.dispatch('keydown',{key:'Enter',target:query(f)});
    await tick();assert.deepEqual(f.calls,[]);assert(f.controller.dialog.open);
  }
});

test('ArrowUp from the search box focuses the last visible result, excluding hidden pins',async t=>{
  const f=fixture(t);f.controller.search('zoom');
  const last=byLabel(f.root,'Zoom out'),scrolled=[];
  last.scrollIntoView=options=>scrolled.push(options);
  await f.controller.dialog.dispatch('keydown',{key:'ArrowUp',target:query(f)});
  assert.equal(document.activeElement.getAttribute('aria-label'),'Zoom out');
  assert.deepEqual(scrolled,[{block:'nearest',inline:'nearest'}]);
  const first=byLabel(f.root,'Zoom extents');
  first.scrollIntoView=options=>scrolled.push(options);
  await f.controller.dialog.dispatch('keydown',{key:'ArrowDown',target:last});
  assert.equal(document.activeElement,first);
  assert.deepEqual(scrolled,[{block:'nearest',inline:'nearest'},{block:'nearest',inline:'nearest'}]);
});

test('toolbar preserves semantic action attributes and stops delegated duplicate execution',async t=>{
  const f=fixture(t),save=byLabel(f.toolbar,'Save DXF');let stopped=false;
  assert.equal(save.getAttribute('data-action'),'save');
  await save.dispatch('click',{stopPropagation(){stopped=true;}});
  if(!stopped)f.calls.push('delegated-save');
  await tick();assert.deepEqual(f.calls,['save']);
  f.controller.restore({version:1,pinned:['command:LINE']});
  assert.equal(byLabel(f.toolbar,'Line').getAttribute('data-command'),'LINE');
});

test('keyboard shortcut is scoped to unhandled Ctrl/Meta+K and respects other modal dialogs',t=>{
  const f=fixture(t);let prevented=0;
  const event=extra=>({key:'k',ctrlKey:true,preventDefault(){prevented++;},stopPropagation(){},...extra});
  assert.equal(f.controller.handleShortcut(event({defaultPrevented:true})),false);
  assert.equal(f.controller.handleShortcut(event({isComposing:true})),false);
  const other=document.createElement('dialog');other.open=true;f.root.append(other);
  assert.equal(f.controller.handleShortcut(event()),false);other.open=false;
  assert.equal(f.controller.handleShortcut(event({ctrlKey:false,metaKey:true})),true);
  assert(f.controller.dialog.open);assert.equal(prevented,1);
});

test('narrow toolbar retains explicit search and overflow access; saved layout restore keeps bounded IDs',t=>{
  const f=fixture(t);f.toolbar.clientWidth=135;f.controller.refresh();
  assert(byLabel(f.toolbar,'Search commands (Ctrl+K)'));assert(byLabel(f.toolbar,'Customize Quick Access'));
  assert(byLabel(f.toolbar,'More Quick Access commands'));
  f.controller.restore({version:1,pinned:['command:LINE','action:save'],payload:'never persist'});
  assert.deepEqual(f.controller.snapshot(),{version:1,pinned:['command:LINE','action:save']});
  assert(!f.stored().includes('payload'));
});

test('storage failures are visible and do not claim a pin was saved',async t=>{
  const f=fixture(t),before=f.controller.snapshot();f.failStorage();f.controller.search('layer manager');
  await byLabel(f.root,'Pin Layer manager').click();
  assert.deepEqual(f.controller.snapshot(),before);assert.match(f.errors.at(-1),/could not be saved/);
});

test('whole workspace restoration applies in memory when optional storage is blocked',t=>{
  const f=fixture(t);f.failStorage();
  assert.doesNotThrow(()=>f.controller.restore({version:1,pinned:['command:LINE']}));
  assert.deepEqual(f.controller.snapshot().pinned,['command:LINE']);
  assert(byLabel(f.toolbar,'Line'));assert.match(f.errors.at(-1),/could not be saved/);
});
