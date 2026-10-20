import { test } from 'node:test';
import assert from 'node:assert/strict';
import { collapsedPanels, chromePreferences, flyoutPosition, reconcileRibbonContext, RibbonLayout } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-layout.mjs';
import { ribbonEntries } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ui.mjs';

test('ribbon retains full panels when space is available and collapses trailing panels first',()=>{
  assert.deepEqual(collapsedPanels([240,300,130],700),[false,false,false]);
  assert.deepEqual(collapsedPanels([240,300,130],600),[false,false,true]);
  assert.deepEqual(collapsedPanels([240,300,130],450),[false,true,true]);
  assert.deepEqual(collapsedPanels([240,300,130],240),[true,true,true]);
});
test('all Home panels fit at the narrow Studio panel width without losing any tool',()=>{
  const widths=[350,470,170,220,150,130,80], collapsed=collapsedPanels(widths,435);
  assert(collapsed.every(Boolean));
  assert(widths.reduce((sum,width,index)=>sum+(collapsed[index]?56:width),0)+12<=435);
  assert.deepEqual(collapsedPanels([],20),[]);
  assert.deepEqual(collapsedPanels([40,160],100),[false,true]);
});
test('command line, command history and ribbon visibility restore independently',()=>{
  assert.deepEqual(chromePreferences(),{ribbonMode:'full',commandLineVisible:true,commandHistoryVisible:true});
  assert.deepEqual(chromePreferences({ribbonMinimized:true,commandLineVisible:false,commandHistoryVisible:false}),{ribbonMode:'tabs',commandLineVisible:false,commandHistoryVisible:false});
  assert.equal(chromePreferences({commandHistoryVisible:false}).commandLineVisible,true);
  assert.equal(chromePreferences({commandLineVisible:false}).commandHistoryVisible,true);
  assert.deepEqual(chromePreferences({ribbonMinimized:'true',commandLineVisible:'false'}),chromePreferences());
});

test('ribbon modes restore safely and migrate the old minimized preference',()=>{
  for(const ribbonMode of ['full','buttons','titles','tabs'])assert.equal(chromePreferences({ribbonMode,ribbonMinimized:true}).ribbonMode,ribbonMode);
  assert.equal(chromePreferences({ribbonMode:'invalid'}).ribbonMode,'full');
});
test('panel flyouts stay on screen at the right and bottom edges',()=>{
  assert.deepEqual(flyoutPosition({left:950,bottom:700},{width:300,height:180},{width:1024,height:768}),{left:720,top:584});
  assert.deepEqual(flyoutPosition({left:-20,bottom:5},{width:600,height:900},{width:400,height:600}),{left:4,top:4});
});

const modules = [{ id:'pid', name:'P&ID', enabled:true }, { id:'electrical', name:'Electrical', enabled:false }];
test('Block Editor is a contextual tab and discipline tabs are disabled only during definition editing',()=>{
  const normal=ribbonEntries({modules}), editing=ribbonEntries({modules,blockEditor:{name:'PUMP'}});
  assert(!normal.some(entry=>entry.key==='Block Editor')); assert(!normal.some(entry=>entry.key==='module:electrical'));
  assert.equal(normal.find(entry=>entry.key==='module:pid').disabled,false);
  assert.equal(editing.find(entry=>entry.key==='module:pid').disabled,true);
  assert.equal(editing.at(-1).contextual,'block'); assert.match(editing.at(-1).title,/PUMP/);
});
test('entering and leaving the Block Editor restores the prior ribbon, including an enabled discipline tab',()=>{
  const normal=ribbonEntries({modules}), editing=ribbonEntries({modules,blockEditor:{name:'PUMP'}});
  for(const previous of ['Insert','Graph','module:pid']){
    const context=reconcileRibbonContext(previous,{},editing);
    assert.deepEqual(context,{tab:'Block Editor',blockEditing:true,previousTab:previous});
    assert.equal(reconcileRibbonContext('Home',context,editing).tab,'Home','explicit tab changes remain possible during editing');
    assert.deepEqual(reconcileRibbonContext('Home',context,normal),{tab:previous,blockEditing:false,previousTab:null});
  }
});
test('removed modules and stale saved contextual tabs resolve to available content',()=>{
  const editing=ribbonEntries({modules,blockEditor:{name:'PUMP'}}), normal=ribbonEntries();
  const context=reconcileRibbonContext('module:pid',{},editing);
  assert.equal(reconcileRibbonContext('module:pid',context,editing).tab,'Block Editor','disabled module content cannot become active');
  assert.equal(reconcileRibbonContext('Block Editor',context,normal).tab,'Home','disabled module is not restored on exit');
  assert.equal(reconcileRibbonContext('Block Editor',{},normal).tab,'Home');
  assert.equal(reconcileRibbonContext('missing',{},normal).tab,'Home');
});

function launcherFixture(){
  const handlers=new Map(), launcher={addEventListener:(name,callback)=>handlers.set(name,callback),setAttribute(){}};
  let focused=0,closed=0,created=0;
  const tools={getAttribute:()=>null,setAttribute(){}},group={dataset:{panel:'Draw'},querySelector:selector=>selector==='.ribbon-launcher'?launcher:tools};
  const layout=Object.create(RibbonLayout.prototype);
  Object.assign(layout,{panel:{replaceChildren(){}},mode:'titles',popup:null,schedule(){},close(focus=false){if(this.popup){closed++;if(focus)focused++;}this.popup=null;}});
  layout.open=function(target,focus=true){
    if(this.popup?.group===target)return RibbonLayout.prototype.open.call(this,target,focus);
    this.close();created++;this.popup={group:target,tools,preview:!focus,keyboard:{focus:()=>{focused++;return true;}}};if(focus)this.focusTools();
  };
  layout.set([group]);
  return {layout,group,emit:(name,event={})=>handlers.get(name)(event),counts:()=>({focused,closed,created})};
}
test('titles-mode hover followed by click promotes the existing flyout instead of closing it',()=>{
  const f=launcherFixture();f.emit('pointerenter',{pointerType:'mouse'});
  assert.equal(f.layout.popup.preview,true);assert.equal(f.counts().focused,0);
  f.emit('click');assert.equal(f.layout.popup.group,f.group);assert.equal(f.layout.popup.preview,false);
  assert.deepEqual(f.counts(),{focused:1,closed:0,created:1});
  f.emit('click');assert.equal(f.layout.popup,null);assert.equal(f.counts().closed,1);
});
test('touch activation and compact panel buttons use explicit clicks without a hover-only dependency',()=>{
  for(const mode of ['titles','buttons','full']){
    const f=launcherFixture();f.layout.mode=mode;f.emit('pointerenter',{pointerType:'touch'});
    assert.equal(f.layout.popup,null);f.emit('click');assert.equal(f.layout.popup.preview,false);assert.equal(f.counts().focused,1);
  }
});
test('Down opens and focuses panel tools from the launcher while other keyboard shortcuts bubble',()=>{
  const f=launcherFixture();let prevented=0,stopped=0;
  const event=key=>({key,preventDefault:()=>prevented++,stopPropagation:()=>stopped++});
  f.emit('keydown',event('ArrowDown'));assert.equal(f.layout.popup.group,f.group);assert.equal(f.counts().focused,1);
  f.emit('keydown',event('ArrowDown'));assert.equal(f.counts().created,1);assert.equal(f.counts().focused,2);
  f.emit('keydown',event('s'));f.emit('keydown',event('F8'));
  assert.equal(prevented,2);assert.equal(stopped,2);
});
