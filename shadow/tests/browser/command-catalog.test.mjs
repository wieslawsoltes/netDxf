import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { commands } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/core.mjs';
import { graphOperations } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/graph-contracts.mjs';
import { buildCommandCatalog, searchCommands, commandAvailability, resolveCommand, quickAccessPreferences,
  changeQuickAccess, quickAccessCapacity, defaultQuickAccess } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/command-catalog.mjs';

const module = {descriptor:{id:'pid',name:'P&ID',commands:[{id:'route',name:'Route pipe',description:'Connect process equipment',requiresSelection:true}]},enabled:true};
const catalog=buildCommandCatalog({modules:[module]});
const state={hasDocument:true,busy:false,readOnly:false,blockCount:2,selectionCount:1,modules:[{id:'pid',enabled:true}],undoCount:1,redoCount:1};
const find=id=>catalog.find(entry=>entry.id===id);

test('catalog covers every current core command, semantic workspace action and topology operation',()=>{
  for(const name of Object.keys(commands))assert(find(`command:${name}`),name);
  for(const operation of Object.keys(graphOperations))assert(find(`action:graph-query:${operation}`),operation);
  const source=readFileSync(new URL('../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/studio-control.mjs',import.meta.url),'utf8');
  const staticNames=source.match(/const actionNames = new Set\(\[([\s\S]*?)\]\)/)[1];
  for(const [,action] of staticNames.matchAll(/'([^']+)'/g))assert(find(`action:${action}`),`undiscoverable action: ${action}`);
  for(const mode of ['explore','components','path','impact'])assert(find(`action:reference-query:${mode}`));
  assert.equal(new Set(catalog.map(entry=>entry.id)).size,catalog.length);
});

test('search finds labels, multiword tool names, command aliases, module metadata and exact prompt mode',()=>{
  for(const [query,id] of [
    ['layer manager','action:layer-manager'],['minimum spanning forest','action:graph-query:spanning_forest'],
    ['object reference explorer','action:reference-query:explore'],['REC','command:RECTANG'],['L','command:LINE'],
    ['WSSAVE','action:ui-workspaces'],['SAVE','action:save-as'],['QSAVE','action:save'],
    ['-INSERT','command:-INSERT'],['INSERT','command:INSERT'],['ATTREQ','input:ATTREQ'],
    ['route pipe','action:module-command:pid:route'],['CUI','action:ribbon-customize'],
    ['LAYISO','action:layer-isolate'],['LAYUNISO','action:layer-unisolate'],
    ['P&ID Connectivity','action:module-open:pid:connectivity']
  ])assert.equal(searchCommands(catalog,query)[0]?.id,id,query);
  assert(searchCommands(catalog,'process equipment').some(entry=>entry.id==='action:module-command:pid:route'));
  assert.equal(searchCommands(catalog,'not-a-real-command-xyz').length,0);
  assert.equal(searchCommands(catalog,'',3).length,3);
  assert.deepEqual(searchCommands(catalog,'graph'),searchCommands(catalog,'GRAPH'),'ranking is deterministic');
});

test('search and catalog bound caller-controlled metadata without evaluating or persisting it',()=>{
  const malformed=buildCommandCatalog({modules:[null,{}, {descriptor:{id:'bad:target',name:'Bad',commands:[]}},module,module]});
  assert.equal(malformed.filter(entry=>entry.id==='action:module-command:pid:route').length,1);
  assert(searchCommands(catalog,'x'.repeat(100000)).length===0);
  const unknown=searchCommands(catalog,'<script>alert(1)</script>');
  assert.equal(unknown.length,0);
  assert(!catalog.some(entry=>Object.hasOwn(entry,'payload')));
});

test('availability checks read-only receipts, busy document switches and actual undo history',()=>{
  assert.match(commandAvailability(find('command:LINE'),{...state,readOnly:true}),/current drawing/);
  assert.match(commandAvailability(find('action:undo'),{...state,undoCount:0}),/Nothing to undo/);
  for(const action of ['new','open','demo','font'])
    assert.match(commandAvailability(find(`action:${action}`),{...state,busy:true}),/Wait/,action);
  assert.equal(commandAvailability(find('action:help'),{...state,busy:true}),'');
  assert.equal(commandAvailability(find('action:reference-graph'),{...state,readOnly:true}),'');
  assert.match(commandAvailability(find('input:ATTREQ'),{...state,commandActive:true}),/current drawing command/);
});

test('block and discipline contexts are rechecked immediately before dispatch',()=>{
  const id='action:module-command:pid:route';
  assert.equal(resolveCommand(id,catalog,state).target,'module-command:pid:route');
  assert.throws(()=>resolveCommand(id,catalog,{...state,modules:[{id:'pid',enabled:false}]}),/Enable/);
  assert.throws(()=>resolveCommand(id,catalog,{...state,blockEditing:true}),/Close the block editor/);
  assert.throws(()=>resolveCommand(id,catalog,{...state,selectionCount:0}),/Select source objects/);
  assert.throws(()=>resolveCommand(id,buildCommandCatalog(),state),/no longer registered/);
  assert.match(commandAvailability(find('action:block-save'),state),/Open a block/);
  assert.equal(commandAvailability(find('action:block-save'),{...state,blockEditing:true}),'');
  assert.equal(commandAvailability(find('command:MOVE'),{...state,selectionCount:0}),'','interactive command can request selection');
  assert.throws(()=>resolveCommand('action:layer-isolate',catalog,{...state,selectionCount:0}),/Select source objects/);
  for(const action of ['layer-isolate','layer-unisolate']) {
    assert.throws(()=>resolveCommand(`action:${action}`,catalog,{...state,readOnly:true}),/current drawing/);
    assert.throws(()=>resolveCommand(`action:${action}`,catalog,{...state,blockEditing:true}),/Close the block editor/);
  }
  assert.equal(commandAvailability(find('action:ribbon-customize'),{hasDocument:false}),'');
  assert.throws(()=>resolveCommand('action:module-open:pid:connectivity',catalog,{...state,modules:[]}),/Enable/);
});

test('preferences retain only bounded registered IDs and unloaded module pins',()=>{
  assert.deepEqual(quickAccessPreferences(null).pinned,defaultQuickAccess);
  const saved=quickAccessPreferences({version:1,pinned:['action:save','action:save','javascript:alert(1)',
    'action:missing','action:module-command:custom:place','action:module-open:custom:connectivity',{},'command:LINE'],secret:'do-not-copy'});
  assert.deepEqual(saved,{version:1,pinned:['action:save','action:module-command:custom:place','action:module-open:custom:connectivity','command:LINE']});
  assert.equal(quickAccessPreferences({version:1,pinned:catalog.map(entry=>entry.id)}).pinned.length,16);
  assert.deepEqual(quickAccessPreferences({version:1,pinned:[]}).pinned,[],'intentional empty toolbar survives reload');
});

test('pinning, keyboard reordering, removal and reset use detached validated preferences',()=>{
  const original={version:1,pinned:['action:save']};
  let value=changeQuickAccess(original,{type:'pin',id:'command:LINE'},catalog);
  value=changeQuickAccess(value,{type:'move',id:'command:LINE',offset:-1},catalog);
  assert.deepEqual(value.pinned,['command:LINE','action:save']);assert.deepEqual(original.pinned,['action:save']);
  assert.deepEqual(changeQuickAccess(value,{type:'move',id:'command:LINE',offset:-1},catalog),value);
  value=changeQuickAccess(value,{type:'remove',id:'command:LINE'},catalog);assert.deepEqual(value.pinned,['action:save']);
  assert.deepEqual(changeQuickAccess(value,{type:'reset'},catalog).pinned,defaultQuickAccess);
  assert.throws(()=>changeQuickAccess(value,{type:'pin',id:'action:arbitrary'},catalog),/registered/);
  assert.throws(()=>changeQuickAccess({version:1,pinned:catalog.slice(0,16).map(entry=>entry.id)},
    {type:'pin',id:'action:save'},catalog),/at most 16/);
});

test('responsive capacity always leaves space for search customize and overflow controls',()=>{
  assert.equal(quickAccessCapacity(88),0);assert.equal(quickAccessCapacity(140),1);
  assert.equal(quickAccessCapacity(244),5);assert.equal(quickAccessCapacity(10000),16);
  assert.equal(quickAccessCapacity(-10),0);
});
