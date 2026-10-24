import {test} from 'node:test';
import assert from 'node:assert/strict';
import {ribbonHostPreferences,ribbonHostRect,ribbonDockTarget,ribbonLock} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-host-model.mjs';
import {ribbonPreferences,changeRibbonPreferences,parseRibbonCustomization} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-customization.mjs';

test('whole-ribbon state is bounded, detached and portable with existing workspace preferences',()=>{
  const raw={placement:'left',orientation:'vertical',dockWidth:900,x:-3,y:1e9,vertical:{width:330,height:400},unknown:'discard'};
  const state=ribbonHostPreferences(raw);assert.equal(state.placement,'left');assert.equal(state.dockWidth,520);assert.equal(state.x,0);assert.equal(state.y,100000);
  raw.vertical.width=999;assert.equal(state.vertical.width,330);assert(!('unknown'in state));
  const preferences=changeRibbonPreferences(null,{type:'host',value:state});assert.deepEqual(parseRibbonCustomization(JSON.stringify(preferences)).host,state);
  assert.equal(ribbonHostPreferences({placement:'bottom'}).placement,'top');assert.equal(ribbonPreferences(null).host.placement,'top');
});
test('floating sizes and offscreen positions retain reachable title controls at compact widths',()=>{
  assert.deepEqual(ribbonHostRect({orientation:'vertical',x:9000,y:9000,vertical:{width:340,height:520}},{width:390,height:650}),{x:46,y:126,width:340,height:520});
  assert.deepEqual(ribbonHostRect({x:9000,y:9000},{width:320,height:160}),{x:4,y:4,width:312,height:152});
});
test('whole-ribbon docking targets exclude compact side docks and honor Ctrl and Allow Docking',()=>{
  const bounds={left:0,top:80,right:1400,bottom:900,width:1400};
  assert.equal(ribbonDockTarget({x:500,y:90},bounds),'top');assert.equal(ribbonDockTarget({x:10,y:500},bounds),'left');assert.equal(ribbonDockTarget({x:1390,y:500},bounds),'right');
  assert.equal(ribbonDockTarget({x:10,y:500},bounds,{ctrlKey:true}),null);assert.equal(ribbonDockTarget({x:10,y:500},bounds,{allowDocking:false}),null);
  assert.equal(ribbonDockTarget({x:10,y:500},{...bounds,width:390,right:390}),null);assert.equal(ribbonDockTarget({x:500,y:940},bounds),null);
});
test('LOCKUI treats docked and floating ribbon positions independently and Ctrl overrides',()=>{
  assert.equal(ribbonLock(1,false),true);assert.equal(ribbonLock(1,true),false);assert.equal(ribbonLock(4,true),true);assert.equal(ribbonLock(4,false),false);
  assert.equal(ribbonLock(15,false,{ctrlKey:true}),false);assert.equal(ribbonLock(10,true),false);
});
test('direct tab and panel reorder preserve dormant identities and pin state',()=>{
  const context={entries:[{key:'Home'},{key:'View'},{key:'Manage'}],panels:[{id:'Draw'},{id:'Modify'},{id:'Layers'}]};
  let value=changeRibbonPreferences(null,{type:'tab-reorder',id:'Manage',beforeId:'Home'},context);
  assert.deepEqual(value.tabs.map(v=>v.id),['Manage','Home','View']);
  value=changeRibbonPreferences(value,{type:'panel-reorder',tab:'Home',id:'Layers',beforeId:'Draw'},context);
  assert.deepEqual(value.panels.map(v=>v.id),['Layers','Draw','Modify']);
  value=changeRibbonPreferences(value,{type:'panel-expanded',tab:'Home',panel:'Modify',expanded:true});
  value=changeRibbonPreferences(value,{type:'panel-expanded',tab:'Home',panel:'Modify',expanded:true});assert.equal(value.expanded.length,1);
  value=changeRibbonPreferences(value,{type:'float',tab:'Home',panel:'Modify',orientation:'vertical'});
  value=changeRibbonPreferences(value,{type:'float',tab:'Home',panel:'Modify',x:100,y:200});assert.equal(value.floating[0].orientation,'vertical');
  value=changeRibbonPreferences(value,{type:'float-orientation',tab:'Home',panel:'Modify',orientation:'horizontal'});assert.equal(value.floating[0].orientation,'horizontal');
  value=changeRibbonPreferences(value,{type:'panel-expanded',tab:'Home',panel:'Modify',expanded:false});assert.equal(value.expanded.length,0);
});
