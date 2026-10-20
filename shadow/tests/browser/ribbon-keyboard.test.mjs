import test from 'node:test';
import assert from 'node:assert/strict';
import { bindRovingToolbar, nextRibbonIndex, visibleRibbonControl } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-keyboard.mjs';
import { RibbonLayout } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-layout.mjs';
import { RibbonCustomization } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-customization.mjs';
import { group, el } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ui.mjs';
import { ribbonDom, RibbonElement } from './ribbon-dom-fixture.mjs';

const command=(name,rect)=>{const node=el('button',{'aria-label':name,text:name});node.rect=rect;return node;};
const key=(element,target,value,extra={})=>{
  const result={prevented:0,stopped:0};element.emit('keydown',{target,key:value,preventDefault:()=>result.prevented++,stopPropagation:()=>result.stopped++,...extra});return result;
};
test('spatial arrows follow rendered rows and columns, including deterministic edge wrapping',()=>{
  const box=(left,top)=>({left,top,right:left+40,bottom:top+20});
  const boxes=[box(0,0),box(0,25),box(0,50),box(45,0),box(45,25),box(45,50)];
  assert.equal(nextRibbonIndex(boxes,0,'ArrowRight'),3);assert.equal(nextRibbonIndex(boxes,4,'ArrowLeft'),1);
  assert.equal(nextRibbonIndex(boxes,0,'ArrowDown'),1);assert.equal(nextRibbonIndex(boxes,5,'ArrowUp'),4);
  assert.equal(nextRibbonIndex(boxes,5,'ArrowRight'),0);assert.equal(nextRibbonIndex(boxes,2,'Home'),0);assert.equal(nextRibbonIndex(boxes,1,'End'),5);
  assert.equal(nextRibbonIndex([],0,'ArrowLeft'),-1);
  assert.equal(nextRibbonIndex(boxes,-1,'End'),5);
});
test('toolbar navigation excludes disabled, hidden and detached commands, without activating them or trapping Tab',t=>{
  const dom=ribbonDom(t),toolbar=new RibbonElement('div');dom.host.append(toolbar);
  const buttons=['First','Disabled','Hidden','Last'].map(name=>command(name));toolbar.append(...buttons);
  buttons[1].disabled=true;buttons[2].hidden=true;let invoked=0;buttons.forEach(node=>node.addEventListener('click',()=>invoked++));
  const nav=bindRovingToolbar(toolbar,{orientation:'horizontal'});dom.disposeWith(()=>nav.dispose());nav.focus();
  assert.equal(document.activeElement,buttons[0]);assert.deepEqual(buttons.map(node=>node.getAttribute('tabindex')),['0','-1','-1','-1']);
  key(toolbar,buttons[0],'ArrowRight');assert.equal(document.activeElement,buttons[3]);
  key(toolbar,buttons[3],'Home');assert.equal(document.activeElement,buttons[0]);key(toolbar,buttons[0],'End');assert.equal(document.activeElement,buttons[3]);
  assert.equal(key(toolbar,buttons[3],'Tab').prevented,0);assert.equal(key(toolbar,buttons[3],'Enter').prevented,0);assert.equal(invoked,0);
  buttons[3].remove();nav.refresh();assert.equal(buttons[0].getAttribute('tabindex'),'0');assert.equal(visibleRibbonControl(buttons[3]),false);
});
test('native resource editing and modified shortcuts are never consumed by command navigation',t=>{
  const dom=ribbonDom(t),toolbar=new RibbonElement('div');dom.host.append(toolbar);
  const first=command('First'),last=command('Last'),select=el('select',{},[]),input=el('input',{},[]);toolbar.append(first,select,input,last);
  const nav=bindRovingToolbar(toolbar,{onEscape:()=>assert.fail('native Escape must remain native')});dom.disposeWith(()=>nav.dispose());
  for(const control of [select,input])for(const value of ['ArrowLeft','ArrowDown','Home','End','Escape']){
    control.focus();assert.equal(key(toolbar,control,value).prevented,0);assert.equal(document.activeElement,control);
  }
  assert.equal(select.getAttribute('tabindex'),null);assert.equal(input.getAttribute('tabindex'),null);
  for(const extra of [{ctrlKey:true},{metaKey:true},{altKey:true},{shiftKey:true},{isComposing:true},{defaultPrevented:true}]){
    first.focus();assert.equal(key(toolbar,first,'ArrowRight',extra).prevented,0);assert.equal(document.activeElement,first);
  }
});
test('vertical menus restore their trigger with Escape and let Tab close without preventing normal traversal',t=>{
  const dom=ribbonDom(t),menu=new RibbonElement('div'),trigger=command('Menu');dom.host.append(trigger,menu);
  const first=command('First'),last=command('Last');menu.append(first,last);let closed=0;
  const close=()=>{closed++;trigger.focus();},nav=bindRovingToolbar(menu,{orientation:'vertical',onEscape:close,onTab:close});dom.disposeWith(()=>nav.dispose());
  nav.focus();key(menu,first,'ArrowUp');assert.equal(document.activeElement,last);key(menu,last,'Escape');assert.equal(document.activeElement,trigger);assert.equal(closed,1);
  nav.focus();assert.equal(key(menu,first,'Tab').prevented,0);assert.equal(closed,2);
  nav.dispose();assert.equal(first.getAttribute('tabindex'),null);assert.equal(menu.handlers.get('keydown').length,0);
});
test('ribbon entry skips full-mode hidden launchers, roves compact launchers, and restores focus after flyout Escape',t=>{
  const dom=ribbonDom(t),layout=new RibbonLayout(dom.panel);dom.disposeWith(()=>layout.dispose());
  const first=command('Line'),last=command('Circle'),draw=group('Draw',[first,last]),modify=group('Modify',[command('Move')]);
  layout.set([draw,modify],{tab:'Home'});layout.layout();layout.keyboard.focus();assert.equal(document.activeElement,first);
  key(dom.panel,first,'Escape');assert.equal(document.activeElement,dom.tab);
  layout.setMode('buttons');layout.layout();layout.keyboard.focus();const launcher=draw.querySelector('.ribbon-launcher');assert.equal(document.activeElement,launcher);
  key(dom.panel,launcher,'End');assert.equal(document.activeElement,modify.querySelector('.ribbon-launcher'));
  launcher.emit('keydown',{key:'ArrowDown'});assert.equal(document.activeElement,first);assert.equal(launcher.getAttribute('aria-expanded'),'true');
  const tools=layout.popup.tools;key(tools,first,'End');assert.equal(document.activeElement,last);
  key(tools,last,'Escape');assert.equal(layout.popup,null);assert.equal(document.activeElement,launcher);assert.equal(launcher.getAttribute('aria-expanded'),'false');
  assert.equal(launcher.getAttribute('aria-controls'),tools.getAttribute('id'));
});
test('floating command navigation keeps movement keys on the grip and removes bindings on contextual suspension',t=>{
  const dom=ribbonDom(t),store=new RibbonCustomization({storage:{getItem:()=>null,setItem(){}}}),layout=new RibbonLayout(dom.panel,{customization:store});dom.disposeWith(()=>layout.dispose());
  const first=command('First'),last=command('Last'),draw=group('Draw',[first,last]),entries=[{key:'Home'}];
  layout.set([draw],{tab:'Home',entries});layout.floatPanel(draw);const frame=[...layout.floating.frames.values()][0];
  frame.keyboard.focus();assert.equal(document.activeElement,first);key(frame.content,first,'End');assert.equal(document.activeElement,last);
  key(frame.content,last,'Escape');assert.equal(document.activeElement,frame.grip);
  const x=frame.position.x;frame.grip.emit('keydown',{key:'ArrowRight',shiftKey:true});assert.equal(frame.position.x,x+1);
  frame.grip.emit('keydown',{key:'Escape'});assert.equal(document.activeElement,dom.tab);
  frame.back.focus();frame.back.emit('keydown',{key:'Escape'});assert.equal(document.activeElement,dom.tab,'the return control also exits the floating panel without canceling a drawing command');
  first.focus();entries[0].visible=false;layout.set([draw],{tab:'Home',entries});assert.equal(document.activeElement,dom.tab);assert.equal(frame.content.handlers.get('keydown').length,0);
});
test('focus leaving a flyout closes it without stealing the destination focus',t=>{
  const dom=ribbonDom(t),layout=new RibbonLayout(dom.panel);dom.disposeWith(()=>layout.dispose());
  const draw=group('Draw',[command('Line')]),outside=command('Elsewhere');dom.host.append(outside);layout.set([draw],{tab:'Home'});layout.open(draw);
  outside.focus();dom.events.emit('focusin',{target:outside});assert.equal(layout.popup,null);assert.equal(document.activeElement,outside);
});

test('a responsive collapse transfers hidden command focus to its launcher and expansion returns to a command',t=>{
  const dom=ribbonDom(t),layout=new RibbonLayout(dom.panel);dom.disposeWith(()=>layout.dispose());
  const first=command('Line'),draw=group('Draw',[first]),launcher=draw.querySelector('.ribbon-launcher');
  first.getClientRects=()=>draw.classList.contains('collapsed')?[]:[first.getBoundingClientRect()];
  launcher.getClientRects=()=>draw.classList.contains('collapsed')?[launcher.getBoundingClientRect()]:[];
  layout.set([draw],{tab:'Home'});layout.layout();first.focus();layout.mode='buttons';layout.layout();assert.equal(document.activeElement,launcher);
  layout.mode='full';layout.layout();assert.equal(document.activeElement,first);
});
