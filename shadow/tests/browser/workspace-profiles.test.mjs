import test from 'node:test';
import assert from 'node:assert/strict';
import { WorkspaceProfiles, changeWorkspaceProfiles, workspaceProfileLayout, workspaceProfiles, workspacePresets } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-profiles.mjs';
import { workspaceProfileTools } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-profile-ui.mjs';
import { installDom, byText, byLabel } from './ui-fixture.mjs';

const ids = ['Properties', 'Graph', 'Layers'];
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
  assert.deepEqual(Object.keys(graph.layout),['chrome','tab','commandHeight','palettes','quickAccess','ribbonCustomization']);
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
