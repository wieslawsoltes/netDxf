import test from 'node:test';
import assert from 'node:assert/strict';
import { WorkspaceProfiles, changeWorkspaceProfiles, workspaceProfileLayout, workspaceProfiles, workspacePresets } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-profiles.mjs';
import { workspaceProfileTools } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-profile-ui.mjs';
import { installDom, byText, byLabel } from './ui-fixture.mjs';

const ids = ['Properties', 'Graph', 'Layers'];
test('direct workspace switching resolves preset and saved identities separately',()=>{
  let text=null;
  const storage={getItem:()=>text,setItem:(_,value)=>{text=value;}};
  const store=new WorkspaceProfiles({paletteIds:ids,storage});
  store.change({type:'save',name:'Drafting',layout:{tab:'Graph',shell:{version:1,commandWindow:{mode:'float'}}}});
  const applied=[];
  const profiles=workspaceProfileTools({paletteIds:ids,storage,capture:()=>({}),restore:layout=>applied.push(layout),onApplied:(name,preset)=>applied.push({name,preset})});
  profiles.apply({name:'Drafting',preset:false});
  assert.equal(applied[0].tab,'Graph');assert.equal(applied[0].shell.commandWindow.mode,'float');assert.deepEqual(applied[1],{name:'Drafting',preset:false});
  profiles.apply({name:'Drafting',preset:true});assert.equal(applied[2].tab,'Home');assert.equal(applied[2].shell.commandWindow.mode,'dock');
  assert.throws(()=>profiles.apply({name:'Missing',preset:false}),/no longer exists/);
});

test('workspace autosave is off by default and applying a current workspace restores its saved layout', () => {
  let text = null;
  const storage = {getItem:()=>text,setItem:(_,value)=>{text=value;}};
  const store = new WorkspaceProfiles({paletteIds:ids,storage});
  store.change({type:'save',name:'Review',layout:{tab:'Graph'}});
  const before = text, applied = [];
  const profiles = workspaceProfileTools({paletteIds:ids,storage,current:()=>({name:'review',preset:false}),
    capture:()=>({tab:'View'}),restore:layout=>applied.push(layout)});
  profiles.apply({name:'REVIEW',preset:false});
  assert.equal(applied[0].tab,'Graph');
  assert.equal(text,before);
});

test('autosave preserves outgoing live layout before restoring a different workspace and keeps canonical names', () => {
  let text = null, active = {name:'review',preset:false}, live = {tab:'View',commandHeight:220,
    palettes:{version:3,panels:{Graph:{open:true,mode:'float',autoHide:true,x:140,y:180}}}};
  const events = [];
  const storage = {getItem:()=>text,setItem:(_,value)=>{text=value;events.push('save');}};
  const store = new WorkspaceProfiles({paletteIds:ids,storage});
  store.change({type:'save',name:'Review',layout:{tab:'Home'}});
  store.change({type:'save',name:'Analysis',layout:{tab:'Graph'}});
  events.length = 0;
  const profiles = workspaceProfileTools({paletteIds:ids,storage,current:()=>active,autoSave:()=>true,
    capture:()=>live,restore:layout=>{events.push('restore');live=layout;},
    onApplied:(name,preset)=>{events.push('applied');active={name,preset};}});
  profiles.apply({name:'ANALYSIS',preset:false});
  assert.deepEqual(events,['save','restore','applied']);
  assert.deepEqual(active,{name:'Analysis',preset:false});
  const saved = store.read().profiles;
  assert.deepEqual(saved.map(entry=>entry.name),['Review','Analysis']);
  assert.equal(saved[0].layout.tab,'View');assert.equal(saved[0].layout.commandHeight,220);
  assert.equal(saved[0].layout.palettes.panels.Graph.autoHide,true);
  assert.equal(saved[1].layout.tab,'Graph');assert.equal(live.tab,'Graph');
  profiles.apply({name:'Review',preset:false});
  assert.equal(live.tab,'View');assert.equal(live.palettes.panels.Graph.x,140);
});

test('autosave distinguishes a saved workspace from an equally named immutable preset', () => {
  let text = null, active = {name:'Drafting',preset:true}, live = {tab:'View'}, writes = 0;
  const storage = {getItem:()=>text,setItem:(_,value)=>{text=value;writes++;}};
  const store = new WorkspaceProfiles({paletteIds:ids,storage});
  store.change({type:'save',name:'Drafting',layout:{tab:'Graph'}});
  const profiles = workspaceProfileTools({paletteIds:ids,storage,current:()=>active,autoSave:()=>true,
    capture:()=>live,restore:layout=>{live=layout;},onApplied:(name,preset)=>{active={name,preset};}});
  profiles.apply({name:'drafting',preset:false});
  assert.equal(writes,1);assert.equal(live.tab,'Graph');
  live={tab:'Manage'};
  profiles.apply({name:'DRAFTING',preset:true});
  assert.equal(writes,2);assert.equal(live.tab,'Home');
  assert.equal(store.read().profiles[0].layout.tab,'Manage');
  assert.equal(workspacePresets(ids)[0].layout.tab,'Home');
});

test('autosave reselect preserves live changes while explicit reset bypasses autosave', () => {
  let text = null, writes = 0, live = {tab:'View'};
  const applied = [];
  const storage = {getItem:()=>text,setItem:(_,value)=>{text=value;writes++;}};
  const store = new WorkspaceProfiles({paletteIds:ids,storage});
  store.change({type:'save',name:'Review',layout:{tab:'Graph'}});
  const profiles = workspaceProfileTools({paletteIds:ids,storage,current:()=>({name:'review',preset:false}),autoSave:()=>true,
    capture:()=>live,restore:layout=>{live=layout;},onApplied:(...args)=>applied.push(args)});
  const result = profiles.apply({name:'REVIEW',preset:false});
  assert.equal(result.tab,'View');assert.equal(live.tab,'View');assert.equal(writes,1);assert.equal(applied.length,0);
  profiles.apply({name:'Review',preset:false},{reset:true});
  assert.equal(live.tab,'Graph');assert.equal(writes,1);assert.deepEqual(applied,[['Review',false]]);
});

test('autosave skips detached or removed current profiles and validates the destination before saving', () => {
  let text = null, writes = 0, active = {name:'Review',preset:false,detached:true};
  const storage = {getItem:()=>text,setItem:(_,value)=>{text=value;writes++;}};
  const store = new WorkspaceProfiles({paletteIds:ids,storage});
  store.change({type:'save',name:'Review',layout:{tab:'Graph'}});
  const applied = [];
  const profiles = workspaceProfileTools({paletteIds:ids,storage,current:()=>active,autoSave:()=>true,
    capture:()=>({tab:'View'}),restore:layout=>applied.push(layout)});
  profiles.apply({name:'Drafting',preset:true});
  assert.equal(writes,1);assert.equal(store.read().profiles[0].layout.tab,'Graph');
  active={name:'Deleted profile',preset:false};
  profiles.apply({name:'Graph analysis',preset:true});
  assert.equal(writes,1);assert.equal(store.read().profiles.length,1);
  active={name:'Review',preset:false};
  assert.throws(()=>profiles.apply({name:'Missing',preset:false}),/no longer exists/);
  assert.equal(writes,1);assert.equal(applied.length,2);
});

test('autosave storage failure aborts switching and leaves the active layout and profile identity intact', () => {
  let text = null, fail = false;
  const storage = {getItem:()=>text,setItem:(_,value)=>{if(fail)throw new Error('quota');text=value;}};
  const store = new WorkspaceProfiles({paletteIds:ids,storage});
  store.change({type:'save',name:'Review',layout:{tab:'Graph'}});
  const before = text, events = [];
  const profiles = workspaceProfileTools({paletteIds:ids,storage,current:()=>({name:'Review',preset:false}),autoSave:()=>true,
    capture:()=>({tab:'View'}),restore:()=>events.push('restore'),onApplied:()=>events.push('applied')});
  fail=true;
  assert.throws(()=>profiles.apply({name:'Drafting',preset:true}),error=>{
    assert.match(error.message,/automatically save workspace “Review”/);
    assert.match(error.message,/unavailable or full/);assert.match(error.message,/turn off automatic workspace saving/);
    return true;
  });
  assert.equal(text,before);assert.deepEqual(events,[]);
});
test('workspace persistence retains only UI layout fields, clamps size and tolerates malformed storage', () => {
  const value = workspaceProfileLayout({ tab:'Graph', commandHeight:99999, chrome:{ribbonMode:'titles',commandLineVisible:false},
    documentId:'private', agent:{secret:'never-copy'}, palettes:{version:2,active:'Graph',panels:{Graph:{open:true,mode:'float',secret:'never-copy'},Unknown:{open:true}}} }, ids);
  assert.equal(value.commandHeight,600);assert.equal(value.chrome.ribbonMode,'titles');
  assert.equal(value.chrome.commandLineVisible,false);assert.equal(value.palettes.panels.Graph.mode,'float');
  assert(!JSON.stringify(value).includes('private'));assert(!JSON.stringify(value).includes('never-copy'));
  for(const input of [null,[],{version:2},{version:1,profiles:[null,{},'invalid']}] )
    assert.deepEqual(workspaceProfiles(input,ids),{version:1,profiles:[]});
});

test('saved layouts support explicit update, case-insensitive rename/delete and detached snapshots', () => {
  const original=workspaceProfileLayout({},ids);
  let saved=changeWorkspaceProfiles(null,{type:'save',name:'  Drafting custom ',layout:original},ids);
  original.palettes.panels.Properties.open=false;
  assert(saved.profiles[0].layout.palettes.panels.Properties.open);
  assert.throws(()=>changeWorkspaceProfiles(saved,{type:'save',name:'drafting CUSTOM'},ids),/already exists/);
  saved=changeWorkspaceProfiles(saved,{type:'save',name:'Drafting custom',replace:true,layout:{tab:'Graph'}},ids);
  assert.equal(saved.profiles[0].layout.tab,'Graph');
  saved=changeWorkspaceProfiles(saved,{type:'rename',name:'DRAFTING CUSTOM',next:'Analysis'},ids);
  assert.equal(saved.profiles[0].name,'Analysis');
  assert.equal(changeWorkspaceProfiles(saved,{type:'delete',name:'analysis'},ids).profiles.length,0);
  assert.throws(()=>changeWorkspaceProfiles(saved,{type:'rename',name:'Analysis',next:' '.repeat(10)},ids));
});

test('saved workspaces restore bounded Quick Access choices and migrate old profiles to defaults', () => {
  const layout = { quickAccess:{version:1,pinned:['action:reference-query:impact','command:LINE','action:layer-manager']} };
  const saved = changeWorkspaceProfiles(null,{type:'save',name:'Investigate',layout},ids);
  layout.quickAccess.pinned.reverse();
  const restored=workspaceProfiles(JSON.parse(JSON.stringify(saved)),ids).profiles[0].layout;
  assert.deepEqual(restored.quickAccess.pinned,['action:reference-query:impact','command:LINE','action:layer-manager']);
  const legacy=workspaceProfiles({version:1,profiles:[{name:'Old layout',layout:{tab:'Graph'}}]},ids).profiles[0].layout;
  assert.equal(legacy.tab,'Graph');assert.equal(legacy.quickAccess.pinned.length,5);
  assert.equal(workspaceProfileLayout({quickAccess:{version:1,pinned:Array(100).fill('command:LINE')}},ids).quickAccess.pinned.length,1);
});

test('profile count, duplicate names and failures have explicit boundaries', () => {
  let saved=null;
  for(let i=0;i<24;i++)saved=changeWorkspaceProfiles(saved,{type:'save',name:`Layout ${i}`},ids);
  assert.throws(()=>changeWorkspaceProfiles(saved,{type:'save',name:'Overflow'},ids),/at most 24/);
  assert.throws(()=>changeWorkspaceProfiles(saved,{type:'rename',name:'Layout 0',next:'LAYOUT 1'},ids),/already exists/);
  assert.throws(()=>changeWorkspaceProfiles(saved,{type:'save',name:'x'.repeat(81)},ids),/1–80/);
  const store=new WorkspaceProfiles({paletteIds:ids,storage:{getItem:()=>'{broken',setItem(){throw new Error('quota');}}});
  assert.equal(store.read().profiles.length,0);
  assert.throws(()=>store.change({type:'save',name:'Layout'}),/unavailable or full/);
});

test('saved workspaces retain detached ribbon customizations and palette appearance', () => {
  const layout = {ribbonCustomization:{version:1,customTabs:[{id:'user:review',name:'Review'}],
    customPanels:[{id:'user:inspect',tab:'user:review',name:'Inspect',commands:['action:reference-graph']}],
    floating:[{tab:'Home',panel:'Draw',x:100,y:150}]},
    palettes:{version:3,panels:{Graph:{open:true,mode:'float',opacity:40,rolloverOpacity:80}},transparencyDisabled:true}};
  const saved=changeWorkspaceProfiles(null,{type:'save',name:'Focused review',layout},ids);
  layout.ribbonCustomization.customTabs[0].name='Changed';
  const restored=workspaceProfiles(JSON.parse(JSON.stringify(saved)),ids).profiles[0].layout;
  assert.equal(restored.ribbonCustomization.customTabs[0].name,'Review');
  assert.deepEqual(restored.ribbonCustomization.customPanels[0].commands,['action:reference-graph']);
  assert.equal(restored.palettes.panels.Graph.opacity,40);
  assert.equal(restored.palettes.panels.Graph.rolloverOpacity,80);
  assert.equal(restored.palettes.transparencyDisabled,true);
});

test('oversized workspace saves and updates retain every previously saved profile', () => {
  let text = null;
  const store = new WorkspaceProfiles({paletteIds:ids,storage:{getItem:()=>text,setItem:(_,value)=>{text=value;}}});
  const layout = count => ({ribbonCustomization:{version:1,
    customPanels:Array.from({length:24},(_,i)=>({id:`user:panel${i}`,tab:'Home',name:`Panel ${i}`,
      commands:Array.from({length:count},(_,j)=>`action:${'x'.repeat(160)}${j}`)}))}});
  let count=0;
  while(true) {
    const before=text;
    try {store.change({type:'save',name:`Layout ${count}`,layout:layout(24)});count++;}
    catch(error) {
      assert.match(error.message,/storage limit/);assert.equal(text,before);break;
    }
    assert(count<24);
  }
  assert.equal(store.read().profiles.length,count);
  // Fill the remaining capacity with smaller, valid profiles before an update.
  for(let i=0;i<18;i++) {
    try {store.change({type:'save',name:`Small ${i}`,layout:layout(1)});}
    catch(error) {assert.match(error.message,/storage limit/);break;}
  }
  const before=text,names=store.read().profiles.map(value=>value.name);
  assert.throws(()=>store.change({type:'save',replace:true,name:'Layout 0',layout:layout(29)}),/storage limit/);
  assert.equal(text,before);
  assert.deepEqual(store.read().profiles.map(value=>value.name),names);
});

test('preset layouts keep Properties available beside a left Graph palette and exclude drawing settings', () => {
  const [draft,graph]=workspacePresets(ids);
  assert(draft.layout.palettes.panels.Properties.open);
  assert(graph.layout.palettes.panels.Properties.open);assert(graph.layout.palettes.panels.Graph.open);
  assert.equal(graph.layout.palettes.panels.Graph.side,'left');assert.equal(graph.layout.tab,'Graph');
  assert.deepEqual(Object.keys(graph.layout),['chrome','tab','commandHeight','palettes','quickAccess','ribbonCustomization','shell']);
});

test('dependency workspace provides a wide References tab beside source Properties', () => {
  const entry=workspacePresets([...ids,'References']).find(preset=>preset.name==='Dependency analysis');
  assert.equal(entry.layout.tab,'Graph');
  assert.equal(entry.layout.palettes.panels.References.width,540);
  assert.equal(entry.layout.palettes.panels.Properties.side,'right');
  assert.equal(entry.layout.palettes.panels.Properties.open,true);
  assert.deepEqual(entry.layout.palettes.groups.left,[{tabs:['References','Graph'],active:'References',weight:1}]);
  assert(!entry.layout.palettes.groups.right.some(group=>group.tabs.includes('References')||group.tabs.includes('Graph')));
});

test('discipline workspace groups domain, topology and references without enabling modules', () => {
  const entry = workspacePresets([...ids, 'References', 'Modules']).find(preset => preset.name === 'Discipline analysis');
  assert.equal(entry.layout.palettes.active, 'Modules');
  assert.deepEqual(entry.layout.palettes.groups.left, [{ tabs: ['Modules','Graph','References'], active: 'Modules', weight: 1 }]);
  assert.equal(entry.layout.palettes.panels.Properties.open, true);
  assert.equal(entry.layout.palettes.panels.Modules.width, 540);
  assert(!Object.hasOwn(entry.layout, 'modules'));
});

test('UI manager applies presets and manages current layouts without touching drawings', async t => {
  const root=installDom(t),applied=[],errors=[];let stored=null;
  const show=workspaceProfileTools({paletteIds:ids,storage:{getItem:()=>stored,setItem:(key,text)=>stored=text},
    capture:()=>({tab:'Graph',chrome:{ribbonMode:'tabs'}}),restore:layout=>applied.push(layout),
    report:error=>errors.push(error),dialog:async(title,content)=>root.replaceChildren(...content)});
  await show();
  assert(byText(root,'Delete workspace').disabled);
  const choice=byLabel(root,'UI workspace');choice.value='1';await choice.dispatch('change');
  await byText(root,'Apply layout').click();assert.equal(applied[0].tab,'Graph');
  const name=byLabel(root,'Workspace name');name.value='Review';
  await byText(root,'Save current as').click();assert(!byText(root,'Update saved workspace').disabled);
  assert.equal(JSON.parse(stored).profiles[0].layout.chrome.ribbonMode,'tabs');
  name.value='Review renamed';await byText(root,'Rename workspace').click();
  assert.equal(JSON.parse(stored).profiles[0].name,'Review renamed');
  await byText(root,'Delete workspace').click();assert.equal(JSON.parse(stored).profiles.length,0);
  assert(byText(root,'Delete workspace').disabled);assert.deepEqual(errors,[]);
});

test('UI manager selects the current typed profile and exports its updated snapshot after an autosave switch', async t => {
  const root = installDom(t), exported = [];
  let text = null, active = {name:'drafting',preset:false};
  const storage = {getItem:()=>text,setItem:(_,value)=>{text=value;}};
  const store = new WorkspaceProfiles({paletteIds:ids,storage});
  store.change({type:'save',name:'Drafting',layout:{tab:'Home'}});
  const show = workspaceProfileTools({paletteIds:ids,storage,current:()=>active,autoSave:()=>true,
    capture:()=>({tab:'View'}),restore:()=>{},onApplied:(name,preset)=>{active={name,preset};},
    download:(name,content)=>exported.push(JSON.parse(content)),dialog:async(title,content)=>root.replaceChildren(...content)});
  await show();
  const choice = byLabel(root,'UI workspace');
  assert.equal(choice.value,'2');
  choice.value='1';await choice.dispatch('change');await byText(root,'Apply layout').click();
  assert.deepEqual(active,{name:'Graph analysis',preset:true});
  choice.value='2';await choice.dispatch('change');await byText(root,'Export selected workspace').click();
  assert.equal(exported[0].name,'Drafting');assert.equal(exported[0].layout.tab,'View');
});
